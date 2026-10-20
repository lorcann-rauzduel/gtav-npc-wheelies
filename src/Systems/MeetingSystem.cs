using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Native;
using GTA.Math;
using GTA.UI;

namespace StreetRiders
{
    /// <summary>
    /// Biker meetings: scheduled (day + hour window) gatherings mustered near a venue that
    /// owns AUTHORED parking points (Position + Heading captured in-game) and AUTHORED
    /// circle points. Each venue is bound to an existing group, so peds/bikes/colors/
    /// relationships/blips are inherited.
    ///
    /// Riders arrive rode in and park at their own captured point via TASK_VEHICLE_PARK
    /// (aligned to the captured heading). They dismount and take one of two on-foot roles,
    /// or ride a freestyle burst:
    ///   - Idle: each rider settles on an authored circle point and FACES the circle center,
    ///     playing the same idle scenarios as in follow mode (_idleScenarios). While settled
    ///     they chat randomly using CHAT_STATE / CHAT_RESP ambient speech;
    ///   - Lean: WORLD_HUMAN_LEANING, one rider stuck to the side of his own bike;
    ///   - FreestyleRiding: fast continuous cruise (TASK_VEHICLE_DRIVE_WANDER at drive speed)
    ///     so wheelies/stunts trigger in bursts without slowing at a target, bounded by the
    ///     tunable FreestyleRadius; drift beyond the radius pulls the rider back inside.
    /// Roles rotate on RoleChangeMinMs/MaxMs: the leaner switches out, circle riders come
    /// and go between on-foot time and freestyle bursts.
    /// ForceActive venues started from the Meetings menu bypass the schedule entirely.
    /// </summary>
    public partial class StreetRidersScript
    {
        // =========================================================================
        //  Main tick (logic throttled ~100ms; wheelie forces run every frame)
        // =========================================================================

        private void UpdateMeetingSystem()
        {
            int now = Game.GameTime;
            if (now - _meetingLastUpdate < 100) return;
            _meetingLastUpdate = now;

            UpdateMeetingScheduler();
            ProcessActiveMeetings();
        }

        private void TickMeetingWheelies()
        {
            // Per-frame pass: only the wheelie physics of freestyle riders (cheap).
            foreach (var venue in _meetingVenues)
            {
                if (!venue.ForceActive) continue;
                ActiveGroup group = FindGroupById(venue.ActiveGroupId);
                if (group == null) continue;

                foreach (int pedHandle in group.PedHandles)
                {
                    RiderState st;
                    if (!_riders.TryGetValue(pedHandle, out st)) continue;
                    MeetingRiderState ms = st.MeetingState;
                    if (ms == null || ms.Phase != MeetingRiderPhase.FreestyleRiding) continue;

                    Ped ped = (Ped)Entity.FromHandle(pedHandle);
                    if (ped == null || !ped.Exists() || !ped.IsAlive || !ped.IsInVehicle()) continue;
                    Vehicle bike = ped.CurrentVehicle;
                    if (bike == null || !bike.Model.IsBike) continue;

                    // Respect "no wheelie during a turn / danger" rule
                    if (!st.DoWheelie || Math.Abs(bike.SteeringAngle) > 6f) continue;

                    float dist = ped.Position.DistanceTo(Game.Player.Character.Position);
                    if (dist > _wheelieActiveDist)
                    {
                        if (st.Phase != Phase.Idle) st.Phase = Phase.Idle;
                        continue;
                    }

                    float speed = bike.Speed;
                    float pitch = bike.Rotation.X;
                    if (speed < 9f)
                    {
                        st.Phase = Phase.Idle;
                        continue;
                    }

                    WheelieConfig cfg = GetWheelieConfig(st.BikeModel, st.WheelieProtected);
                    Phase oldPhase = st.Phase;
                    TickWheelie(bike, st, speed, pitch, cfg);
                    UpdateMeetingWheelieProtection(st, oldPhase, ped);
                }
            }
        }

        private void UpdateMeetingWheelieProtection(RiderState state, Phase oldPhase, Ped ped)
        {
            // Mirrors the WheelieOnly / Always fall-prevention wiring of ProcessAllRiders.
            bool wasWheeling = oldPhase != Phase.Idle;
            bool isWheeling = state.Phase != Phase.Idle;
            if (!wasWheeling && isWheeling)
            {
                if (_preventFallMode == PreventFallMode.Always)
                {
                    state.WheelieProtected = true;
                }
                else if (_preventFallMode == PreventFallMode.WheelieOnly)
                {
                    state.WheelieProtected = _rnd.Next(100) < _preventFallChance;
                    if (state.WheelieProtected)
                        Function.Call(Hash.SET_PED_CAN_BE_KNOCKED_OFF_VEHICLE, ped.Handle, 1);
                }
                else
                {
                    state.WheelieProtected = false;
                }
            }
            else if (wasWheeling && !isWheeling)
            {
                if (state.WheelieProtected)
                {
                    if (_preventFallMode == PreventFallMode.WheelieOnly)
                        Function.Call(Hash.SET_PED_CAN_BE_KNOCKED_OFF_VEHICLE, ped.Handle, 0);
                    state.WheelieProtected = false;
                }
            }
        }

        // =========================================================================
        //  Scheduler (calendar window + player-distance materialization)
        // =========================================================================

        private void UpdateMeetingScheduler()
        {
            int now = Game.GameTime;
            bool enabled = _meetingEnabled && _meetingVenues.Count > 0;

            // Throttle schedule checks: meaningful only when meetings are enabled.
            if (enabled && now - _meetingNextSchedulerCheck < 5000) return;
            _meetingNextSchedulerCheck = now;
            if (!enabled) return;

            Vector3 playerPos = Game.Player.Character.Position;
            int day = Function.Call<int>(Hash.GET_CLOCK_DAY_OF_WEEK);
            int hour = Function.Call<int>(Hash.GET_CLOCK_HOURS);
            bool inWindow = _meetingScheduleDays.Contains(day)
                && IsActiveHour(hour, _meetingStartHour, _meetingEndHour);

            int activeCount = 0;
            foreach (var venue in _meetingVenues)
                if (venue.ForceActive) activeCount++;

            // Materialize venues the player approaches while the schedule is open.
            if (inWindow && activeCount < _meetingMaxConcurrent)
            {
                foreach (var venue in _meetingVenues)
                {
                    if (venue.ForceActive) continue;
                    if (playerPos.DistanceTo(MeetingCenter(venue)) > _meetingMaterializeDist) continue;

                    StartVenueMeeting(venue, false);
                    activeCount++;
                    if (_debug)
                        Notification.PostTicker($"~b~Meetings: {venue.Name} materialized (schedule)", true);
                }
            }

            // Dismantle venues the player left behind (riders ride home).
            foreach (var venue in _meetingVenues)
            {
                if (!venue.ForceActive) continue;
                bool nearVenue = playerPos.DistanceTo(MeetingCenter(venue)) < _meetingMaterializeDist;
                if (!nearVenue)
                {
                    DismantleVenueMeeting(venue, true);
                    if (_debug)
                        Notification.PostTicker($"~b~Meetings: {venue.Name} dismissed (player left)", true);
                }
            }
        }

        // =========================================================================
        //  Meeting start (convoy in) and dismantle (formation out)
        // =========================================================================

        private void StartVenueMeeting(MeetingVenue venue, bool forced)
        {
            if (venue.ForceActive) return;
            GroupConfig groupCfg = FindGroupConfig(venue.GroupName);
            if (groupCfg == null)
            {
                Notification.PostTicker($"~r~Meetings: {venue.Name} has no group '{venue.GroupName}'", true);
                return;
            }

            Vector3 venueCenter = MeetingCenter(venue);
            Vector3 spawnPos;
            float spawnHeading;
            if (!FindRoadNode(venueCenter, 120f, 320f, out spawnPos, out spawnHeading))
            {
                Notification.PostTicker($"~r~Meetings: {venue.Name} — no staging road found", true);
                return;
            }

            int count = _rnd.Next(_meetingRidersMin, _meetingRidersMax + 1);
            int venueIndex = _meetingVenues.IndexOf(venue);

            // --- Load bike models (mixed like SpawnSystem) ---
            bool mixed = groupCfg.MixedVehicles && groupCfg.BikeModels.Count > 1;
            var bikeNames = new List<string>();
            var bikeModels = new List<Model>();
            if (mixed)
            {
                foreach (string name in groupCfg.BikeModels)
                {
                    bikeNames.Add(name);
                    Model m = new Model(name);
                    m.Request();
                    bikeModels.Add(m);
                }
            }
            else
            {
                string name = groupCfg.BikeModels[_rnd.Next(groupCfg.BikeModels.Count)];
                bikeNames.Add(name);
                Model m = new Model(name);
                m.Request();
                bikeModels.Add(m);
            }

            int timeout = Game.GameTime + 5000;
            while (Game.GameTime < timeout)
            {
                bool allLoaded = bikeModels.All(m => m.IsLoaded);
                if (allLoaded) break;
                Wait(0);
            }
            for (int j = bikeModels.Count - 1; j >= 0; j--)
            {
                if (!bikeModels[j].IsLoaded)
                {
                    bikeModels[j].MarkAsNoLongerNeeded();
                    bikeModels.RemoveAt(j);
                    bikeNames.RemoveAt(j);
                }
            }
            if (bikeModels.Count == 0)
            {
                Notification.PostTicker($"~r~Meetings: {venue.Name} — bike models failed to load", true);
                return;
            }

            // --- Load ped models in parallel ---
            var pedEntries = new List<Model>();
            for (int i = 0; i < count; i++)
            {
                string pedName = groupCfg.PedModels[_rnd.Next(groupCfg.PedModels.Count)];
                Model pm = new Model(pedName);
                pm.Request();
                pedEntries.Add(pm);
            }
            timeout = Game.GameTime + 5000;
            while (Game.GameTime < timeout)
            {
                bool allLoaded = pedEntries.All(pm => pm.IsLoaded);
                if (allLoaded) break;
                Wait(0);
            }

            var group = new ActiveGroup
            {
                Id = _nextGroupId++,
                Destination = venueCenter,
                SpawnOrigin = spawnPos,
                IsTerritory = false,
                ConfigName = groupCfg.Name,
                Zones = groupCfg.Zones,
                MeetingVenueIndex = venueIndex
            };

            Vector3 forwardDir = GetHeadingDir(spawnHeading);

            for (int i = 0; i < pedEntries.Count; i++)
            {
                Model pedModel = pedEntries[i];
                if (!pedModel.IsLoaded)
                {
                    pedModel.MarkAsNoLongerNeeded();
                    continue;
                }

                int bikeIdx = mixed ? (i % bikeModels.Count) : 0;
                string bikeModelName = bikeNames[bikeIdx];
                Model bikeModel = bikeModels[bikeIdx];

                Vector3 pos = spawnPos + forwardDir * (-_spawnSpacing * i);
                Vehicle bike = World.CreateVehicle(bikeModel, pos, spawnHeading);
                if (bike == null) { pedModel.MarkAsNoLongerNeeded(); continue; }

                if (groupCfg.BikeColors.Count > 0)
                {
                    int colorId = groupCfg.BikeColors[_rnd.Next(groupCfg.BikeColors.Count)];
                    Function.Call(Hash.SET_VEHICLE_COLOURS, bike.Handle, colorId, colorId);
                }

                Ped ped = World.CreatePed(pedModel, pos);
                if (ped == null) { bike.Delete(); pedModel.MarkAsNoLongerNeeded(); continue; }

                ped.SetIntoVehicle(bike, VehicleSeat.Driver);
                WheelieConfig cfg = GetWheelieConfig(bikeModelName);

                uint relGroup = Function.Call<uint>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle);
                if (!string.IsNullOrEmpty(groupCfg.RelationshipGroup))
                    relGroup = Function.Call<uint>(Hash.GET_HASH_KEY, groupCfg.RelationshipGroup);
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, bike.Handle, true, true);
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, relGroup);
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 0, relGroup, relGroup);

                if (groupCfg.PlayerRelationship >= 0)
                {
                    int playerRelGroup = Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH,
                        Game.Player.Character.Handle);
                    Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS,
                        groupCfg.PlayerRelationship, relGroup, playerRelGroup);
                    Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS,
                        groupCfg.PlayerRelationship, playerRelGroup, relGroup);
                }

                ApplyPedFlags(ped, cfg);
                Function.Call(Hash.SET_ENTITY_MAX_SPEED, bike.Handle, cfg.MaxSpeed);
                Function.Call(Hash.SET_DRIVER_ABILITY, ped.Handle, cfg.DriveAbility);
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, ped.Handle, cfg.DriveAggressiveness);

                if (groupCfg.Weapons.Count > 0)
                {
                    string weapon = groupCfg.Weapons[_rnd.Next(groupCfg.Weapons.Count)];
                    uint weaponHash = Function.Call<uint>(Hash.GET_HASH_KEY, weapon);
                    Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle, weaponHash, 200, false, true);
                }

                // Personal parking point from the authored list — every rider parks at
                // his own captured spot, oriented by the captured heading.
                var ms = new MeetingRiderState
                {
                    VenueIndex = venueIndex,
                    Phase = MeetingRiderPhase.Arriving,
                    ParkingPointIndex = i % venue.ParkingPoints.Count,
                    NextTaskTime = Game.GameTime
                };
                MeetingPoint pp = venue.ParkingPoints[ms.ParkingPointIndex];
                ms.ParkingOffset = pp.Position;
                ms.ParkingHeading = pp.Heading;
                ms.LastTalkTime = Game.GameTime + _rnd.Next(0, 8000); // désynchro de la parlote
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                    ped.Handle, bike.Handle,
                    ms.ParkingOffset.X, ms.ParkingOffset.Y, ms.ParkingOffset.Z,
                    cfg.DriveSpeed * 0.6f, cfg.DrivingFlags, 5f);

                var state = new RiderState
                {
                    BikeModel = bikeModelName.ToUpperInvariant(),
                    GroupId = group.Id,
                    IsSpawned = true,
                    DoWheelie = groupCfg.DoWheelie,
                    DoWheelieStunts = groupCfg.DoWheelieStunts,
                    LastBikeHandle = bike.Handle,
                    PedHandle = ped.Handle,
                    OriginalRelGroup = relGroup,
                    MeetingState = ms,
                    LastMovingTime = Game.GameTime
                };
                _riders[ped.Handle] = state;

                group.PedHandles.Add(ped.Handle);
                group.BikeHandles.Add(bike.Handle);
                if (i == 0) group.LeaderBikeHandle = bike.Handle;
                pedModel.MarkAsNoLongerNeeded();
            }

            foreach (var m in bikeModels) m.MarkAsNoLongerNeeded();

            if (group.PedHandles.Count == 0)
            {
                Notification.PostTicker($"~r~Meetings: {venue.Name} — no riders created", true);
                return;
            }

            _activeGroups.Add(group);
            venue.ActiveGroupId = group.Id;
            venue.ForceActive = true;

            if (_debug)
                Notification.PostTicker($"~g~Meetings: {venue.Name} started ({group.PedHandles.Count}x {groupCfg.Name}){(forced ? " [forced]" : "")}", true);
        }

        private void DismantleVenueMeeting(MeetingVenue venue, bool depart)
        {
            ActiveGroup group = FindGroupById(venue.ActiveGroupId);
            venue.ForceActive = false;
            venue.ActiveGroupId = -1;
            if (group == null) return;

            int now = Game.GameTime;
            for (int i = group.PedHandles.Count - 1; i >= 0; i--)
            {
                Ped ped = (Ped)Entity.FromHandle(group.PedHandles[i]);
                RiderState st;
                if (ped == null || !ped.Exists() || !_riders.TryGetValue(ped.Handle, out st))
                {
                    group.PedHandles.RemoveAt(i);
                    if (i < group.BikeHandles.Count) group.BikeHandles.RemoveAt(i);
                    continue;
                }

                StopWheelieAnimWithBlend(st);
                st.Phase = Phase.Idle;
                st.WheelieAnimPlayed = false;
                st.WheelieAnimTriggerTime = 0;
                st.WheelieNeedsRetask = false;
                if (st.WheelieProtected)
                {
                    Function.Call(Hash.SET_PED_CAN_BE_KNOCKED_OFF_VEHICLE, ped.Handle, 0);
                    st.WheelieProtected = false;
                }
                st.MeetingState = null;

                if (ped.IsInVehicle())
                {
                    Vehicle bike = ped.CurrentVehicle;
                    if (bike != null && bike.Exists())
                        Function.Call(Hash.SET_VEHICLE_HANDBRAKE, bike.Handle, false);
                }
                else
                {
                    Vehicle bike = (Vehicle)Entity.FromHandle(st.LastBikeHandle);
                    if (bike != null && bike.Exists() && !bike.IsDead && !bike.IsInWater)
                    {
                        // Let the normal remount machinery take over and ride home.
                        Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                        Function.Call(Hash.SET_VEHICLE_HANDBRAKE, bike.Handle, false);
                        st.WasInVehicle = false;
                        st.RemountActive = true;
                        st.RemountTimeout = now + 15000;
                        st.RemountLastTaskTime = now - 5000;
                    }
                    else
                    {
                        // No bike left: release this rider back to ambient life.
                        Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                        RemoveRiderBlip(st);
                        ped.MarkAsNoLongerNeeded();
                        _riders.Remove(ped.Handle);
                        group.PedHandles.RemoveAt(i);
                        if (i < group.BikeHandles.Count) group.BikeHandles.RemoveAt(i);
                    }
                }
            }

            group.MeetingVenueIndex = -1;

            if (depart && group.PedHandles.Count > 0)
            {
                Vector3 exitPos;
                float exitHeading;
                if (!FindRoadNode(MeetingCenter(venue), _destDistMin, _destDistMax, out exitPos, out exitHeading))
                    exitPos = MeetingCenter(venue) + GetHeadingDir(0f) * 450f;
                group.Destination = exitPos;
                RetaskEntireGroup(group);
                if (_debug)
                    Notification.PostTicker($"~o~Meetings: {venue.Name} ended, riders leaving in formation", true);
            }
            else if (_debug)
            {
                Notification.PostTicker($"~o~Meetings: {venue.Name} ended", true);
            }
        }

        private void AbortAllMeetings()
        {
            // Deletion of every spawned entity is performed by OnAborted; here we only
            // mark all venues as stopped so the scheduler does not resurrect them.
            foreach (var venue in _meetingVenues)
            {
                venue.ForceActive = false;
                venue.ActiveGroupId = -1;
            }
        }

        // =========================================================================
        //  Active meeting processing (per-venue, all riders)
        // =========================================================================

        private void ProcessActiveMeetings()
        {
            foreach (var venue in _meetingVenues)
            {
                if (!venue.ForceActive) continue;
                ActiveGroup group = FindGroupById(venue.ActiveGroupId);
                if (group == null)
                {
                    venue.ForceActive = false;
                    venue.ActiveGroupId = -1;
                    continue;
                }

                for (int i = group.PedHandles.Count - 1; i >= 0; i--)
                {
                    Ped ped = (Ped)Entity.FromHandle(group.PedHandles[i]);
                    RiderState st;
                    if (ped == null || !ped.Exists() || !_riders.TryGetValue(ped.Handle, out st)
                        || st.MeetingState == null)
                    {
                        // The rider is no longer managed here (dead or handed to the player).
                        group.PedHandles.RemoveAt(i);
                        if (i < group.BikeHandles.Count) group.BikeHandles.RemoveAt(i);
                        continue;
                    }

                    ProcessMeetingRider(venue, group, ped, st, Game.GameTime);
                }

                if (group.PedHandles.Count == 0)
                {
                    venue.ForceActive = false;
                    venue.ActiveGroupId = -1;
                }
            }
        }

        // =========================================================================
        //  Per-rider finite state machine
        // =========================================================================

        private void ProcessMeetingRider(MeetingVenue venue, ActiveGroup group, Ped ped, RiderState st, int now)
        {
            MeetingRiderState ms = st.MeetingState;
            if (ms == null || ms.VenueIndex < 0) return;
            if (!ped.IsAlive || ped.IsRagdoll) return;

            Vehicle bike = ped.CurrentVehicle;
            Vehicle myBike = GetMyBike(st);

            // Bike destroyed/submerged mid-meeting: rider falls back to the on-foot life.
            if (myBike != null && (myBike.IsDead || myBike.IsInWater))
            {
                myBike = null;
            }

            switch (ms.Phase)
            {
                case MeetingRiderPhase.Arriving:
                    if (bike == null || bike.IsDead || bike.IsInWater)
                    {
                        // Still traveling and the bike broke: nothing left to do but wander in.
                        if (TryMountBike(ms, ped, st, now)) break;
                        EnterOnFoot(venue, group, ms, ped, st, now);
                        break;
                    }

                    float parkDist = bike.Position.DistanceTo(ms.ParkingOffset);
                    if (parkDist < 8f && bike.Speed < 2f)
                    {
                        // Reached the spot: engine idles, handbrake holds, rider gets ready.
                        Function.Call(Hash.SET_VEHICLE_HANDBRAKE, bike.Handle, true);
                        ms.Phase = MeetingRiderPhase.ParkedOnBike;
                        ms.HoldUntil = now + _rnd.Next(2000, 6000);
                        if (_debug) DrawDebugText(ped, "MEET: ParkedOnBike", Color.SkyBlue);
                    }
                    else if (now > ms.NextTaskTime)
                    {
                        DriveToParkingOffset(ms, ped, st, now);
                    }
                    break;

                case MeetingRiderPhase.ParkedOnBike:
                    if (bike == null || !ped.IsInVehicle())
                    {
                        EnterOnFoot(venue, group, ms, ped, st, now);
                        break;
                    }
                    if (now >= ms.HoldUntil)
                    {
                        Function.Call(Hash.TASK_LEAVE_VEHICLE, ped.Handle, bike.Handle, 0);
                        ms.Phase = MeetingRiderPhase.Dismounting;
                        ms.HoldUntil = now + 5000;
                    }
                    break;

                case MeetingRiderPhase.Dismounting:
                    if (!ped.IsInVehicle())
                    {
                        EnterOnFoot(venue, group, ms, ped, st, now);
                    }
                    else if (now > ms.HoldUntil)
                    {
                        Function.Call(Hash.TASK_LEAVE_VEHICLE, ped.Handle, bike.Handle, 0);
                        ms.HoldUntil = now + 5000;
                    }
                    break;

                case MeetingRiderPhase.OnFoot:
                    if (ped.IsInVehicle())
                    {
                        if (now > ms.NextTaskTime)
                        {
                            Function.Call(Hash.TASK_LEAVE_VEHICLE, ped.Handle, bike.Handle, 0);
                            ms.NextTaskTime = now + 3000;
                        }
                        break;
                    }
                    UpdateOnFootRole(venue, group, ms, ped, st, now);
                    break;

                case MeetingRiderPhase.Mounting:
                    if (ped.IsInVehicle())
                    {
                        StartFreestyleRiding(venue, ped, st, ms, now);
                    }
                    else if (now >= ms.HoldUntil)
                    {
                        if (!TryMountBike(ms, ped, st, now))
                            EnterOnFoot(venue, group, ms, ped, st, now);
                    }
                    break;

                case MeetingRiderPhase.FreestyleRiding:
                    if (!ped.IsInVehicle())
                    {
                        // Fell off mid-burst: remount if the bike is near, else back on foot.
                        if (!TryMountBike(ms, ped, st, now))
                            EnterOnFoot(venue, group, ms, ped, st, now);
                        break;
                    }
                    UpdateFreestyleRiding(venue, ms, ped, st, now);
                    break;

                case MeetingRiderPhase.Paused:
                    if (!ped.IsInVehicle())
                    {
                        EnterOnFoot(venue, group, ms, ped, st, now);
                        break;
                    }
                    // Breather ends once back at the personal offset: get off and rejoin
                    // the crowd so the on-foot role rotation keeps turning.
                    float backDist = bike.Position.DistanceTo(ms.ParkingOffset);
                    if (backDist < 8f && bike.Speed < 2f)
                    {
                        Function.Call(Hash.SET_VEHICLE_HANDBRAKE, bike.Handle, true);
                        Function.Call(Hash.TASK_LEAVE_VEHICLE, ped.Handle, bike.Handle, 0);
                        ms.Phase = MeetingRiderPhase.Dismounting;
                        ms.HoldUntil = now + 5000;
                    }
                    else if (now > ms.NextTaskTime)
                    {
                        DriveToParkingOffset(ms, ped, st, now);
                    }
                    break;
            }
        }

        private Vehicle GetMyBike(RiderState st)
        {
            Vehicle bike = (Vehicle)Entity.FromHandle(st.LastBikeHandle);
            if (bike != null && bike.Exists() && !bike.IsDead && !bike.IsInWater)
                return bike;
            return null;
        }

        /// <summary>
        /// Tries to get the rider back on their bike. Returns false when the bike is gone.
        /// </summary>
        private bool TryMountBike(MeetingRiderState ms, Ped ped, RiderState st, int now)
        {
            Vehicle myBike = GetMyBike(st);
            if (myBike == null) return false;
            if (ped.Position.DistanceTo(myBike.Position) > 40f) return false;

            if (now > ms.NextTaskTime)
            {
                Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                Function.Call(Hash.TASK_ENTER_VEHICLE, ped.Handle, myBike.Handle, -1, -1, 2.0f, 1, 0);
                ms.NextTaskTime = now + 3000;
            }
            return true;
        }

        // =========================================================================
        //  On-foot roles
        // =========================================================================

        private void EnterOnFoot(MeetingVenue venue, ActiveGroup group, MeetingRiderState ms, Ped ped, RiderState st, int now)
        {
            ms.Phase = MeetingRiderPhase.OnFoot;
            ms.NextTaskTime = now + 300;
            st.Phase = Phase.Idle;
            st.WheelieAnimPlayed = false;
            st.WheelieAnimTriggerTime = 0;
            st.WheelieNeedsRetask = false;

            // Crowd allocation: one single leaner stuck to the side of his bike, everyone
            // else joins the shared circle (slots spread so the ring grows with the crowd).
            int leaners = CountOnFootMode(venue, MeetingOnFootMode.Lean);

            if (leaners < 1)
            {
                ms.OnFootMode = MeetingOnFootMode.Lean;
                ms.ChangeTime = now + _rnd.Next(_meetingRoleChangeMinMs, _meetingRoleChangeMaxMs + 1);
                ms.ScenarioName = "WORLD_HUMAN_LEANING";
                // Side is picked once per role: +1 right / -1 left, computed against the
                // bike heading so he always stands flat against the side of his own bike.
                ms.LeanSideSign = _rnd.Next(100) < 50 ? 1 : -1;
                SetLeanHangout(ms, st);

                Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                if (_debug) DrawDebugText(ped, "MEET: Lean", Color.LightBlue);
                return;
            }

            // Everyone else joins the circle: slot index = current gatherer count, so the
            // authored circle points (or the fallback ring) spread across the crowd.
            ms.OnFootMode = MeetingOnFootMode.Idle;
            ms.ChangeTime = now + _rnd.Next(_meetingRoleChangeMinMs, _meetingRoleChangeMaxMs + 1);
            List<string> pool = _idleScenarios.Count > 0 ? _idleScenarios : new List<string> { "WORLD_HUMAN_SMOKING" };
            ms.ScenarioName = pool[_rnd.Next(pool.Count)];
            ms.CircleSlot = CountOnFootMode(venue, MeetingOnFootMode.Idle);
            ms.HangoutPoint = MeetingCirclePoint(venue, ms.CircleSlot);
            ms.LastTalkTime = now; // chaque motard parle à un rythme indépendant

            Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
        }

        /// <summary>
        /// Circle slot for an on-foot rider. Uses the authored CirclePoints when the venue
        /// has them (each rider gets one of the 8 edited points); otherwise falls back to
        /// evenly spaced slots on a ring in front of the first parking point.
        /// </summary>
        private Vector3 MeetingCirclePoint(MeetingVenue venue, int slot)
        {
            if (venue.CirclePoints.Count > 0)
                return venue.CirclePoints[slot % venue.CirclePoints.Count].Position;

            const int maxSlots = 8;
            const float ringRadius = 4.0f;
            Vector3 center = venue.Center + GetHeadingDir(venue.ParkingPoints[0].Heading) * 7f;
            double ang = (slot % maxSlots) * Math.PI * 2.0 / maxSlots;
            return center + RandomOffset(0.3f, 0.6f)
                + new Vector3((float)Math.Cos(ang) * ringRadius, (float)Math.Sin(ang) * ringRadius, 0f);
        }

        /// <summary>
        /// Geometrical center of the authored circle points (average), or the fallback ring
        /// center if the venue has none — riders face this point so the circle reads inward.
        /// </summary>
        private Vector3 MeetingCircleCenter(MeetingVenue venue)
        {
            if (venue.CirclePoints.Count > 0)
            {
                Vector3 sum = Vector3.Zero;
                foreach (var p in venue.CirclePoints) sum += p.Position;
                return sum / venue.CirclePoints.Count;
            }
            return venue.Center + GetHeadingDir(venue.ParkingPoints[0].Heading) * 7f;
        }

        /// <summary>
        /// Places the leaner flush against the left or right side of his own bike, using
        /// the bike heading so the side offset stays correct whatever direction it faces.
        /// </summary>
        private void SetLeanHangout(MeetingRiderState ms, RiderState st)
        {
            Vehicle bike = GetMyBike(st);
            if (bike == null || !bike.Exists()) return;

            Vector3 forward = GetHeadingDir(bike.Heading);
            // Perpendicular of the bike heading: right side (+), left side (-).
            Vector3 side = new Vector3(-forward.Y, forward.X, 0f) * ms.LeanSideSign;
            // Bike half-width + a small gap so he touches the side without clipping.
            ms.HangoutPoint = bike.Position + side * 1.1f + forward * 1.0f;
        }

        private int CountOnFootMode(MeetingVenue venue, MeetingOnFootMode mode)
        {
            ActiveGroup group = FindGroupById(venue.ActiveGroupId);
            if (group == null) return 0;
            int count = 0;
            foreach (int pedHandle in group.PedHandles)
            {
                RiderState st;
                if (_riders.TryGetValue(pedHandle, out st) && st.MeetingState != null
                    && st.MeetingState.Phase == MeetingRiderPhase.OnFoot
                    && st.MeetingState.OnFootMode == mode)
                    count++;
            }
            return count;
        }

        private void UpdateOnFootRole(MeetingVenue venue, ActiveGroup group, MeetingRiderState ms, Ped ped, RiderState st, int now)
        {
            if (ms.OnFootMode == MeetingOnFootMode.Idle)
            {
                float d = ped.Position.DistanceTo(ms.HangoutPoint);
                if (d > 1.2f)
                {
                    if (now > ms.NextTaskTime)
                    {
                        Function.Call(Hash.TASK_GO_TO_COORD_ANY_MEANS,
                            ped.Handle, ms.HangoutPoint.X, ms.HangoutPoint.Y, ms.HangoutPoint.Z,
                            1.0f, 0, false, 0x42B90000, 0f);
                        ms.NextTaskTime = now + 4000;
                    }
                }
                else
                {
                    // Une fois arrivé à son point de cercle authoré, il regarde vers le centre
                    // puis boucle son scénario idle — le cercle se lit vers l'intérieur.
                    Vector3 center = MeetingCircleCenter(venue);
                    if (ms.ScenarioName.Length > 0
                        // IS_PED_ACTIVE_IN_SCENARIO
                        && !Function.Call<bool>((Hash)0xAA135F9482C82CC3, ped.Handle))
                    {
                        Function.Call(Hash.TASK_TURN_PED_TO_FACE_COORD,
                            ped.Handle, center.X, center.Y, center.Z, 1500);
                        Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, ms.ScenarioName, 0, true);
                    }
                }

                UpdateCircleChatter(ped, ms, now);
            }
            else // Lean
            {
                UpdateLeanOnBike(ms, ped, st, now);
            }

            // Timed decision: re-roll the on-foot crowd or join the freestyle riders.
            if (now >= ms.ChangeTime)
            {
                if (_rnd.Next(100) < 50)
                {
                    EnterOnFoot(venue, group, ms, ped, st, now);
                }
                else
                {
                    Vehicle myBike = GetMyBike(st);
                    if (myBike != null && ped.Position.DistanceTo(myBike.Position) < 60f)
                    {
                        ms.Phase = MeetingRiderPhase.Mounting;
                        ms.HoldUntil = now + 6000;
                        Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                        Function.Call(Hash.TASK_ENTER_VEHICLE, ped.Handle, myBike.Handle, -1, -1, 2.0f, 1, 0);
                        return;
                    }
                    EnterOnFoot(venue, group, ms, ped, st, now);
                }
            }
        }

        /// <summary>
        /// Parlote ambiante du cercle : un motard posé parle de façon aléatoire avec
        /// CHAT_STATE / CHAT_RESP (même principe que le dialogue follow-mode), en
        /// respectant un rythme personnel pour ne pas tous parler en même temps.
        /// </summary>
        private void UpdateCircleChatter(Ped ped, MeetingRiderState ms, int now)
        {
            if (ms.ScenarioName.Length == 0) return;
            if (now - ms.LastTalkTime > _rnd.Next(8000, 15000))
            {
                if (_rnd.Next(100) < 45)
                {
                    string speech = _rnd.Next(100) < 50 ? "CHAT_STATE" : "CHAT_RESP";
                    Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE,
                        ped.Handle, speech, "SPEECH_PARAMS_FORCE_NORMAL");
                    if (_debug) DrawDebugText(ped, "MEET: talk " + speech, Color.LightGoldenrodYellow);
                }
                ms.LastTalkTime = now;
            }
        }

        private void UpdateLeanOnBike(MeetingRiderState ms, Ped ped, RiderState st, int now)
        {
            // Re-compute the spot against the bike side every so often, so the leaner stays
            // glued to the bike even if it shifts a little.
            if (now > ms.NextTaskTime)
            {
                SetLeanHangout(ms, st);
                ms.NextTaskTime = now + 3000;
            }

            float d = ped.Position.DistanceTo(ms.HangoutPoint);
            if (d > 1.8f)
            {
                if (now > ms.NextTaskTime + 3000)
                {
                    Function.Call(Hash.TASK_GO_TO_COORD_ANY_MEANS,
                        ped.Handle, ms.HangoutPoint.X, ms.HangoutPoint.Y, ms.HangoutPoint.Z,
                        1.0f, 0, false, 0x42B90000, 0f);
                }
            }
            else
            {
                // Face the bike so the lean pose reads against the side, then loop it.
                Vehicle bike = GetMyBike(st);
                if (bike != null && bike.Exists())
                    Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, ped.Handle, bike.Handle, 1500);

                if (ms.ScenarioName.Length > 0
                    // IS_PED_ACTIVE_IN_SCENARIO
                    && !Function.Call<bool>((Hash)0xAA135F9482C82CC3, ped.Handle))
                {
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, ms.ScenarioName, 0, true);
                }
            }
        }

        // =========================================================================
        //  Freestyle riding (wheelie bursts around the parking zone, no circuit)
        // =========================================================================

        private void DriveToParkingOffset(MeetingRiderState ms, Ped ped, RiderState st, int now)
        {
            // TASK_VEHICLE_PARK drives to the spot and aligns the bike to the captured heading
            // (mode 1 = park forward). Radius 20 = reach the exact spot before completion.
            Function.Call(Hash.TASK_VEHICLE_PARK,
                ped.Handle, ped.CurrentVehicle.Handle,
                ms.ParkingOffset.X, ms.ParkingOffset.Y, ms.ParkingOffset.Z,
                ms.ParkingHeading, 1, 20f, false);
            ms.NextTaskTime = now + 4000;
        }

        private void StartFreestyleRiding(MeetingVenue venue, Ped ped, RiderState st, MeetingRiderState ms, int now)
        {
            ms.Phase = MeetingRiderPhase.FreestyleRiding;
            ms.ChangeTime = now + _rnd.Next(_meetingFreestyleMinMs, _meetingFreestyleMaxMs + 1);

            Vehicle bike = ped.CurrentVehicle;
            if (bike != null && bike.Exists())
                Function.Call(Hash.SET_VEHICLE_HANDBRAKE, bike.Handle, false);

            st.Phase = Phase.Idle;
            st.WheelieAnimPlayed = false;
            st.WheelieAnimTriggerTime = 0;
            st.WheelieNeedsRetask = false;
            CruiseFreestyle(venue, ms, ped, st, now);
            if (_debug) DrawDebugText(ped, "MEET: Freestyle", Color.Orange);
        }

        /// <summary>
        /// Croisière rapide sans destination (TASK_VEHICLE_DRIVE_WANDER) : le motard roule
        /// en continu à vitesse élevée au lieu de ralentir à l'approche d'un point cible,
        /// ce qui laisse les wheelies se déclencher en rafales sur la zone de freestyle.
        /// </summary>
        private void CruiseFreestyle(MeetingVenue venue, MeetingRiderState ms, Ped ped, RiderState st, int now)
        {
            Vehicle bike = ped.CurrentVehicle;
            if (bike == null || !bike.Exists()) return;
            WheelieConfig cfg = GetWheelieConfig(st.BikeModel);
            Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER,
                ped.Handle, bike.Handle, cfg.DriveSpeed, cfg.DrivingFlags);
            ms.NextTaskTime = now + 3500;
            if (_debug) DrawDebugText(ped, "MEET: cruise", Color.Yellow);
        }

        private void ChooseFreestylePoint(MeetingVenue venue, MeetingRiderState ms)
        {
            // Zone autour du centre du parking.
            ms.FreestylePoint = venue.Center + RandomOffset(_meetingFreestyleRadiusMin, _meetingFreestyleRadius);
        }

        private void DriveToFreestylePoint(MeetingVenue venue, MeetingRiderState ms, Ped ped, RiderState st, int now)
        {
            // Retour dans la zone si la croisière a dérivé au-delà du rayon autorisé.
            WheelieConfig cfg = GetWheelieConfig(st.BikeModel);
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                ped.Handle, ped.CurrentVehicle.Handle,
                ms.FreestylePoint.X, ms.FreestylePoint.Y, ms.FreestylePoint.Z,
                cfg.DriveSpeed, cfg.DrivingFlags, 3f);
            ms.NextTaskTime = now + 3500;
        }

        private void UpdateFreestyleRiding(MeetingVenue venue, MeetingRiderState ms, Ped ped, RiderState st, int now)
        {
            Vehicle bike = ped.CurrentVehicle;
            if (bike == null || !bike.Model.IsBike) return;
            WheelieConfig cfg = GetWheelieConfig(st.BikeModel, st.WheelieProtected);

            // Re-issue the driving task once a picked-up stunt anim has ended (same
            // retask rule as ProcessAllRiders, so freestylers do not coast forever).
            if (st.Phase == Phase.Sustained
                && st.WheelieNeedsRetask
                && !string.IsNullOrEmpty(st.LastWheelieAnimDict)
                && !string.IsNullOrEmpty(st.LastWheelieAnimClip)
                && now - st.WheelieAnimStartTime >= 500
                && !Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM,
                    ped.Handle, st.LastWheelieAnimDict, st.LastWheelieAnimClip, 3))
            {
                CruiseFreestyle(venue, ms, ped, st, now);
                st.WheelieNeedsRetask = false;
                int nextCooldown = st.WheelieAnimUpperBodyOnly ? 2000 : cfg.StuntCooldown;
                st.WheelieAnimUpperBodyOnly = false;
                st.WheelieAnimPlayed = false;
                st.WheelieAnimTriggerTime = now + nextCooldown;
            }

            // Si la croisière a dérivé hors de la zone, on revient à un point intérieur,
            // sinon on garde la vitesse en réémettant la croisière régulièrement.
            float distCenter = bike.Position.DistanceTo(venue.Center);
            if (distCenter > _meetingFreestyleRadius)
            {
                if (now > ms.NextTaskTime)
                {
                    ChooseFreestylePoint(venue, ms);
                    DriveToFreestylePoint(venue, ms, ped, st, now);
                }
            }
            else if (now > ms.NextTaskTime)
            {
                CruiseFreestyle(venue, ms, ped, st, now);
            }

            // Burst over: ride back to the personal parking spot and hold for the dismount.
            if (now >= ms.ChangeTime)
            {
                st.Phase = Phase.Idle;
                st.WheelieAnimPlayed = false;
                st.WheelieAnimTriggerTime = 0;
                st.WheelieNeedsRetask = false;
                ms.Phase = MeetingRiderPhase.Paused;
                DriveToParkingOffset(ms, ped, st, now);
                if (_debug) DrawDebugText(ped, "MEET: Paused", Color.Gray);
            }
        }

        // =========================================================================
        //  Shared helpers
        // =========================================================================

        /// <summary>
        /// Uniform random offset in [min, max] from the origin (squared distance ring).
        /// </summary>
        private Vector3 RandomOffset(float min, float max)
        {
            float ang = (float)(_rnd.NextDouble() * Math.PI * 2);
            float dist = min + (float)(_rnd.NextDouble() * Math.Max(0f, max - min));
            return new Vector3((float)Math.Cos(ang) * dist, (float)Math.Sin(ang) * dist, 0f);
        }

        private bool IsGroupOnActiveMeeting(string groupName)
        {
            foreach (var venue in _meetingVenues)
            {
                if (venue.ForceActive && venue.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private ActiveGroup FindGroupById(int id)
        {
            foreach (var group in _activeGroups)
                if (group.Id == id) return group;
            return null;
        }

        private Vector3 MeetingCenter(MeetingVenue venue)
        {
            return venue.Center;
        }
    }
}
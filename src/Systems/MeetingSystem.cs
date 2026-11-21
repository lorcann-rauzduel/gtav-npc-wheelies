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
    /// Meetings de motards : orchestration des lieux (planning jour + plage horaire,
    /// matérialisation à l'approche du joueur, démarrage forcé depuis le menu), arrivée du
    /// convoi, départ en formation et passe wheelie par frame des motards en freestyle.
    /// Chaque lieu est lié à un groupe existant (peds, motos, couleurs, relations, blips).
    /// Le comportement individuel des motards est dans MeetingRiderBehavior.cs,
    /// l'édition des lieux dans MeetingEditor.cs.
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
                    if (activeCount >= _meetingMaxConcurrent) break;
                    if (venue.ForceActive || venue == _editVenue || now < venue.NextScheduleAttempt) continue;
                    if (playerPos.DistanceTo(MeetingCenter(venue)) > _meetingMaterializeDist) continue;

                    LogMeeting($"Scheduler: materializing {venue.Name} (day={day} hour={hour})");
                    if (!StartVenueMeeting(venue, false))
                    {
                        // Échec (groupe absent, pas de route...) : on ne réessaie pas à chaque passage.
                        venue.NextScheduleAttempt = now + 60000;
                        continue;
                    }
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
                    LogMeeting($"Scheduler: {venue.Name} dismissed (player left)");
                    DismantleVenueMeeting(venue, true);
                    if (_debug)
                        Notification.PostTicker($"~b~Meetings: {venue.Name} dismissed (player left)", true);
                }
            }
        }

        // =========================================================================
        //  Meeting start (convoy in) and dismantle (formation out)
        // =========================================================================

        private bool StartVenueMeeting(MeetingVenue venue, bool forced)
        {
            if (venue.ForceActive) return false;
            GroupConfig groupCfg = FindGroupConfig(venue.GroupName);
            if (groupCfg == null)
            {
                return FailVenueStart(venue, forced, $"group '{venue.GroupName}' missing", T($"groupe '{venue.GroupName}' introuvable dans groups.ini", $"group '{venue.GroupName}' not found in groups.ini"));
            }

            if (venue.ParkingPoints.Count == 0)
            {
                return FailVenueStart(venue, forced, "no parking spot",
                    T("aucune place de parking : pose-en via Modifier au sol", "no parking spot: place some with Edit on the ground"));
            }

            Vector3 venueCenter = MeetingCenter(venue);
            Vector3 spawnPos;
            float spawnHeading;
            if (!FindRoadNode(venueCenter, 120f, 320f, out spawnPos, out spawnHeading))
            {
                return FailVenueStart(venue, forced, "no staging road node", T("aucune route proche pour faire arriver le convoi", "no nearby road for the convoy to arrive"));
            }

            int wanted = _rnd.Next(venue.RidersMin ?? _meetingRidersMin, (venue.RidersMax ?? _meetingRidersMax) + 1);
            List<MeetingPoint> spots = BuildParkingSpots(venue, wanted);
            int count = spots.Count;
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
                return FailVenueStart(venue, forced, "bike models failed to load", T("modèles de motos introuvables (groups.ini)", "bike models failed to load (groups.ini)"));
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

                // Une place unique par motard (authorée ou générée), orientée par son cap.
                var ms = new MeetingRiderState
                {
                    VenueIndex = venueIndex,
                    Phase = MeetingRiderPhase.Arriving,
                    ParkingPointIndex = i,
                    NextTaskTime = Game.GameTime
                };
                MeetingPoint pp = spots[i];
                ms.ParkingOffset = pp.Position;
                ms.ParkingHeading = pp.Heading;
                ms.ApproachPoint = ApproachPointFor(pp.Position, pp.Heading);
                ms.LastTalkTime = Game.GameTime + _rnd.Next(0, 8000); // désynchro de la parlote
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                    ped.Handle, bike.Handle,
                    ms.ApproachPoint.X, ms.ApproachPoint.Y, ms.ApproachPoint.Z,
                    cfg.DriveSpeed * 0.6f, cfg.DrivingFlags, 2f);

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
                return FailVenueStart(venue, forced, "no rider created", T("aucun motard n'a pu être créé", "no rider could be created"));
            }

            _activeGroups.Add(group);
            venue.ActiveGroupId = group.Id;
            venue.ForceActive = true;

            LogMeeting($"Meeting {venue.Name}: started {group.PedHandles.Count}x {groupCfg.Name} forced={forced} parking={venue.ParkingPoints.Count} circle={venue.CircleCenter.HasValue} lines={venue.Lines.Count}");
            if (_debug)
                Notification.PostTicker($"~g~Meetings: {venue.Name} started ({group.PedHandles.Count}x {groupCfg.Name}){(forced ? " [forced]" : "")}", true);
            return true;
        }

        /// <summary>
        /// Une place par motard. En mode Limit, l'effectif est plafonné aux places authorées ;
        /// en mode AutoSpots, les places manquantes prolongent la dernière rangée (même
        /// espacement et même cap que les deux dernières places, ou à côté de l'unique place).
        /// </summary>
        private List<MeetingPoint> BuildParkingSpots(MeetingVenue venue, int wanted)
        {
            var spots = new List<MeetingPoint>(venue.ParkingPoints);
            MeetingExtraRiders mode = venue.ExtraRiders ?? _meetingExtraRiders;
            if (wanted <= spots.Count)
            {
                spots.RemoveRange(wanted, spots.Count - wanted);
                LogMeeting($"Meeting {venue.Name}: {wanted} riders on {venue.ParkingPoints.Count} spots");
                return spots;
            }
            if (mode == MeetingExtraRiders.Limit)
            {
                LogMeeting($"Meeting {venue.Name}: riders limited {wanted} -> {spots.Count} (one per spot)");
                return spots;
            }

            MeetingPoint last = spots[spots.Count - 1];
            Vector3 step;
            if (spots.Count >= 2)
            {
                Vector3 delta = last.Position - spots[spots.Count - 2].Position;
                delta.Z = 0f;
                float spacing = Math.Max(1.2f, Math.Min(3.5f, delta.Length()));
                step = delta.Length() > 0.01f ? Vector3.Normalize(delta) * spacing : Vector3.Zero;
            }
            else
            {
                step = Vector3.Zero;
            }
            if (step == Vector3.Zero)
            {
                // Une seule place : on se range à droite de la moto, épaule contre épaule.
                Vector3 forward = GetHeadingDir(last.Heading);
                step = new Vector3(forward.Y, -forward.X, 0f) * 1.8f;
            }

            int generated = wanted - spots.Count;
            for (int k = 1; k <= generated; k++)
                spots.Add(new MeetingPoint { Position = last.Position + step * k, Heading = last.Heading });
            LogMeeting($"Meeting {venue.Name}: {generated} auto spots generated after P{venue.ParkingPoints.Count} (step={step.Length():0.0}m)");
            return spots;
        }

        /// <summary>
        /// Échec de démarrage : toujours journalisé ; le joueur n'est prévenu que s'il a
        /// lui-même lancé le meetup (le planning échoue en silence et réessaie plus tard).
        /// </summary>
        private bool FailVenueStart(MeetingVenue venue, bool forced, string logReason, string playerReason)
        {
            LogMeeting($"Meeting {venue.Name}: start failed ({logReason}) forced={forced}");
            if (forced)
                Notification.PostTicker(T($"~r~Meetings : {venue.Name} — {playerReason}", $"~r~Meetings: {venue.Name} — {playerReason}"), true);
            return false;
        }

        private void DismantleVenueMeeting(MeetingVenue venue, bool depart)
        {
            ActiveGroup group = FindGroupById(venue.ActiveGroupId);
            LogMeeting($"Meeting {venue.Name}: dismantle depart={depart} riders={(group != null ? group.PedHandles.Count : 0)}");
            venue.ForceActive = false;
            venue.ActiveGroupId = -1;
            venue.CircleLayoutKey = "";
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
                if (st.MeetingState != null) ReleaseMeetingSpeedCap(st.MeetingState, GetMyBike(st), st);
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
                venue.CircleLayoutKey = "";
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
                    venue.CircleLayoutKey = "";
                    continue;
                }

                UpdateCircleLayout(venue, group);

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
                    venue.CircleLayoutKey = "";
                }
            }
        }

        // =========================================================================
        //  Shared helpers
        // =========================================================================

        /// <summary>
        /// Avant un rechargement de la config : on quitte l'édition et on renvoie les meetups
        /// en cours en formation, car les objets lieux vont être recréés depuis meetings.ini.
        /// </summary>
        private void PrepareMeetingsForReload()
        {
            if (_meetingVenues == null) return;
            EndMeetingEdit("config reload");
            HideMeetingMenu();
            foreach (var venue in _meetingVenues)
            {
                if (!venue.ForceActive) continue;
                LogMeeting($"Meeting {venue.Name}: dismantled for config reload");
                DismantleVenueMeeting(venue, true);
            }
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

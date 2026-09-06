using System.Collections.Generic;
using GTA;
using GTA.Native;
using GTA.Math;
using GTA.UI;

namespace StreetRiders
{
    /// <summary>
    /// Cleanup: distance-based entity removal, leader promotion, destination refresh,
    /// group retasking, police release, and entity deletion helpers.
    /// </summary>
    public partial class StreetRidersScript
    {
        private void CleanupAll()
        {
            Vector3 playerPos = Game.Player.Character.Position;

            // Natural riders: stop tracking
            List<int> removeNatural = new List<int>();
            foreach (var kv in _riders)
            {
                if (kv.Value.IsSpawned) continue;
                Entity e = Entity.FromHandle(kv.Key);
                if (e == null || !e.Exists() || e.Position.DistanceTo(playerPos) > _cleanupDist)
                    removeNatural.Add(kv.Key);
            }
            foreach (int h in removeNatural) _riders.Remove(h);

            // Spawned groups: delete entities
            for (int g = _activeGroups.Count - 1; g >= 0; g--)
            {
                var group = _activeGroups[g];
                bool anyAlive = false;

                for (int i = group.PedHandles.Count - 1; i >= 0; i--)
                {
                    int pedHandle = group.PedHandles[i];
                    Ped ped = (Ped)Entity.FromHandle(pedHandle);
                    bool remove = false;

                    if (ped == null || !ped.Exists() || !ped.IsAlive)
                    {
                        // Rider dead/gone: release cops that were pursuing
                        if (ped != null && ped.Exists())
                            ReleasePursuingCops(ped.Handle);
                        remove = true;
                    }
                    else if (ped.Position.DistanceTo(playerPos) > _cleanupDist)
                    {
                        ReleasePursuingCops(ped.Handle);
                        remove = true;
                        DeletePedAndBike(ped);
                    }
                    else if (ped.IsInVehicle())
                    {
                        Vehicle v = ped.CurrentVehicle;
                        if (v != null && (v.IsDead || v.IsInWater))
                        {
                            // Bike destroyed or in water: release naturally
                            ReleasePursuingCops(ped.Handle);
                            ped.Task.LeaveVehicle();
                            ped.Task.Wander();
                            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                            ped.MarkAsNoLongerNeeded();
                            v.MarkAsNoLongerNeeded();
                            remove = true;
                            if (_debug)
                                Notification.PostTicker($"~o~Riders: cleanup - bike {(v.IsInWater ? "submerged" : "wrecked")}", true);
                        }
                    }

                    if (remove)
                    {
                        RiderState removeState;
                        if (_riders.TryGetValue(pedHandle, out removeState))
                            RemoveRiderBlip(removeState);
                        _riders.Remove(pedHandle);
                        group.PedHandles.RemoveAt(i);
                        if (i < group.BikeHandles.Count)
                        {
                            Vehicle bike = (Vehicle)Entity.FromHandle(group.BikeHandles[i]);
                            if (bike != null && bike.Exists())
                            {
                                Function.Call(Hash.SET_VEHICLE_HANDBRAKE, bike.Handle, false);
                                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, bike.Handle, true, true, false);
                                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, bike.Handle, false, true);

                                if (bike.Position.DistanceTo(playerPos) > _cleanupDist)
                                    bike.Delete();
                                else
                                    bike.MarkAsNoLongerNeeded();
                            }
                            if (ped != null && ped.Exists())
                            {
                                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, false, true);
                                ped.MarkAsNoLongerNeeded();
                            }
                            group.BikeHandles.RemoveAt(i);
                        }
                    }
                    else anyAlive = true;
                }

                if (!anyAlive)
                {
                    foreach (int bh in group.BikeHandles)
                    {
                        Vehicle v = (Vehicle)Entity.FromHandle(bh);
                        if (v != null && v.Exists())
                        {
                            Function.Call(Hash.SET_VEHICLE_HANDBRAKE, v.Handle, false);
                            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v.Handle, true, true, false);
                            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, v.Handle, false, true);
                            v.MarkAsNoLongerNeeded();
                        }
                    }
                    _activeGroups.RemoveAt(g);
                }
                else
                {
                    // Check if the current leader is still functional
                    bool needNewLeader = false;
                    Vehicle leaderBike = (Vehicle)Entity.FromHandle(group.LeaderBikeHandle);

                    if (leaderBike == null || !leaderBike.Exists())
                    {
                        needNewLeader = true;
                    }
                    else
                    {
                        Ped driver = leaderBike.Driver;
                        if (driver == null || !driver.Exists() || !driver.IsAlive
                            || driver.IsRagdoll || !driver.IsInVehicle())
                        {
                            needNewLeader = true;
                        }
                    }

                    if (needNewLeader)
                    {
                        if (group.LeaderLostTime == 0)
                        {
                            // First tick the leader looks invalid — start the grace timer
                            group.LeaderLostTime = Game.GameTime;
                        }
                        else if (Game.GameTime - group.LeaderLostTime >= _leaderGracePeriodMs)
                        {
                            // Leader has been invalid for long enough — actually promote
                            group.LeaderLostTime = 0;
                            if (_debug)
                                Notification.PostTicker($"~o~Riders: {group.ConfigName} leader lost, promoting...", true);
                            PromoteNewLeader(group);
                        }
                        // else: still within grace period, keep the current leader handle
                    }
                    else
                    {
                        // Leader recovered — reset the timer
                        group.LeaderLostTime = 0;
                    }
                }
            }
        }

        private void PromoteNewLeader(ActiveGroup group)
        {
            if (group.PedHandles.Count == 0) return;

            // Find first alive ped that is riding a bike
            Ped newLeader = null;
            int leaderIndex = -1;
            for (int i = 0; i < group.PedHandles.Count; i++)
            {
                Ped p = (Ped)Entity.FromHandle(group.PedHandles[i]);
                if (p != null && p.Exists() && p.IsAlive && p.IsInVehicle() && !p.IsRagdoll)
                {
                    newLeader = p;
                    leaderIndex = i;
                    break;
                }
            }

            if (newLeader == null) return;

            Vehicle newBike = newLeader.CurrentVehicle;
            if (newBike == null || !newBike.Exists()) return;

            group.LeaderBikeHandle = newBike.Handle;

            RiderState ls;
            _riders.TryGetValue(newLeader.Handle, out ls);
            WheelieConfig cfg = GetWheelieConfig(ls?.BikeModel ?? "");

            // Leader drives to destination
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                newLeader.Handle, newBike.Handle,
                group.Destination.X, group.Destination.Y, group.Destination.Z,
                cfg.DriveSpeed, cfg.DrivingFlags, 10f);

            // All other alive riders escort the new leader
            for (int i = 0; i < group.PedHandles.Count; i++)
            {
                if (i == leaderIndex) continue;

                Ped f = (Ped)Entity.FromHandle(group.PedHandles[i]);
                if (f == null || !f.Exists() || !f.IsAlive || !f.IsInVehicle()) continue;

                Vehicle fb = f.CurrentVehicle;
                RiderState fs;
                _riders.TryGetValue(f.Handle, out fs);
                WheelieConfig fc = GetWheelieConfig(fs?.BikeModel ?? "");

                Function.Call((Hash)0xFC545A9F0626E3B6, // TASK_VEHICLE_FOLLOW
                    f.Handle, fb.Handle, newBike.Handle,
                    fc.DriveSpeed, fc.DrivingFlags, _formationDistance);
            }

            if (_debug)
                Notification.PostTicker($"~g~Riders: {group.ConfigName} new leader promoted!", true);
        }

        private void RefreshDestinations()
        {
            foreach (var group in _activeGroups)
            {
                if (group.PedHandles.Count == 0) continue;

                // Don't refresh destinations for groups following the player
                if (group.FollowMode != GroupFollowMode.Autonomous) continue;

                Ped leader = null;
                foreach (int ph in group.PedHandles)
                {
                    Ped p = (Ped)Entity.FromHandle(ph);
                    if (p != null && p.Exists() && p.IsInVehicle()
                        && p.CurrentVehicle.Handle == group.LeaderBikeHandle)
                    { leader = p; break; }
                }

                if (leader == null) continue;
                if (leader.Position.DistanceTo(group.Destination) > 50f) continue;

                // Pick new destination based on Territory/GoAway mode
                Vector3 newDest; float newHead;
                bool found;

                if (group.IsTerritory)
                {
                    // Territory: new destination near spawn origin, validated against group zones
                    GroupConfig gcfg = FindGroupConfig(group.ConfigName);
                    float radius = gcfg != null && gcfg.TerritoryRadius > 0 ? gcfg.TerritoryRadius : 150f;
                    found = FindRoadNodeInZone(group.SpawnOrigin, radius * 0.3f, radius,
                        group.Zones, out newDest, out newHead);
                }
                else
                {
                    // GoAway: new destination far from leader
                    found = FindRoadNode(leader.Position, _destDistMin, _destDistMax, out newDest, out newHead);
                }

                if (found)
                {
                    group.Destination = newDest;
                    RetaskEntireGroup(group);
                    if (_debug) Notification.PostTicker($"~b~Riders: {group.ConfigName} new dest ({(group.IsTerritory ? "Territory" : "GoAway")})", true);
                }
            }
        }

        private void RetaskEntireGroup(ActiveGroup group)
        {
            for (int i = 0; i < group.PedHandles.Count; i++)
            {
                Ped ped = (Ped)Entity.FromHandle(group.PedHandles[i]);
                if (ped == null || !ped.Exists() || !ped.IsInVehicle()) continue;

                Vehicle bike = ped.CurrentVehicle;
                RiderState st;
                _riders.TryGetValue(ped.Handle, out st);
                WheelieConfig cfg = GetWheelieConfig(st?.BikeModel ?? "");

                if (bike.Handle == group.LeaderBikeHandle)
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                        ped.Handle, bike.Handle,
                        group.Destination.X, group.Destination.Y, group.Destination.Z,
                        cfg.DriveSpeed, cfg.DrivingFlags, 10f);
                else
                {
                    Vehicle lb = (Vehicle)Entity.FromHandle(group.LeaderBikeHandle);
                    if (lb != null && lb.Exists())
                        Function.Call((Hash)0xFC545A9F0626E3B6, // TASK_VEHICLE_FOLLOW
                            ped.Handle, bike.Handle, lb.Handle,
                            cfg.DriveSpeed, cfg.DrivingFlags, _formationDistance);
                }
                Function.Call(Hash.SET_ENTITY_MAX_SPEED, bike.Handle, cfg.MaxSpeed);
            }
        }

        // =========================================================================
        //  Release cops pursuing a dead/removed rider
        // =========================================================================

        /// <summary>
        /// Releases the cops Riders itself assigned to chase the given rider, and only
        /// those. The old version swept every cop ped within 120 m and cleared his task
        /// + unblocked his non-temporal events even when Riders had never tasked him —
        /// which broke the scripted role another mod (e.g. a scripted arrest transport
        /// driver sitting in a locked hold) had given him. Cops are only ever reset when
        /// CopPursuitInfo proves WE assigned this exact rider to them.
        /// </summary>
        private void ReleasePursuingCops(int riderHandle)
        {
            // Snapshot before mutating the dict while iterating it
            List<int> pursuing = new List<int>();
            foreach (var kv in _copPursuits)
            {
                if (kv.Value.TargetRiderHandle == riderHandle)
                    pursuing.Add(kv.Key);
            }

            foreach (int copH in pursuing)
            {
                _copPursuits.Remove(copH);

                Ped cop = (Ped)Entity.FromHandle(copH);
                if (cop == null || !cop.Exists() || !cop.IsAlive) continue;

                // Clear pursuit task so the cop doesn't stay stuck
                Function.Call(Hash.CLEAR_PED_TASKS, cop.Handle);
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, cop.Handle, false);

                // Turn off sirens on cop vehicle
                if (cop.IsInVehicle())
                {
                    Vehicle copCar = cop.CurrentVehicle;
                    if (copCar != null && copCar.Exists())
                    {
                        Function.Call(Hash.SET_VEHICLE_SIREN, copCar.Handle, false);
                        Function.Call(Hash.SET_VEHICLE_HAS_BEEN_OWNED_BY_PLAYER, copCar.Handle, false);
                    }
                }

                // Let the cop return to normal ambient behavior
                cop.MarkAsNoLongerNeeded();

                if (_debug)
                    Notification.PostTicker("~b~Riders: Cop released from pursuit (target gone)", true);
            }
        }

        private static void DeletePedAndBike(Ped ped)
        {
            if (ped.IsInVehicle())
            {
                Vehicle v = ped.CurrentVehicle;
                ped.Delete();
                if (v != null && v.Exists()) v.Delete();
            }
            else ped.Delete();
        }
    }
}

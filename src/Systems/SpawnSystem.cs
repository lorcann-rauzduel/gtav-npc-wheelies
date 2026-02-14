using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using GTA.Math;
using GTA.UI;

namespace StreetRiders
{
    /// <summary>
    /// Group spawning: zone-filtered auto-spawn, force spawn (test), model loading,
    /// ped/vehicle creation, road-node helpers.
    /// </summary>
    public partial class StreetRidersScript
    {
        // =========================================================================
        //  Zone-Filtered Spawning
        // =========================================================================

        /// <summary>
        /// Checks if the given hour is within the group's active time window.
        /// Supports wrapping around midnight (e.g. 18 to 4).
        /// </summary>
        private static bool IsActiveHour(int currentHour, int start, int end)
        {
            if (start <= end)
                return currentHour >= start && currentHour <= end;
            else
                // Wraps around midnight (e.g. 18 to 4)
                return currentHour >= start || currentHour <= end;
        }

        // =========================================================================
        //  Force Spawn (test mode) - bypasses zone, hour, cooldown, max groups
        // =========================================================================

        /// <summary>
        /// Force-spawns the group at the given index (cycles through groups).
        /// Ignores all restrictions for easy testing.
        /// </summary>
        private void ForceSpawnGroup()
        {
            if (_groupConfigs.Count == 0)
            {
                Notification.PostTicker("~r~Riders TEST: No groups configured!", true);
                return;
            }

            // Cycle through groups
            if (_forceSpawnGroupIndex >= _groupConfigs.Count)
                _forceSpawnGroupIndex = 0;

            GroupConfig groupCfg = _groupConfigs[_forceSpawnGroupIndex];
            _forceSpawnGroupIndex++;

            Vector3 playerPos = Game.Player.Character.Position;

            Vector3 spawnPos;
            float spawnHeading;
            if (!FindRoadNode(playerPos, 30f, 60f, out spawnPos, out spawnHeading))
            {
                Notification.PostTicker("~r~Riders TEST: No road node found nearby!", true);
                return;
            }

            Vector3 destination;
            float destHeading;
            if (!FindRoadNode(spawnPos, _destDistMin, _destDistMax, out destination, out destHeading))
                destination = spawnPos + GetHeadingDir(spawnHeading) * 500f;

            int count = _rnd.Next(_ridersMin, _ridersMax + 1);

            // --- Load bike model(s) ---
            bool mixed = groupCfg.MixedVehicles && groupCfg.BikeModels.Count > 1;
            var bikeNames = new List<string>();
            var bikeModels = new List<Model>();

            if (mixed)
            {
                // Mixed: load ALL bike models from the group
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
                // Classic: pick one random model for the whole group
                string name = groupCfg.BikeModels[_rnd.Next(groupCfg.BikeModels.Count)];
                bikeNames.Add(name);
                Model m = new Model(name);
                m.Request();
                bikeModels.Add(m);
            }

            int timeout = Game.GameTime + 5000;
            while (Game.GameTime < timeout)
            {
                bool allLoaded = true;
                foreach (var m in bikeModels) if (!m.IsLoaded) { allLoaded = false; break; }
                if (allLoaded) break;
                Wait(0);
            }

            // Remove any that failed to load
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
                Notification.PostTicker($"~r~Riders TEST: All bike models FAILED to load!", true);
                return;
            }

            string displayBikes = mixed ? string.Join("/", bikeNames) : bikeNames[0];
            Notification.PostTicker($"~y~Riders TEST: Spawning ~w~{groupCfg.Name}~y~ ({count}x {displayBikes}) close to you...", true);

            // Load ped models
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
                bool allLoaded = true;
                foreach (var pm in pedEntries)
                    if (!pm.IsLoaded) { allLoaded = false; break; }
                if (allLoaded) break;
                Wait(0);
            }

            var group = new ActiveGroup
            {
                Id = _nextGroupId++,
                Destination = destination,
                ConfigName = groupCfg.Name
            };

            Vector3 forwardDir = GetHeadingDir(spawnHeading);
            Vehicle leaderBike = null;

            for (int i = 0; i < pedEntries.Count; i++)
            {
                Model pedModel = pedEntries[i];
                if (!pedModel.IsLoaded)
                {
                    pedModel.MarkAsNoLongerNeeded();
                    continue;
                }

                // Pick bike model: round-robin if mixed, otherwise the single model
                int bikeIdx = mixed ? (i % bikeModels.Count) : 0;
                string bikeModelName = bikeNames[bikeIdx];
                Model bikeModel = bikeModels[bikeIdx];

                Vector3 pos = spawnPos + forwardDir * (-_spawnSpacing * i);

                Vehicle bike = World.CreateVehicle(bikeModel, pos, spawnHeading);
                if (bike == null) { pedModel.MarkAsNoLongerNeeded(); continue; }

                // Apply color if configured
                if (groupCfg.BikeColors.Count > 0)
                {
                    int colorId = groupCfg.BikeColors[_rnd.Next(groupCfg.BikeColors.Count)];
                    Function.Call(Hash.SET_VEHICLE_COLOURS, bike.Handle, colorId, colorId);
                }

                Ped ped = World.CreatePed(pedModel, pos);
                if (ped == null) { bike.Delete(); pedModel.MarkAsNoLongerNeeded(); continue; }

                ped.SetIntoVehicle(bike, VehicleSeat.Driver);
                WheelieConfig cfg = GetWheelieConfig(bikeModelName);

                // Preserve original relationship group, set as mission entity, then restore
                uint relGroup = Function.Call<uint>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle);
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, bike.Handle, true, true);
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, relGroup);
                // Ensure same-group riders are companions (prevents friendly fire)
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 0, relGroup, relGroup);

                ApplyPedFlags(ped, cfg);
                Function.Call(Hash.SET_ENTITY_MAX_SPEED, bike.Handle, cfg.MaxSpeed);

                // Give weapon from group config
                if (groupCfg.Weapons.Count > 0)
                {
                    string weapon = groupCfg.Weapons[_rnd.Next(groupCfg.Weapons.Count)];
                    uint weaponHash = Function.Call<uint>(Hash.GET_HASH_KEY, weapon);
                    Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle, weaponHash, 200, false, true);
                }

                if (i == 0)
                {
                    leaderBike = bike;
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                        ped.Handle, bike.Handle,
                        destination.X, destination.Y, destination.Z,
                        cfg.DriveSpeed, cfg.DrivingFlags, 10f);
                }
                else if (leaderBike != null)
                {
                    Function.Call((Hash)0xFC545A9F0626E3B6, // TASK_VEHICLE_FOLLOW
                        ped.Handle, bike.Handle, leaderBike.Handle,
                        cfg.DriveSpeed, cfg.DrivingFlags, _followMinDistance);
                }

                _riders[ped.Handle] = new RiderState
                {
                    BikeModel = bikeModelName.ToUpperInvariant(),
                    GroupId = group.Id,
                    IsSpawned = true,
                    DoWheelie = groupCfg.DoWheelie,
                    LastBikeHandle = bike.Handle,
                    PedHandle = ped.Handle,
                    OriginalRelGroup = relGroup
                };

                group.PedHandles.Add(ped.Handle);
                group.BikeHandles.Add(bike.Handle);
                if (i == 0) group.LeaderBikeHandle = bike.Handle;
                pedModel.MarkAsNoLongerNeeded();
            }

            foreach (var m in bikeModels) m.MarkAsNoLongerNeeded();

            if (group.PedHandles.Count > 0)
            {
                _activeGroups.Add(group);
                Notification.PostTicker($"~g~Riders TEST: {group.PedHandles.Count}x ~w~{groupCfg.Name}~g~ spawned! (group {_activeGroups.Count})", true);
            }
            else
            {
                Notification.PostTicker("~r~Riders TEST: Spawn failed - no peds created!", true);
            }
        }

        /// <summary>
        /// Deletes all spawned riders and clears tracking. For testing.
        /// </summary>
        private void CleanAllRiders()
        {
            int pedCount = 0;
            foreach (var group in _activeGroups)
            {
                foreach (int ph in group.PedHandles)
                {
                    Ped p = (Ped)Entity.FromHandle(ph);
                    if (p != null && p.Exists())
                    {
                        ReleasePursuingCops(p.Position);
                        DeletePedAndBike(p);
                        pedCount++;
                    }
                }
                foreach (int bh in group.BikeHandles)
                {
                    Vehicle v = (Vehicle)Entity.FromHandle(bh);
                    if (v != null && v.Exists()) v.Delete();
                }
            }
            _activeGroups.Clear();

            // Also clean natural rider tracking
            _riders.Clear();

            Notification.PostTicker($"~o~Riders TEST: Cleaned {pedCount} riders, all groups removed", true);
        }

        private void TrySpawnGroup()
        {
            if (_activeGroups.Count >= _maxGroups)
            {
                if (_debug) Notification.PostTicker($"~o~Riders: Max groups ({_activeGroups.Count}/{_maxGroups})", true);
                return;
            }

            Vector3 playerPos = Game.Player.Character.Position;
            string playerZone = GetZoneName(playerPos).ToUpperInvariant();
            int currentHour = Function.Call<int>(Hash.GET_CLOCK_HOURS);

            if (_debug)
                Notification.PostTicker($"~y~Riders: Spawn attempt | Zone: ~w~{playerZone}~y~ | Hour: {currentHour}h | Active: {_activeGroups.Count}/{_maxGroups}", true);

            // Filter groups by zone (always enforced) and per-group active hours
            // TestMode only bypasses hour restrictions for automatic spawning
            List<GroupConfig> candidates = new List<GroupConfig>();
            foreach (var gc in _groupConfigs)
            {
                // Zones are ALWAYS enforced (empty = everywhere)
                bool zoneOk = gc.Zones.Count == 0 || gc.Zones.Contains(playerZone);
                if (!zoneOk) continue;

                // Hours: bypassed in TestMode, enforced otherwise
                bool hourOk = _testMode || IsActiveHour(currentHour, gc.ActiveHourStart, gc.ActiveHourEnd);

                if (hourOk)
                    candidates.Add(gc);
                else if (_debug)
                    Notification.PostTicker($"~o~Riders: {gc.Name} outside hours ({gc.ActiveHourStart}h-{gc.ActiveHourEnd}h)", true);
            }

            if (candidates.Count == 0)
            {
                if (_debug) Notification.PostTicker($"~r~Riders: No groups for zone {playerZone} at {currentHour}h", true);
                return;
            }

            GroupConfig groupCfg = PickWeightedGroup(candidates);
            if (groupCfg == null) return;

            if (_debug)
                Notification.PostTicker($"~y~Riders: Picked ~w~{groupCfg.Name}~y~ ({candidates.Count} candidates)", true);

            Vector3 spawnPos;
            float spawnHeading;
            if (!FindRoadNode(playerPos, _spawnDistMin, _spawnDistMax, out spawnPos, out spawnHeading))
            {
                if (_debug) Notification.PostTicker("~r~Riders: No road node found", true);
                return;
            }

            Vector3 destination;
            float destHeading;
            if (!FindRoadNode(spawnPos, _destDistMin, _destDistMax, out destination, out destHeading))
                destination = spawnPos + GetHeadingDir(spawnHeading) * 500f;

            int count = _rnd.Next(_ridersMin, _ridersMax + 1);

            // --- Load bike model(s) ---
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
                bool allLoaded = true;
                foreach (var m in bikeModels) if (!m.IsLoaded) { allLoaded = false; break; }
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
                if (_debug) Notification.PostTicker("~r~Riders: All bike models FAILED to load", true);
                return;
            }

            string displayBikes = mixed ? string.Join("/", bikeNames) : bikeNames[0];
            if (_debug)
                Notification.PostTicker($"~y~Riders: Loading {count}x ~w~{displayBikes}~y~...", true);

            // Load ped models in parallel
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
                bool allLoaded = true;
                foreach (var pm in pedEntries)
                    if (!pm.IsLoaded) { allLoaded = false; break; }
                if (allLoaded) break;
                Wait(0);
            }

            // Create group
            var group = new ActiveGroup
            {
                Id = _nextGroupId++,
                Destination = destination,
                ConfigName = groupCfg.Name
            };

            Vector3 forwardDir = GetHeadingDir(spawnHeading);
            Vehicle leaderBike = null;

            for (int i = 0; i < pedEntries.Count; i++)
            {
                Model pedModel = pedEntries[i];
                if (!pedModel.IsLoaded)
                {
                    if (_debug) Notification.PostTicker($"~r~Riders: Ped #{i} failed to load", true);
                    pedModel.MarkAsNoLongerNeeded();
                    continue;
                }

                // Pick bike model: round-robin if mixed, otherwise single model
                int bikeIdx = mixed ? (i % bikeModels.Count) : 0;
                string bikeModelName = bikeNames[bikeIdx];
                Model bikeModel = bikeModels[bikeIdx];

                // Stagger position along road (uses configurable spacing)
                Vector3 pos = spawnPos + forwardDir * (-_spawnSpacing * i);

                Vehicle bike = World.CreateVehicle(bikeModel, pos, spawnHeading);
                if (bike == null)
                {
                    if (_debug) Notification.PostTicker($"~r~Riders: CreateVehicle #{i} failed", true);
                    pedModel.MarkAsNoLongerNeeded();
                    continue;
                }

                // Apply color if configured
                if (groupCfg.BikeColors.Count > 0)
                {
                    int colorId = groupCfg.BikeColors[_rnd.Next(groupCfg.BikeColors.Count)];
                    Function.Call(Hash.SET_VEHICLE_COLOURS, bike.Handle, colorId, colorId);
                }

                Ped ped = World.CreatePed(pedModel, pos);
                if (ped == null)
                {
                    if (_debug) Notification.PostTicker($"~r~Riders: CreatePed #{i} failed", true);
                    bike.Delete();
                    pedModel.MarkAsNoLongerNeeded();
                    continue;
                }

                ped.SetIntoVehicle(bike, VehicleSeat.Driver);
                WheelieConfig cfg = GetWheelieConfig(bikeModelName);

                // Preserve original relationship group, set as mission entity, then restore
                uint relGroup = Function.Call<uint>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle);
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, bike.Handle, true, true);
                Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, relGroup);
                // Ensure same-group riders are companions (prevents friendly fire)
                Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 0, relGroup, relGroup);

                ApplyPedFlags(ped, cfg);
                Function.Call(Hash.SET_ENTITY_MAX_SPEED, bike.Handle, cfg.MaxSpeed);

                // Give weapon from group config
                if (groupCfg.Weapons.Count > 0)
                {
                    string weapon = groupCfg.Weapons[_rnd.Next(groupCfg.Weapons.Count)];
                    uint weaponHash = Function.Call<uint>(Hash.GET_HASH_KEY, weapon);
                    Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle, weaponHash, 200, false, true);
                }

                // Set drive ability (0.0-1.0)  
                Function.Call(Hash.SET_DRIVER_ABILITY, ped.Handle, cfg.DriveAbility);
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, ped.Handle, cfg.DriveAggressiveness);

                if (i == 0)
                {
                    leaderBike = bike;
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                        ped.Handle, bike.Handle,
                        destination.X, destination.Y, destination.Z,
                        cfg.DriveSpeed, cfg.DrivingFlags, 10f);
                }
                else if (leaderBike != null)
                {
                    Function.Call((Hash)0xFC545A9F0626E3B6, // TASK_VEHICLE_FOLLOW
                        ped.Handle, bike.Handle, leaderBike.Handle,
                        cfg.DriveSpeed, cfg.DrivingFlags, _followMinDistance);
                }

                _riders[ped.Handle] = new RiderState
                {
                    BikeModel = bikeModelName.ToUpperInvariant(),
                    GroupId = group.Id,
                    IsSpawned = true,
                    DoWheelie = groupCfg.DoWheelie,
                    LastBikeHandle = bike.Handle,
                    PedHandle = ped.Handle,
                    OriginalRelGroup = relGroup
                };

                group.PedHandles.Add(ped.Handle);
                group.BikeHandles.Add(bike.Handle);
                if (i == 0) group.LeaderBikeHandle = bike.Handle;
                pedModel.MarkAsNoLongerNeeded();
            }

            foreach (var m in bikeModels) m.MarkAsNoLongerNeeded();

            if (group.PedHandles.Count > 0)
            {
                _activeGroups.Add(group);
                if (_debug)
                    Notification.PostTicker($"~g~Riders: {group.PedHandles.Count}x {groupCfg.Name} spawned [{playerZone}]", true);
            }
        }

        /// <summary>
        /// Apply all ped behavior flags from config in one place
        /// </summary>
        private void ApplyPedFlags(Ped ped, WheelieConfig cfg)
        {
            // Block non-temporary events (prevents flee/combat reactions)
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, cfg.BlockNonTempEvents);
            
            // Flee attributes (0 = never flee)
            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, cfg.FleeAttributes, false);
            
            // Combat attributes (17 = can't fight back when attacked)
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, cfg.CombatAttribute, cfg.CombatAttributeValue);
                        
            if (cfg.SuffersCriticalHits >= 0)
                Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, ped.Handle, cfg.SuffersCriticalHits > 0);
        }

        private GroupConfig PickWeightedGroup(List<GroupConfig> candidates)
        {
            if (candidates.Count == 0) return null;
            int total = 0;
            foreach (var g in candidates) total += g.Weight;
            if (total <= 0) return candidates[0];

            int roll = _rnd.Next(total);
            int cumul = 0;
            foreach (var g in candidates)
            {
                cumul += g.Weight;
                if (roll < cumul) return g;
            }
            return candidates[candidates.Count - 1];
        }

        private bool FindRoadNode(Vector3 origin, float minDist, float maxDist, out Vector3 position, out float heading)
        {
            position = Vector3.Zero;
            heading = 0f;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                float angle = (float)(_rnd.NextDouble() * Math.PI * 2);
                float dist = minDist + (float)(_rnd.NextDouble() * (maxDist - minDist));

                Vector3 candidate = origin + new Vector3(
                    (float)Math.Cos(angle) * dist,
                    (float)Math.Sin(angle) * dist, 0);

                OutputArgument outPos = new OutputArgument();
                OutputArgument outHead = new OutputArgument();

                bool found = Function.Call<bool>(Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING,
                    candidate.X, candidate.Y, candidate.Z,
                    outPos, outHead, 1, 3.0f, 0);

                if (found)
                {
                    position = outPos.GetResult<Vector3>();
                    heading = outHead.GetResult<float>();
                    return true;
                }
            }
            return false;
        }

        // =========================================================================
        //  Natural Rider Scanning
        // =========================================================================

        private void ScanNaturalRiders()
        {
            Ped[] peds = World.GetNearbyPeds(Game.Player.Character.Position, _naturalScanRadius);
            foreach (Ped p in peds)
            {
                if (p.Handle == Game.Player.Character.Handle) continue;
                if (!p.Exists() || !p.IsAlive || !p.IsInVehicle()) continue;
                if (_riders.ContainsKey(p.Handle)) continue;

                Vehicle v = p.CurrentVehicle;
                if (v == null || !v.Model.IsBike || p.SeatIndex != VehicleSeat.Driver) continue;

                string modelName = Function.Call<string>(
                    Hash.GET_DISPLAY_NAME_FROM_VEHICLE_MODEL, v.Model.Hash) ?? "UNKNOWN";

                _riders[p.Handle] = new RiderState
                {
                    BikeModel = modelName.ToUpperInvariant(),
                    GroupId = -1,
                    IsSpawned = false,
                    DoWheelie = !_wheelieExcludedModels.Contains(modelName),
                    LastBikeHandle = v.Handle,
                    PedHandle = p.Handle
                };
            }
        }
    }
}

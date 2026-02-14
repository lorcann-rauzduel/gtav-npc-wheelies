using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;
using GTA.Math;
using GTA.UI;

namespace StreetRiders
{
    /// <summary>
    /// Group spawning, per-rider processing loop, remount, cleanup, destinations.
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
                int escortMode = (i % 3 == 0) ? -1 : (i % 3 == 1) ? 2 : 3;
                Function.Call(Hash.TASK_VEHICLE_ESCORT,
                    ped.Handle, bike.Handle, leaderBike.Handle,
                    escortMode, cfg.DriveSpeed, cfg.DrivingFlags, 8.0f, 0, 20.0f);
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
                int escortMode = (i % 3 == 0) ? -1 : (i % 3 == 1) ? 2 : 3;
                Function.Call(Hash.TASK_VEHICLE_ESCORT,
                    ped.Handle, bike.Handle, leaderBike.Handle,
                    escortMode, cfg.DriveSpeed, cfg.DrivingFlags, 8.0f, 0, 20.0f);
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

    // =========================================================================
    //  Per-Rider Processing Loop
    // =========================================================================

    private void ProcessAllRiders()
    {
        int now = Game.GameTime;
        List<int> handles = new List<int>(_riders.Keys);

        foreach (int handle in handles)
        {
            Ped ped = (Ped)Entity.FromHandle(handle);
            if (ped == null || !ped.Exists() || !ped.IsAlive)
            {
                // Rider dead/gone: release any cops that were pursuing them
                if (ped != null && ped.Exists())
                    ReleasePursuingCops(ped.Position);
                _riders.Remove(handle);
                continue;
            }

            if (ped.Handle == Game.Player.Character.Handle) continue;

            RiderState state = _riders[handle];
            WheelieConfig cfg = GetWheelieConfig(state.BikeModel);

            // Skip riders in idle scenarios (waiting for player)
            if (state.InIdleScenario) continue;

            if (state.RemountActive && !ped.IsInVehicle())
                HandleRemount(ped, state, cfg);

            bool inVehicle = ped.IsInVehicle();

            if (state.WasInVehicle && !inVehicle) OnFellOff(ped, state, cfg);
            if (!state.WasInVehicle && inVehicle) OnRemounted(ped, state, cfg);
            state.WasInVehicle = inVehicle;

            if (!inVehicle || ped.IsRagdoll) continue;

            Vehicle bike = ped.CurrentVehicle;
            if (bike == null || !bike.Model.IsBike) continue;
            state.LastBikeHandle = bike.Handle;

            // Bike destroyed or submerged: eject ped, make them wander, release naturally
            if (bike.IsDead || bike.EngineHealth <= 0 || bike.IsInWater)
            {
                ReleasePursuingCops(ped.Position);
                ped.Task.LeaveVehicle();
                Script.Wait(0);
                ped.Task.Wander();
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
                ped.MarkAsNoLongerNeeded();
                bike.MarkAsNoLongerNeeded();
                _riders.Remove(handle);
                if (_debug)
                    Notification.PostTicker($"~o~Riders: ped released (bike {(bike.IsInWater ? "in water" : "destroyed")})", true);
                continue;
            }

            // Skip wheelie if disabled for this group
            if (!state.DoWheelie)
            {
                if (_debug)
                    DrawDebugText(ped, $"[{(state.IsSpawned?"S":"N")}:{state.BikeModel}] Cruising", Color.SkyBlue);
                continue;
            }

            // Skip wheelie physics if player is too far (perf optimization)
            float distToPlayer = ped.Position.DistanceTo(Game.Player.Character.Position);
            if (distToPlayer > _wheelieActiveDist)
            {
                // Reset state so they don't start mid-wheelie when player approaches
                if (state.Phase != Phase.Idle)
                    state.Phase = Phase.Idle;
                if (_debug)
                    DrawDebugText(ped, $"[{(state.IsSpawned?"S":"N")}:{state.BikeModel}] Far ({distToPlayer:F0}m)", Color.Gray);
                continue;
            }

            // =========================================================================
            //  POLICE LOGIC (Check every ~1s spread via handle modulo)
            //  Cops lock onto a target and stay on it while alive + within radius
            // =========================================================================
            if (cfg.AllowPoliceArrest && (Game.GameTime + handle) % 1000 < 50)
            {
                Ped[] nearbyPeds = World.GetNearbyPeds(ped.Position, 15.0f);
                foreach (Ped cop in nearbyPeds)
                {
                    if (!Function.Call<bool>(Hash.DOES_ENTITY_EXIST, cop.Handle)) continue;
                    if (Function.Call<int>(Hash.GET_PED_TYPE, cop.Handle) != 6) continue;
                    if (!cop.IsAlive) continue;

                    int copH = cop.Handle;

                    // Check if this cop already has a locked pursuit target
                    CopPursuitInfo pursuit;
                    if (_copPursuits.TryGetValue(copH, out pursuit))
                    {
                        Ped currentTarget = (Ped)Entity.FromHandle(pursuit.TargetRiderHandle);
                        bool targetValid = currentTarget != null && currentTarget.Exists() && currentTarget.IsAlive;
                        bool withinRadius = targetValid && cop.Position.DistanceTo(currentTarget.Position) < _policePursuitRadius;
                        bool minTimeElapsed = (now - pursuit.AssignedTime) > _policePursuitMinDurationMs;

                        // Keep current target if still valid and within radius, OR min time not elapsed
                        if (targetValid && (withinRadius || !minTimeElapsed))
                            continue;

                        // Target lost or out of range after min time: clear and allow reassignment
                        _copPursuits.Remove(copH);
                    }

                    // Skip cops already in active combat (they have a target from the game)
                    if (cop.IsInCombat) continue;

                    // Assign this cop to chase this rider
                    _copPursuits[copH] = new CopPursuitInfo
                    {
                        TargetRiderHandle = ped.Handle,
                        AssignedTime = now
                    };

                    if (cop.IsInVehicle())
                    {
                        Vehicle copCar = cop.CurrentVehicle;
                        if (copCar != null && copCar.Driver == cop)
                        {
                            copCar.IsSirenActive = true;
                            Function.Call(Hash.TASK_VEHICLE_CHASE, cop.Handle, ped.Handle);

                            if (_debug)
                                Notification.PostTicker("POLICE: VEHICLE CHASE (locked)", true);
                        }
                    }
                    else
                    {
                        Function.Call(Hash.TASK_ARREST_PED, cop.Handle, ped.Handle);

                        if (_debug)
                            DrawDebugText(cop, "POLICE: ARRESTING RIDER", Color.Red);
                    }
                }
            }

            float speed = bike.Speed;
            float pitch = bike.Rotation.X;
            state.PedHandle = ped.Handle;
            
            // >>> All wheelie logic (raycast, pitch, force) is in RidersWheelie.cs <<<
            TickWheelie(bike, state, speed, pitch, cfg);

            state.LastPitch = pitch;
            state.LastSpeed = speed;

            if (_debug)
            {
                string tag = state.IsSpawned ? "S" : "N";
                float absPitch = Math.Abs(pitch);

                Color col;
                string detail;
                if (state.LastForceApplied > 0.001f)
                {
                    col = Color.Lime;
                    detail = $"LIFT:{state.LastForceApplied:F2}";
                }
                else if (state.LastForceApplied < -0.001f)
                {
                    col = Color.Red;
                    detail = $"DOWN:{state.LastForceApplied:F2}";
                }
                else if (!string.IsNullOrEmpty(_wheelieSkipReason))
                {
                    col = Color.Yellow;
                    detail = _wheelieSkipReason;
                }
                else
                {
                    col = Color.White;
                    detail = "";
                }

                DrawDebugText(ped,
                    $"[{tag}:{state.BikeModel}] {state.Phase} {state.AttemptsDone}/{state.AttemptsTarget} | P:{absPitch:F1} Spd:{speed:F0} {detail}",
                    col);
            }
        }
    }

    // =========================================================================
    //  Remount (fall recovery) - FIXED VERSION v2
    // =========================================================================

    /// <summary>
    /// HandleRemount - Uses IS_PED_STILL + cooldown to detect truly idle peds.
    /// 
    /// Previous version used GET_SCRIPT_TASK_STATUS with a TASK_ENTER_VEHICLE hash,
    /// which returned "no task" when TASK_GO_TO_ENTITY was active, causing an
    /// infinite clear-assign loop that kept the ped stuck forever.
    /// 
    /// This version:
    /// - Uses IS_PED_STILL to detect truly idle peds (not moving at all)
    /// - Waits for IS_PED_GETTING_UP to finish before assigning tasks
    /// - Respects cfg.RemountTaskCooldown between task assignments
    /// - Only clears tasks when ped is confirmed stuck (still + cooldown elapsed)
    /// - Never interrupts a ped that is actively walking toward the bike
    /// </summary>
    private void HandleRemount(Ped ped, RiderState state, WheelieConfig cfg)
    {
        // 1. Safety: Dead - abort remount
        if (!ped.IsAlive) 
        {
            state.RemountActive = false;
            return;
        }
        
        // Wait for ragdoll / getting-up animation to finish
        if (ped.IsRagdoll) return;
        if (Function.Call<bool>(Hash.IS_PED_GETTING_UP, ped.Handle)) return;

        Vehicle bike = (Vehicle)Entity.FromHandle(state.LastBikeHandle);

        // 2. Bike validity checks
        if (bike == null || !bike.Exists() || Game.GameTime > state.RemountTimeout
            || bike.IsDead || bike.EngineHealth <= 0 || bike.IsInWater)
        {
            state.RemountActive = false;
            ped.Task.Wander();
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
            return;
        }

        int now = Game.GameTime;
        float dist = ped.Position.DistanceTo(bike.Position);

        // 3. CRITICAL: If TASK_ENTER_VEHICLE (160) is active, let it play out.
        //    NEVER interrupt an ongoing enter-vehicle animation.
        bool enterTaskActive = Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, ped.Handle, 160);
        if (enterTaskActive) return;

        // 4. Also check TASK_GO_TO_ENTITY (178) — ped is walking to bike
        bool goToTaskActive = Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, ped.Handle, 178);
        if (goToTaskActive)
        {
            // Task is alive, but check if ped drifted too far (pathfinding fail)
            // Only intervene after cooldown + ped somehow got further from bike
            if (dist < cfg.RemountMaxDistance) return; // still walking, let it be
        }

        // 5. If we reach here, ped has NO active remount task — assign one
        bool isFirstTask = state.RemountLastTaskTime == 0;
        bool cooldownElapsed = (now - state.RemountLastTaskTime) >= cfg.RemountTaskCooldown;

        if (!isFirstTask && !cooldownElapsed) return;

        if (dist < cfg.RemountEnterDistance) 
        {
            // Close enough: Enter vehicle directly (no timeout — let ped take their time)
            Function.Call(Hash.TASK_ENTER_VEHICLE,
                ped.Handle, bike.Handle, -1, -1, cfg.RemountMoveSpeed, 1, 0);
            
            state.RemountLastTaskTime = now;
            
            if (_debug)
                Notification.PostTicker($"~y~Remount: ENTER_VEHICLE (dist:{dist:F1}m)", true);
        }
        else if (dist < cfg.RemountMaxDistance) 
        {
            // Too far: Go to bike first
            Function.Call(Hash.TASK_GO_TO_ENTITY, 
                ped.Handle, bike.Handle, -1, 
                cfg.RemountEnterDistance - 1.0f,
                cfg.RemountMoveSpeed, 1073741824.0f, 0);
            
            state.RemountLastTaskTime = now;
            
            if (_debug)
                Notification.PostTicker($"~y~Remount: GO_TO_ENTITY (dist:{dist:F1}m)", true);
        }
        else
        {
            // Give up: Bike too far away
            state.RemountActive = false;
            ped.Task.Wander();
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, false);
            
            if (_debug)
                Notification.PostTicker($"~r~Remount: ABANDONED (dist:{dist:F1}m)", true);
        }
    }

    private void OnFellOff(Ped ped, RiderState state, WheelieConfig cfg)
    {
        state.RemountActive = true;
        state.RemountTimeout = Game.GameTime + cfg.RemountTimeout;
        state.RemountLastTaskTime = 0;
        state.Phase = Phase.Idle;
        state.AttemptsDone = 0;
        state.AttemptsTarget = 0;
        state.NextAttemptDelay = Game.GameTime + 2000;

        // Engage handbrake so the bike doesn't roll away from the fallen rider
        Vehicle bike = (Vehicle)Entity.FromHandle(state.LastBikeHandle);
        if (bike != null && bike.Exists())
            Function.Call(Hash.SET_VEHICLE_HANDBRAKE, bike.Handle, true);

        Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
        ApplyPedFlags(ped, cfg);
    }

    private void OnRemounted(Ped ped, RiderState state, WheelieConfig cfg)
    {
        state.Phase = Phase.Idle;
        state.AttemptsDone = 0;
        state.AttemptsTarget = 0;
        state.RemountActive = false;
        state.RemountLastTaskTime = 0;
        ApplyPedFlags(ped, cfg);
        state.NextAttemptDelay = Game.GameTime + _rnd.Next(1500, 3000);

        // Release the handbrake that was set when the rider fell off
        Vehicle bike = ped.CurrentVehicle;
        if (bike != null && bike.Exists())
            Function.Call(Hash.SET_VEHICLE_HANDBRAKE, bike.Handle, false);

        if (state.IsSpawned) RetaskRiderToGroup(ped, state);
    }

    private void RetaskRiderToGroup(Ped ped, RiderState state)
    {
        Vehicle bike = ped.CurrentVehicle;
        if (bike == null) return;

        WheelieConfig cfg = GetWheelieConfig(state.BikeModel);
        
        // Find the group
        ActiveGroup targetGroup = null;
        foreach (var group in _activeGroups)
        {
            if (group.Id == state.GroupId)
            {
                targetGroup = group;
                break;
            }
        }

        if (targetGroup == null) return;

        // If the group is following the player, escort player's vehicle instead
        if (targetGroup.FollowMode == GroupFollowMode.FollowingPlayer)
        {
            Ped player = Game.Player.Character;
            if (player.IsInVehicle())
            {
                Vehicle playerVeh = player.CurrentVehicle;
                if (playerVeh != null && playerVeh.Exists())
                {
                    if (bike.Handle == targetGroup.LeaderBikeHandle)
                    {
                        Function.Call(Hash.TASK_VEHICLE_ESCORT,
                            ped.Handle, bike.Handle, playerVeh.Handle,
                            -1, cfg.DriveSpeed, cfg.DrivingFlags, 8.0f, 0, 500.0f);
                    }
                    else
                    {
                        Vehicle lb = (Vehicle)Entity.FromHandle(targetGroup.LeaderBikeHandle);
                        if (lb != null && lb.Exists())
                        {
                            Function.Call(Hash.TASK_VEHICLE_ESCORT,
                                ped.Handle, bike.Handle, lb.Handle,
                                -1, cfg.DriveSpeed, cfg.DrivingFlags, 8.0f, 0, 500.0f);
                        }
                        else
                        {
                            Function.Call(Hash.TASK_VEHICLE_ESCORT,
                                ped.Handle, bike.Handle, playerVeh.Handle,
                                -1, cfg.DriveSpeed, cfg.DrivingFlags, 8.0f, 0, 500.0f);
                        }
                    }
                    Function.Call(Hash.SET_ENTITY_MAX_SPEED, bike.Handle, cfg.MaxSpeed);
                    return;
                }
            }
        }

        // Autonomous mode: follow group destination / leader
        if (bike.Handle == targetGroup.LeaderBikeHandle)
        {
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                ped.Handle, bike.Handle,
                targetGroup.Destination.X, targetGroup.Destination.Y, targetGroup.Destination.Z,
                cfg.DriveSpeed, cfg.DrivingFlags, 10f);
        }
        else
        {
            Vehicle leaderBike = (Vehicle)Entity.FromHandle(targetGroup.LeaderBikeHandle);
            
            if (leaderBike != null && leaderBike.Exists())
            {
                Function.Call(Hash.TASK_VEHICLE_ESCORT,
                    ped.Handle, bike.Handle, leaderBike.Handle,
                    -1, cfg.DriveSpeed, cfg.DrivingFlags, 10.0f, 0, 500.0f);
            }
            else
            {
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                    ped.Handle, bike.Handle,
                    targetGroup.Destination.X, targetGroup.Destination.Y, targetGroup.Destination.Z,
                    cfg.DriveSpeed, cfg.DrivingFlags, 10f);
            }
        }
        
        Function.Call(Hash.SET_ENTITY_MAX_SPEED, bike.Handle, cfg.MaxSpeed);
    }

    // =========================================================================
    //  Follow Player System
    // =========================================================================

    /// <summary>
    /// Checks if the player is in a valid vehicle type for following.
    /// </summary>
    private bool IsPlayerInValidVehicle(out Vehicle playerVeh)
    {
        playerVeh = null;
        Ped player = Game.Player.Character;
        if (!player.IsInVehicle()) return false;

        playerVeh = player.CurrentVehicle;
        if (playerVeh == null) return false;

        if (_playerVehicleType.Equals("BikeOnly", StringComparison.OrdinalIgnoreCase))
            return playerVeh.Model.IsBike;
        if (_playerVehicleType.Equals("TwoWheeler", StringComparison.OrdinalIgnoreCase))
            return playerVeh.Model.IsBike || playerVeh.Model.IsBicycle;

        return true; // "Any"
    }

    /// <summary>
    /// Main follow player processing. Runs state machine for each active group.
    /// Handles transitions: Autonomous <-> FollowingPlayer <-> WaitingForPlayer
    /// </summary>
    private void ProcessFollowPlayer()
    {
        if (!_followEnabled || _activeGroups.Count == 0) return;

        Ped player = Game.Player.Character;
        Vector3 playerPos = player.Position;
        bool playerInVehicle = player.IsInVehicle();
        Vehicle playerVeh = null;
        bool playerInValidVehicle = IsPlayerInValidVehicle(out playerVeh);
        int now = Game.GameTime;

        foreach (var group in _activeGroups)
        {
            if (group.PedHandles.Count == 0) continue;

            // Only groups with CanFollowPlayer = true can follow
            GroupConfig grpCfg = FindGroupConfig(group.ConfigName);
            if (grpCfg == null || !grpCfg.CanFollowPlayer) continue;

            // =================================================================
            //  BODYGUARD — protect player when attacked
            //  Uses LockPedForScript pattern to preserve rider identity & prevent
            //  friendly fire between riders and against the player.
            // =================================================================
            if (_bodyguardEnabled && (group.FollowMode == GroupFollowMode.FollowingPlayer
                                  || group.FollowMode == GroupFollowMode.WaitingForPlayer))
            {
                // Active bodyguard engagement
                if (group.BodyguardTarget != 0)
                {
                    // Periodically re-lock riders to prevent relationship drift during combat
                    // (every 3 seconds while bodyguard is active)
                    if ((now - group.BodyguardStartTime) % 3000 < 200)
                        LockGroupRiders(group);

                    // PRIORITY: Player remounted → cancel fight immediately, force riders to follow
                    if (playerInVehicle && playerInValidVehicle)
                    {
                        group.BodyguardTarget = 0;
                        CancelBodyguardAndFollow(group, playerVeh);
                        group.FollowMode = GroupFollowMode.FollowingPlayer;
                        group.PlayerWasInVehicle = true;
                        group.RidersInScenario = false;

                        if (_debug)
                            Notification.PostTicker($"~g~Bodyguard: {group.ConfigName} player remounted, falling in!", true);
                        continue;
                    }

                    // Check if threat is still valid
                    Ped threat = (Ped)Entity.FromHandle(group.BodyguardTarget);
                    bool threatGone = threat == null || !threat.Exists() || !threat.IsAlive
                                     || threat.Position.DistanceTo(playerPos) > _bodyguardRadius * 2f
                                     || (now - group.BodyguardStartTime) > _bodyguardDurationMs;

                    if (threatGone)
                    {
                        group.BodyguardTarget = 0;

                        // Re-lock riders to clean state after combat ends
                        LockGroupRiders(group);

                        // Resume normal behavior based on current mode
                        if (group.FollowMode == GroupFollowMode.FollowingPlayer && playerInValidVehicle)
                            CancelBodyguardAndFollow(group, playerVeh);
                        else if (group.FollowMode == GroupFollowMode.WaitingForPlayer
                                 && _dismountBehavior == DismountBehavior.Freestyle)
                            group.FreestyleNextSwitch = 0; // force freestyle retask next tick

                        if (_debug)
                            Notification.PostTicker($"~g~Bodyguard: {group.ConfigName} threat neutralized, resuming", true);
                    }
                    continue; // skip normal follow logic while bodyguard is active
                }

                // Detect new attacker — check if player was damaged by any ped
                if (Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ANY_PED, Game.Player.Character.Handle))
                {
                    Ped attacker = FindPlayerAttacker(playerPos);
                    if (attacker != null)
                    {
                        group.BodyguardTarget = attacker.Handle;
                        group.BodyguardStartTime = now;
                        TaskGroupCombat(group, attacker);

                        if (_debug)
                            Notification.PostTicker($"~r~Bodyguard: {group.ConfigName} engaging threat!", true);
                    }
                    Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, Game.Player.Character.Handle);
                }
            }

            // Find group leader
            Vehicle leaderBike = (Vehicle)Entity.FromHandle(group.LeaderBikeHandle);
            Ped leaderPed = null;
            if (leaderBike != null && leaderBike.Exists())
                leaderPed = leaderBike.Driver;

            // =====================================================================
            //  AUTONOMOUS -> check for follow trigger
            // =====================================================================
            if (group.FollowMode == GroupFollowMode.Autonomous)
            {
                if (!playerInValidVehicle) continue;

                // Decision cooldown — don't re-evaluate trigger too soon after leaving follow
                if (group.LastDecisionTime != 0 && now - group.LastDecisionTime < _dynamicTriggerMs)
                    continue;

                // Check ALL riders in the group, not just the leader
                // This allows re-trigger when the player crosses any rider
                float closestDist = float.MaxValue;
                for (int r = 0; r < group.PedHandles.Count; r++)
                {
                    Ped rp = (Ped)Entity.FromHandle(group.PedHandles[r]);
                    if (rp != null && rp.Exists() && rp.IsAlive)
                    {
                        float d = rp.Position.DistanceTo(playerPos);
                        if (d < closestDist) closestDist = d;
                    }
                }

                bool shouldFollow = false;

                if (_followTrigger == FollowTrigger.Proximity)
                {
                    shouldFollow = closestDist < _followRadius;
                }
                else // Dynamic — speed-based: player must stay fast + close for a sustained period
                {
                    bool playerFast = playerVeh.Speed > _dynamicSpeedThreshold;
                    bool conditionMet = playerFast && closestDist < _followRadius;

                    if (conditionMet)
                    {
                        // Start tracking when condition is first met
                        if (group.DynamicTriggerStart == 0)
                            group.DynamicTriggerStart = now;

                        // Only trigger after sustained for DynamicTriggerMs
                        if (now - group.DynamicTriggerStart >= _dynamicTriggerMs)
                            shouldFollow = true;
                    }
                    else
                    {
                        // Condition broken — reset timer
                        group.DynamicTriggerStart = 0;
                    }
                }

                if (shouldFollow)
                {
                    group.DynamicTriggerStart = 0; // Reset for next time
                    group.LastDecisionTime = now;   // Start cooldown
                    group.FollowMode = GroupFollowMode.FollowingPlayer;
                    group.PlayerWasInVehicle = true;
                    RetaskGroupFollowPlayer(group, playerVeh);

                    if (_debug)
                        Notification.PostTicker($"~g~Follow: {group.ConfigName} now following player!", true);
                }
            }
            // =====================================================================
            //  FOLLOWING PLAYER
            // =====================================================================
            else if (group.FollowMode == GroupFollowMode.FollowingPlayer)
            {
                // Player dismounted?
                if (!playerInVehicle)
                {
                    if (group.PlayerWasInVehicle)
                    {
                        // Edge: player just got off
                        if (_dismountBehavior == DismountBehavior.Continue)
                        {
                            // Pick random rider as new leader, go back to autonomous
                            group.FollowMode = GroupFollowMode.Autonomous;
                            PromoteNewLeader(group);
                            if (_debug)
                                Notification.PostTicker($"~o~Follow: {group.ConfigName} leader takes over (player dismounted)", true);
                        }
                        else
                        {
                            // Wait or Freestyle mode
                            group.FollowMode = GroupFollowMode.WaitingForPlayer;
                            group.WaitStartTime = now;
                            group.RidersInScenario = false;

                            if (_dismountBehavior == DismountBehavior.Freestyle)
                            {
                                // Freestyle: riders stay on bikes, ride loops near player
                                Vector3 dest;
                                float destH;
                                if (FindRoadNode(playerPos, 30f, 80f, out dest, out destH))
                                    group.Destination = dest;
                                group.FreestyleNextSwitch = now + _rnd.Next(8000, 15000);
                                RetaskFreestyleLoop(group);
                                if (_debug)
                                    Notification.PostTicker($"~b~Follow: {group.ConfigName} freestyle near player!", true);
                            }
                            else
                            {
                                // Wait: dismount and idle near player
                                for (int r = 0; r < group.PedHandles.Count; r++)
                                {
                                    RiderState rst;
                                    if (_riders.TryGetValue(group.PedHandles[r], out rst))
                                    {
                                        rst.InIdleScenario = true;
                                        rst.RemountActive = false;
                                    }
                                }
                                StopGroupNearPlayer(group, playerPos);
                                if (_debug)
                                    Notification.PostTicker($"~y~Follow: {group.ConfigName} waiting for player", true);
                            }
                        }
                    }
                    group.PlayerWasInVehicle = false;
                    continue;
                }

                group.PlayerWasInVehicle = true;

                // Player in wrong vehicle type?
                if (!playerInValidVehicle)
                {
                    group.FollowMode = GroupFollowMode.Autonomous;
                    RetaskEntireGroup(group);
                    if (_debug)
                        Notification.PostTicker($"~o~Follow: {group.ConfigName} stopped (wrong vehicle)", true);
                    continue;
                }

                // Decision cooldown — don't re-evaluate lose conditions too soon after entering follow
                if (now - group.LastDecisionTime < _dynamicTriggerMs)
                    continue;

                // Check distance to lose follow — uses closest rider, not just leader
                // This prevents losing follow when the leader is far but other riders are close
                float closestFollowDist = float.MaxValue;
                for (int r = 0; r < group.PedHandles.Count; r++)
                {
                    Ped rp = (Ped)Entity.FromHandle(group.PedHandles[r]);
                    if (rp != null && rp.Exists() && rp.IsAlive)
                    {
                        float d = rp.Position.DistanceTo(playerPos);
                        if (d < closestFollowDist) closestFollowDist = d;
                    }
                }

                if (closestFollowDist > _followLoseRadius)
                {
                    group.LastDecisionTime = now; // Cooldown before re-trigger
                    group.FollowMode = GroupFollowMode.Autonomous;
                    RetaskEntireGroup(group);
                    if (_debug)
                        Notification.PostTicker($"~o~Follow: {group.ConfigName} lost player ({closestFollowDist:0}m > {_followLoseRadius}m)", true);
                    continue;
                }

                // Dynamic mode: player too slow = riders take back the lead
                if (_followTrigger == FollowTrigger.Dynamic
                    && playerVeh != null && playerVeh.Speed < _dynamicSpeedThreshold)
                {
                    group.LastDecisionTime = now; // Cooldown before re-trigger
                    group.FollowMode = GroupFollowMode.Autonomous;
                    RetaskEntireGroup(group);
                    if (_debug)
                        Notification.PostTicker($"~o~Follow: {group.ConfigName} riders take lead (player slow: {playerVeh.Speed:0.0} m/s < {_dynamicSpeedThreshold} m/s)", true);
                    continue;
                }
            }
            // =====================================================================
            //  WAITING FOR PLAYER — tick-based logic (Wait or Freestyle)
            // =====================================================================
            else if (group.FollowMode == GroupFollowMode.WaitingForPlayer)
            {
                // Player remounted → resume following (all behaviors)
                if (playerInVehicle && playerInValidVehicle)
                {
                    if (_dismountBehavior == DismountBehavior.Freestyle)
                    {
                        // Freestyle: riders already on bikes, just retask
                        group.FollowMode = GroupFollowMode.FollowingPlayer;
                        group.PlayerWasInVehicle = true;
                        RetaskGroupFollowPlayer(group, playerVeh);
                    }
                    else
                    {
                        // Wait: riders on foot, need to remount
                        ResumeGroupFromIdle(group);
                        group.FollowMode = GroupFollowMode.FollowingPlayer;
                        group.PlayerWasInVehicle = true;
                        group.RidersInScenario = false;
                        Script.Wait(_resumeDelayMs > 100 ? 100 : 0);
                        RetaskGroupFollowPlayer(group, playerVeh);
                    }

                    if (_debug)
                        Notification.PostTicker($"~g~Follow: {group.ConfigName} resuming follow!", true);
                    continue;
                }

                group.PlayerWasInVehicle = false;

                // =============================================================
                //  FREESTYLE — riders stay on bikes, ride loops near player
                // =============================================================
                if (_dismountBehavior == DismountBehavior.Freestyle)
                {
                    // Time to pick a new destination?
                    if (now > group.FreestyleNextSwitch)
                    {
                        Vector3 newDest;
                        float destH;
                        if (FindRoadNode(playerPos, 30f, 80f, out newDest, out destH))
                            group.Destination = newDest;
                        group.FreestyleNextSwitch = now + _rnd.Next(8000, 15000);
                        RetaskFreestyleLoop(group);
                    }

                    // If riders drifted too far from player, pull them back
                    for (int r = 0; r < group.PedHandles.Count; r++)
                    {
                        Ped ped = (Ped)Entity.FromHandle(group.PedHandles[r]);
                        if (ped == null || !ped.Exists() || !ped.IsAlive) continue;
                        float dist = ped.Position.DistanceTo(playerPos);
                        if (dist > _followLoseRadius * 0.8f)
                        {
                            // Too far — retask toward player area
                            group.FreestyleNextSwitch = 0; // force refresh next tick
                        }
                    }
                }
                // =============================================================
                //  WAIT — riders on foot, follow player + idle scenarios
                // =============================================================
                else
                {
                    int playerHandle = Game.Player.Character.Handle;

                    for (int r = 0; r < group.PedHandles.Count; r++)
                    {
                        Ped ped = (Ped)Entity.FromHandle(group.PedHandles[r]);
                        if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                        RiderState st;
                        _riders.TryGetValue(ped.Handle, out st);
                        float distToPlayer = ped.Position.DistanceTo(playerPos);

                        // --- Phase 1: Still on bike → drive to player, dismount ONLY when close ---
                        if (ped.IsInVehicle())
                        {
                            Vehicle bike = ped.CurrentVehicle;
                            if (bike != null)
                            {
                                if (distToPlayer <= _idleScenarioDist)
                                {
                                    // Close enough → dismount
                                    Function.Call(Hash.TASK_LEAVE_VEHICLE, ped.Handle, bike.Handle, 0);
                                    if (st != null) st.RemountLastTaskTime = 0; // reset for Phase 2
                                }
                                else
                                {
                                    // Far away → drive toward player first, retask every 5s
                                    int lastDrv = st != null ? st.RemountLastTaskTime : 0;
                                    if (lastDrv == 0 || (now - lastDrv) > 5000)
                                    {
                                        WheelieConfig cfg = GetWheelieConfig(st?.BikeModel ?? "");
                                        Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                                            ped.Handle, bike.Handle,
                                            playerPos.X, playerPos.Y, playerPos.Z,
                                            cfg.DriveSpeed * 0.5f, cfg.DrivingFlags, _idleScenarioDist);
                                        if (st != null) st.RemountLastTaskTime = now;
                                    }
                                }
                            }
                            continue;
                        }

                        // --- Phase 2: On foot ---
                        // EXACT pattern from StateFollowEntity.cs:
                        //   1. Assign TASK_GO_TO_ENTITY ONCE
                        //   2. Every 1s, check GET_IS_TASK_ACTIVE(ped, 178)
                        //   3. Only reassign if the task DIED — never spam
                        bool playingScenario = st != null && st.ScenarioPlaying;

                        if (playingScenario)
                        {
                            // Scenario active but player walked away → break and follow
                            if (distToPlayer > _idleFollowDist)
                            {
                                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, ped.Handle);
                                if (st != null)
                                {
                                    st.ScenarioPlaying = false;
                                    st.IdleArrivalTime = 0;
                                    st.RemountLastTaskTime = 0; // force fresh task assignment
                                }
                                // Assign TASK_GO_TO_ENTITY once (StateFollowEntity pattern)
                                Function.Call(Hash.TASK_GO_TO_ENTITY,
                                    ped.Handle, playerHandle, -1,
                                    _idleMinStayDist, 1.0f, 1073741824.0f, 0);
                                Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 218, true);
                                Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 184, true);
                                if (st != null) st.RemountLastTaskTime = now;
                                if (_debug)
                                    Notification.PostTicker($"~y~Follow: rider broke scenario, following player ({distToPlayer:0.0}m)", true);
                            }
                        }
                        else
                        {
                            if (distToPlayer > _idleScenarioDist)
                            {
                                // Too far → follow player using TASK_GO_TO_ENTITY
                                if (st != null) st.IdleArrivalTime = 0;

                                int lastTask = st != null ? st.RemountLastTaskTime : 0;
                                bool needsFirst = (lastTask == 0);
                                bool refreshTime = (now - lastTask) > 1000;

                                // Only retask if: first assignment OR (1s+ elapsed AND task 178 died)
                                if (needsFirst || (refreshTime && !Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, ped.Handle, 178)))
                                {
                                    float speed = distToPlayer > _idleFollowDist ? 2.0f : 1.0f;
                                    Function.Call(Hash.TASK_GO_TO_ENTITY,
                                        ped.Handle, playerHandle, -1,
                                        _idleMinStayDist, speed, 1073741824.0f, 0);
                                    Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 218, true);
                                    Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, 184, true);
                                    if (st != null) st.RemountLastTaskTime = now;
                                }
                            }
                            else
                            {
                                // Close enough — reset task timer so next follow gets a fresh assignment
                                if (st != null) st.RemountLastTaskTime = 0;

                                bool riderStill = Function.Call<bool>(Hash.IS_PED_STILL, ped.Handle);

                                if (!riderStill)
                                {
                                    // Rider is still moving → reset timer, don't start scenario
                                    if (st != null) st.IdleArrivalTime = 0;
                                }
                                else
                                {
                                    // Rider is truly idle → start/continue arrival timer
                                    if (st != null && st.IdleArrivalTime == 0)
                                        st.IdleArrivalTime = now;

                                    bool arrivedLongEnough = st != null && st.IdleArrivalTime > 0
                                                             && (now - st.IdleArrivalTime) > _idleScenarioWaitMs;

                                    if (arrivedLongEnough && _idleScenarios.Count > 0)
                                    {
                                        string scenario = _idleScenarios[_rnd.Next(_idleScenarios.Count)];
                                        Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, scenario, 0, true);
                                        if (st != null) st.ScenarioPlaying = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Retasks the entire group to escort the player's vehicle.
    /// Leader escorts player, followers escort leader.
    /// </summary>
    private void RetaskGroupFollowPlayer(ActiveGroup group, Vehicle playerVeh)
    {
        if (playerVeh == null || !playerVeh.Exists()) return;

        for (int i = 0; i < group.PedHandles.Count; i++)
        {
            Ped ped = (Ped)Entity.FromHandle(group.PedHandles[i]);
            if (ped == null || !ped.Exists() || !ped.IsAlive || !ped.IsInVehicle()) continue;

            Vehicle bike = ped.CurrentVehicle;
            RiderState st;
            _riders.TryGetValue(ped.Handle, out st);
            WheelieConfig cfg = GetWheelieConfig(st?.BikeModel ?? "");

            if (bike.Handle == group.LeaderBikeHandle)
            {
                // Leader escorts player's vehicle
                Function.Call(Hash.TASK_VEHICLE_ESCORT,
                    ped.Handle, bike.Handle, playerVeh.Handle,
                    -1, cfg.DriveSpeed, cfg.DrivingFlags, 8.0f, 0, 500.0f);
            }
            else
            {
                // Followers escort the leader
                Vehicle lb = (Vehicle)Entity.FromHandle(group.LeaderBikeHandle);
                if (lb != null && lb.Exists())
                {
                    int escortMode = (i % 3 == 0) ? -1 : (i % 3 == 1) ? 2 : 3;
                    Function.Call(Hash.TASK_VEHICLE_ESCORT,
                        ped.Handle, bike.Handle, lb.Handle,
                        escortMode, cfg.DriveSpeed, cfg.DrivingFlags, 8.0f, 0, 500.0f);
                }
            }
            Function.Call(Hash.SET_ENTITY_MAX_SPEED, bike.Handle, cfg.MaxSpeed);
        }
    }

    /// <summary>
    /// Finds the ped that attacked the player within bodyguard radius.
    /// Does not modify relationships — purely task-based detection.
    /// </summary>
    private Ped FindPlayerAttacker(Vector3 playerPos)
    {
        int playerHandle = Game.Player.Character.Handle;
        Ped[] nearby = World.GetNearbyPeds(Game.Player.Character, _bodyguardRadius);

        foreach (Ped p in nearby)
        {
            if (p == null || !p.Exists() || !p.IsAlive) continue;
            if (p.Handle == playerHandle) continue;
            if (_riders.ContainsKey(p.Handle)) continue; // skip our own riders

            if (Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, playerHandle, p.Handle, true))
                return p;
        }
        return null;
    }

    /// <summary>
    /// Applies LockPedForScript pattern to all riders in a group:
    /// Re-locks relationship identity and ensures same-group + player companion status.
    /// Call this before/during/after combat to prevent relationship corruption.
    /// </summary>
    private void LockGroupRiders(ActiveGroup group)
    {
        int playerRelGroup = Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH,
            Game.Player.Character.Handle);

        for (int i = 0; i < group.PedHandles.Count; i++)
        {
            Ped ped = (Ped)Entity.FromHandle(group.PedHandles[i]);
            if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

            RiderState st;
            _riders.TryGetValue(ped.Handle, out st);

            // 1. Get current identity
            int currentRelHash = Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle);

            // 2. Set mission entity (may reset rel group internally)
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped.Handle, true, true);

            // 3. Re-apply original group (prefer spawn-time original if available)
            int targetGroup = (st != null && st.OriginalRelGroup != 0)
                ? (int)st.OriginalRelGroup
                : currentRelHash;
            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, targetGroup);

            // 4. Same-group = Companion (prevents friendly fire between riders)
            Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 0, targetGroup, targetGroup);

            // 5. Rider <-> Player = Companion (prevents attacking the player)
            Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 0, targetGroup, playerRelGroup);
            Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 0, playerRelGroup, targetGroup);
        }
    }

    /// <summary>
    /// Tasks all riders in a group to combat a specific threat.
    /// Uses LockPedForScript pattern to preserve relationships and prevent
    /// riders from attacking each other or the player.
    /// </summary>
    private void TaskGroupCombat(ActiveGroup group, Ped threat)
    {
        // Lock all riders' relationships before combat engagement
        LockGroupRiders(group);

        for (int i = 0; i < group.PedHandles.Count; i++)
        {
            Ped ped = (Ped)Entity.FromHandle(group.PedHandles[i]);
            if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

            RiderState st;
            _riders.TryGetValue(ped.Handle, out st);

            // Clear current tasks (scenario, follow, etc.)
            //Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);

            if (st != null)
            {
                st.ScenarioPlaying = false;
                st.IdleArrivalTime = 0;
            }

            // Engage threat (flag 0 = default combat, 16 = can use vehicle)
            //Function.Call(Hash.TASK_COMBAT_PED, ped.Handle, threat.Handle, 0, 16);
        }
    }

    /// <summary>
    /// Cancels bodyguard combat and forces riders to remount and follow the player.
    /// Riders on bikes get retasked immediately; riders on foot sprint to their bike
    /// or run toward the player if no bike is available.
    /// </summary>
    private void CancelBodyguardAndFollow(ActiveGroup group, Vehicle playerVeh)
    {
        // Re-lock all riders' relationships to clean state after combat
        LockGroupRiders(group);

        for (int i = 0; i < group.PedHandles.Count; i++)
        {
            Ped ped = (Ped)Entity.FromHandle(group.PedHandles[i]);
            if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

            RiderState st;
            _riders.TryGetValue(ped.Handle, out st);

            // Block events FIRST to prevent combat re-engagement, then clear tasks
            // OnRemounted → ApplyPedFlags restores the configured value
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
            Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);

            if (st != null)
            {
                st.InIdleScenario = false;
                st.ScenarioPlaying = false;
                st.IdleArrivalTime = 0;
            }

            if (ped.IsInVehicle())
            {
                // Already on bike — will be retasked below
                continue;
            }

            // On foot — sprint back to bike or run to player
            if (st != null)
            {
                Vehicle bike = (Vehicle)Entity.FromHandle(st.LastBikeHandle);
                if (bike != null && bike.Exists())
                {
                    // Sprint to bike and enter (moveSpeed 2.0 = run)
                    Function.Call(Hash.TASK_ENTER_VEHICLE,
                        ped.Handle, bike.Handle, -1, -1, 2.0f, 1, 0);
                    st.RemountActive = true;
                    st.RemountTimeout = Game.GameTime + 15000;
                    st.RemountLastTaskTime = Game.GameTime;
                }
                else if (playerVeh != null && playerVeh.Exists())
                {
                    // No bike — run toward player's vehicle
                    Function.Call(Hash.TASK_GO_TO_ENTITY,
                        ped.Handle, playerVeh.Handle, -1, 5.0f, 2.0f, 0, 0);
                }
            }
        }

        // Retask riders already on bikes to escort the player
        RetaskGroupFollowPlayer(group, playerVeh);
    }

    /// <summary>
    /// Freestyle mode: tasks riders to drive to group.Destination in a loop near the player.
    /// Wheelie logic in ProcessAllRiders handles tricks automatically.
    /// </summary>
    private void RetaskFreestyleLoop(ActiveGroup group)
    {
        for (int i = 0; i < group.PedHandles.Count; i++)
        {
            Ped ped = (Ped)Entity.FromHandle(group.PedHandles[i]);
            if (ped == null || !ped.Exists() || !ped.IsAlive || !ped.IsInVehicle()) continue;

            Vehicle bike = ped.CurrentVehicle;
            RiderState st;
            _riders.TryGetValue(ped.Handle, out st);
            WheelieConfig cfg = GetWheelieConfig(st?.BikeModel ?? "");

            // Drive to the freestyle destination at moderate speed
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                ped.Handle, bike.Handle,
                group.Destination.X, group.Destination.Y, group.Destination.Z,
                cfg.DriveSpeed * 0.7f, cfg.DrivingFlags, 15f);
        }
    }

    /// <summary>
    /// Stops riders near the player's position when player dismounts (Wait mode).
    /// </summary>
    private void StopGroupNearPlayer(ActiveGroup group, Vector3 playerPos)
    {
        for (int i = 0; i < group.PedHandles.Count; i++)
        {
            Ped ped = (Ped)Entity.FromHandle(group.PedHandles[i]);
            if (ped == null || !ped.Exists() || !ped.IsAlive || !ped.IsInVehicle()) continue;

            Vehicle bike = ped.CurrentVehicle;
            RiderState st;
            _riders.TryGetValue(ped.Handle, out st);
            WheelieConfig cfg = GetWheelieConfig(st?.BikeModel ?? "");

            // Drive to player's position then stop
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                ped.Handle, bike.Handle,
                playerPos.X, playerPos.Y, playerPos.Z,
                20.0f, cfg.DrivingFlags, 15f);
        }
    }

    /// <summary>
    /// Clears idle scenarios and gets riders back on their bikes.
    /// </summary>
    private void ResumeGroupFromIdle(ActiveGroup group)
    {
        for (int i = 0; i < group.PedHandles.Count; i++)
        {
            Ped ped = (Ped)Entity.FromHandle(group.PedHandles[i]);
            if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

            RiderState st;
            _riders.TryGetValue(ped.Handle, out st);

            // Block non-temporary events FIRST to prevent combat re-engagement
            // after CLEAR_PED_TASKS. OnRemounted → ApplyPedFlags restores config value.
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped.Handle, true);
            Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);

            if (st != null)
            {
                st.InIdleScenario = false;
                st.ScenarioPlaying = false;
                st.IdleArrivalTime = 0;
            }

            // If not in vehicle, force sprint to bike and enter
            if (!ped.IsInVehicle() && st != null)
            {
                Vehicle bike = (Vehicle)Entity.FromHandle(st.LastBikeHandle);
                if (bike != null && bike.Exists())
                {
                    // Always use run speed (2.0) for urgency when player remounts
                    Function.Call(Hash.TASK_ENTER_VEHICLE,
                        ped.Handle, bike.Handle, -1, -1, 2.0f, 1, 0);
                    st.RemountActive = true;
                    st.RemountTimeout = Game.GameTime + 15000;
                    st.RemountLastTaskTime = Game.GameTime;
                }
            }
        }
    }

    /// <summary>
    /// Toggle follow for the nearest group (called from key handler).
    /// </summary>
    private void ToggleFollowPlayer()
    {
        // Check if any group is already following
        foreach (var group in _activeGroups)
        {
            if (group.FollowMode == GroupFollowMode.FollowingPlayer
                || group.FollowMode == GroupFollowMode.WaitingForPlayer)
            {
                // Stop following
                if (group.RidersInScenario)
                    ResumeGroupFromIdle(group);
                group.FollowMode = GroupFollowMode.Autonomous;
                group.RidersInScenario = false;
                RetaskEntireGroup(group);
                Notification.PostTicker($"~o~Follow: {group.ConfigName} stopped following", true);
                return;
            }
        }

        // No group following: find nearest group and start
        Vehicle playerVeh;
        bool validVeh = IsPlayerInValidVehicle(out playerVeh);

        if (!validVeh)
        {
            Notification.PostTicker("~r~Follow: Get in a vehicle first!", true);
            return;
        }

        ActiveGroup nearest = null;
        float bestDist = float.MaxValue;
        Vector3 playerPos = Game.Player.Character.Position;

        foreach (var group in _activeGroups)
        {
            if (group.PedHandles.Count == 0) continue;

            // Only consider groups with CanFollowPlayer
            GroupConfig grpCfg = FindGroupConfig(group.ConfigName);
            if (grpCfg == null || !grpCfg.CanFollowPlayer) continue;

            Vehicle lb = (Vehicle)Entity.FromHandle(group.LeaderBikeHandle);
            if (lb == null || !lb.Exists()) continue;

            float d = lb.Position.DistanceTo(playerPos);
            if (d < bestDist)
            {
                bestDist = d;
                nearest = group;
            }
        }

        if (nearest == null)
        {
            Notification.PostTicker("~r~Follow: No rider group nearby!", true);
            return;
        }

        nearest.FollowMode = GroupFollowMode.FollowingPlayer;
        nearest.PlayerWasInVehicle = true;
        RetaskGroupFollowPlayer(nearest, playerVeh);
        Notification.PostTicker($"~g~Follow: {nearest.ConfigName} now following you!", true);
    }

    // =========================================================================
    //  Cleanup
    // =========================================================================

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
                        ReleasePursuingCops(ped.Position);
                    remove = true;
                }
                else if (ped.Position.DistanceTo(playerPos) > _cleanupDist)
                {
                    ReleasePursuingCops(ped.Position);
                    remove = true;
                    DeletePedAndBike(ped);
                }
                else if (ped.IsInVehicle())
                {
                    Vehicle v = ped.CurrentVehicle;
                    if (v != null && (v.IsDead || v.EngineHealth <= 0 || v.IsInWater))
                    {
                        // Bike destroyed or in water: release naturally
                        ReleasePursuingCops(ped.Position);
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
                    _riders.Remove(pedHandle);
                    group.PedHandles.RemoveAt(i);
                    if (i < group.BikeHandles.Count)
                    {
                        Vehicle bike = (Vehicle)Entity.FromHandle(group.BikeHandles[i]);
                        if (bike != null && bike.Exists())
                        {
                            // Only hard-delete if far from player; otherwise let the game clean up naturally
                            if (bike.Position.DistanceTo(playerPos) > _cleanupDist)
                                bike.Delete();
                            else
                                bike.MarkAsNoLongerNeeded();
                        }
                        // Also release the dead/removed ped naturally if still exists
                        if (ped != null && ped.Exists())
                            ped.MarkAsNoLongerNeeded();
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
                    if (v != null && v.Exists()) v.MarkAsNoLongerNeeded();
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
                    if (_debug)
                        Notification.PostTicker($"~o~Riders: {group.ConfigName} leader lost, promoting...", true);
                    PromoteNewLeader(group);
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

            Function.Call(Hash.TASK_VEHICLE_ESCORT,
                f.Handle, fb.Handle, newBike.Handle,
                -1, fc.DriveSpeed, fc.DrivingFlags, 8.0f, 0, 20.0f);
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

            Vector3 newDest; float newHead;
            if (FindRoadNode(leader.Position, _destDistMin, _destDistMax, out newDest, out newHead))
            {
                group.Destination = newDest;
                RetaskEntireGroup(group);
                if (_debug) Notification.PostTicker($"~b~Riders: {group.ConfigName} new dest", true);
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
                    Function.Call(Hash.TASK_VEHICLE_ESCORT,
                        ped.Handle, bike.Handle, lb.Handle,
                        -1, cfg.DriveSpeed, cfg.DrivingFlags, 8.0f, 0, 20.0f);
            }
            Function.Call(Hash.SET_ENTITY_MAX_SPEED, bike.Handle, cfg.MaxSpeed);
        }
    }

    // =========================================================================
    //  Release cops pursuing a dead/removed rider
    // =========================================================================

    /// <summary>
    /// Finds nearby cops that were chasing a rider at the given position and
    /// resets them: clears their task, turns off sirens, and lets them return
    /// to normal ambient behavior.
    /// Called whenever a rider is removed from tracking (dead, cleaned up, etc.)
    /// </summary>
    private void ReleasePursuingCops(Vector3 riderPosition)
    {
        Ped[] nearby = World.GetNearbyPeds(riderPosition, 120.0f);
        foreach (Ped cop in nearby)
        {
            if (cop == null || !cop.Exists() || !cop.IsAlive) continue;
            if (Function.Call<int>(Hash.GET_PED_TYPE, cop.Handle) != 6) continue; // Not a cop

            // Remove from pursuit tracking
            _copPursuits.Remove(cop.Handle);

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
} // namespace StreetRiders
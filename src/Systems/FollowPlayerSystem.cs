using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;
using GTA.Math;
using GTA.UI;

namespace StreetRiders
{
    /// <summary>
    /// Follow Player state machine: Autonomous <-> FollowingPlayer <-> WaitingForPlayer.
    /// Handles trigger/lose conditions, dismount behaviors (Wait/Continue/Freestyle),
    /// idle scenarios, foot-following, toggle input, and group retasking.
    /// </summary>
    public partial class StreetRidersScript
    {
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

                        // PRIORITY: Player remounted -> cancel fight immediately, force riders to follow
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
                    // Player remounted -> resume following (all behaviors)
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

                        // Check each rider: retask if stuck idle or drifted too far
                        for (int r = 0; r < group.PedHandles.Count; r++)
                        {
                            Ped ped = (Ped)Entity.FromHandle(group.PedHandles[r]);
                            if (ped == null || !ped.Exists() || !ped.IsAlive) continue;

                            float dist = ped.Position.DistanceTo(playerPos);

                            if (dist > _followLoseRadius * 0.8f)
                            {
                                // Too far — force refresh next tick
                                group.FreestyleNextSwitch = 0;
                            }
                            else if (ped.IsInVehicle() && Function.Call<bool>(Hash.IS_PED_STILL, ped.Handle))
                            {
                                // Rider is on bike but not moving — task died, retask immediately
                                Vehicle bike = ped.CurrentVehicle;
                                if (bike != null)
                                {
                                    RiderState st;
                                    _riders.TryGetValue(ped.Handle, out st);
                                    WheelieConfig cfg = GetWheelieConfig(st?.BikeModel ?? "");

                                    Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
                                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                                        ped.Handle, bike.Handle,
                                        group.Destination.X, group.Destination.Y, group.Destination.Z,
                                        cfg.DriveSpeed * 0.7f, cfg.DrivingFlags, 15f);

                                    if (_debug)
                                        Notification.PostTicker($"~b~Freestyle: retasked idle rider", true);
                                }
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

                            // --- Phase 1: Still on bike -> drive to player, dismount ONLY when close ---
                            if (ped.IsInVehicle())
                            {
                                Vehicle bike = ped.CurrentVehicle;
                                if (bike != null)
                                {
                                    if (distToPlayer <= _idleScenarioDist)
                                    {
                                        // Close enough -> dismount
                                        Function.Call(Hash.TASK_LEAVE_VEHICLE, ped.Handle, bike.Handle, 0);
                                        if (st != null) st.RemountLastTaskTime = 0; // reset for Phase 2
                                    }
                                    else
                                    {
                                        // Far away -> drive toward player first, retask every 5s
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
                                // Scenario active but player walked away -> break and follow
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
                                    // Too far -> follow player using TASK_GO_TO_ENTITY
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
                                        // Rider is still moving -> reset timer, don't start scenario
                                        if (st != null) st.IdleArrivalTime = 0;
                                    }
                                    else
                                    {
                                        // Rider is truly idle -> start/continue arrival timer
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
        /// Freestyle mode: tasks riders to drive to group.Destination in a loop near the player.
        /// Clears previous tasks first to ensure clean transition from escort/follow.
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

                // Clear previous tasks (escort, follow, etc.) before assigning freestyle drive
                Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);

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
                // after CLEAR_PED_TASKS. OnRemounted -> ApplyPedFlags restores config value.
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
    }
}

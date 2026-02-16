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
                                group.FreestyleLastRefresh = 0; // force waypoint refresh next tick

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

                // --- DÉTECTION LEADER STUCK (Vitesse < 1.5 m/s) ---
                // SKIP cette détection si le groupe suit le joueur (le leader s'arrête naturellement)
                if (group.FollowMode != GroupFollowMode.FollowingPlayer 
                    && leaderPed != null && leaderPed.Exists() && leaderPed.IsAlive && leaderPed.IsInVehicle())
                {
                    Vehicle lv = leaderPed.CurrentVehicle;
                    
                    // IGNORE SI C'EST LE JOUEUR
                    if (leaderPed.Handle == Game.Player.Character.Handle)
                    {
                        group.LeaderStuckStartTime = 0;
                    }
                    else if (lv.Speed < 1.5f && group.FollowMode != GroupFollowMode.WaitingForPlayer)
                    {
                        if (group.LeaderStuckStartTime == 0) 
                            group.LeaderStuckStartTime = now;
                        
                        if (now - group.LeaderStuckStartTime > 8000)
                        {
                            PromoteNewLeader(group);
                            group.LeaderStuckStartTime = 0;
                            if (_debug) Notification.PostTicker("~o~Follow: Leader stuck (speed 0), promoting new one", true);
                            
                            RetaskEntireGroup(group);
                            continue;
                        }
                    }
                    else
                    {
                        group.LeaderStuckStartTime = 0;
                    }
                }

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
                                    // Freestyle: riders stay on bikes, cruise back and forth near player
                                    SetupFreestyleWaypoints(group, playerPos);
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

                    // ... (code existant: check playerInValidVehicle) ...

                    // =========================================================================
                    // LOGIQUE DE RATTRAPAGE / ANTI-ÉPARPILLEMENT
                    // Vérifie toutes les 1s si un motard est trop loin (> 60m) et qu'il ne suit pas le joueur
                    // =========================================================================
                    if (now % 1000 < 50) // Check toutes les secondes environ
                    {
                        Vehicle grpLeaderBike = (Vehicle)Entity.FromHandle(group.LeaderBikeHandle);
                        
                        if (grpLeaderBike != null && grpLeaderBike.Exists())
                        {
                            Vector3 leaderPos = grpLeaderBike.Position;
                            for (int r = 0; r < group.PedHandles.Count; r++)
                            {
                                Ped rp = (Ped)Entity.FromHandle(group.PedHandles[r]);
                                // On ne touche pas au leader, ni aux morts, ni aux piétons (ils sont gérés par HandleRemount)
                                if (rp == null || !rp.Exists() || !rp.IsAlive || !rp.IsInVehicle() || rp.CurrentVehicle.Handle == group.LeaderBikeHandle) 
                                    continue;

                                float distToLeader = rp.Position.DistanceTo(grpLeaderBike.Position);

                                // CAS 1 : Il est loin (> 60m) -> On force le rattrapage (CATCH UP)
                                if (distToLeader > 60.0f)
                                {
                                    // SKIP SI C'EST LE JOUEUR
                                    if (rp.Handle == Game.Player.Character.Handle)
                                        continue;
                                    
                                    RiderState st;
                                    _riders.TryGetValue(rp.Handle, out st);
                                    WheelieConfig cfg = GetWheelieConfig(st?.BikeModel ?? "");
                                    
                                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD, 
                                        rp.Handle, 
                                        rp.CurrentVehicle.Handle, 
                                        leaderPos.X, leaderPos.Y, leaderPos.Z, 
                                        cfg.DriveSpeed * 1.2f,
                                        1.0f, 
                                        rp.CurrentVehicle.Model.Hash, 
                                        1074528293,
                                        5.0f,
                                        true);
                                }
                                // CAS 2 : Il est revenu (< 50m) mais n'a pas la tâche Follow -> On remet en formation
                                else if (distToLeader < 50.0f)
                                {
                                    // Si sa tâche n'est pas "Follow Vehicle" (ID 169 ou similaire), on le remet dans le rang
                                    // Le moyen simple est de forcer le retask s'il était en mode rattrapage
                                    // Ici, on réapplique simplement le Follow standard s'il est proche
                                    RiderState st;
                                    _riders.TryGetValue(rp.Handle, out st);
                                    WheelieConfig cfg = GetWheelieConfig(st?.BikeModel ?? "");

                                    // On réassigne TASK_VEHICLE_FOLLOW pour qu'il se cale à la vitesse du groupe
                                    Function.Call((Hash)0xFC545A9F0626E3B6, 
                                        rp.Handle, rp.CurrentVehicle.Handle, grpLeaderBike.Handle,
                                        cfg.DriveSpeed, cfg.DrivingFlags, _followMinDistance);
                                }
                            }
                        }
                    }
                    // =========================================================================

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
                        // Refresh waypoints every 20s or if player moved far from the route
                        float playerToA = playerPos.DistanceTo(group.FreestylePointA);
                        float playerToB = playerPos.DistanceTo(group.FreestylePointB);
                        bool waypointsStale = (now - group.FreestyleLastRefresh) > 20000
                                              || (playerToA > 80f && playerToB > 80f);

                        if (waypointsStale)
                            SetupFreestyleWaypoints(group, playerPos);

                        // Per-rider: check if reached current waypoint → flip direction
                        bool pastGracePeriod = (now - group.FreestyleLastRetask) > 3000;

                        for (int r = 0; r < group.PedHandles.Count; r++)
                        {
                            Ped ped = (Ped)Entity.FromHandle(group.PedHandles[r]);
                            if (ped == null || !ped.Exists() || !ped.IsAlive || !ped.IsInVehicle()) continue;

                            RiderState st;
                            if (!_riders.TryGetValue(ped.Handle, out st)) continue;

                            Vector3 target = st.FreestyleTowardB ? group.FreestylePointB : group.FreestylePointA;
                            float distToTarget = ped.Position.DistanceTo(target);

                            // Reached waypoint → flip to the other one
                            if (distToTarget < 20f)
                            {
                                st.FreestyleTowardB = !st.FreestyleTowardB;
                                Vector3 newTarget = st.FreestyleTowardB ? group.FreestylePointB : group.FreestylePointA;
                                TaskRiderFreestyle(ped, newTarget, st);
                                group.FreestyleLastRetask = now;
                            }
                            // Stuck idle for 3s+ → retask toward current waypoint
                            else if (pastGracePeriod && Function.Call<bool>(Hash.IS_PED_STILL, ped.Handle))
                            {
                                TaskRiderFreestyle(ped, target, st);
                                group.FreestyleLastRetask = now;

                                if (_debug)
                                    Notification.PostTicker($"~b~Freestyle: retasked idle rider", true);
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
                                    Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
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
                                        float speed = distToPlayer > _idleFollowDist ? 1.5f : 1.0f;
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

                                   
                                        // Rider is truly idle -> start/continue arrival timer
                                        // Rider is truly idle -> start/continue arrival timer
                                        if (st != null && st.IdleArrivalTime == 0)
                                            st.IdleArrivalTime = now;

                                        // --- AJOUT MAJEUR : HEAD TRACKING & RE-ORIENTATION DYNAMIQUE ---

                                        // 1. On force le PNJ à regarder le joueur (la tête seulement) en permanence
                                        Function.Call(Hash.TASK_LOOK_AT_ENTITY, ped.Handle, playerHandle, -1, 0, 3);

                                        // ==================================================================================
                                        // AJOUT : SYSTÈME DE DIALOGUE AMBIANT
                                        // ==================================================================================
                                        // On vérifie si ça fait un moment qu'il n'a pas parlé (entre 8s et 15s)
                                        // "LastTalkTime" doit être ajouté dans ta classe RiderState, sinon utilise une variable locale temporaire
                                        if (st != null && (now - st.LastTalkTime) > _rnd.Next(8000, 15000))
                                        {
                                            // On ne veut pas qu'il parle à chaque frame, donc on lance un dé (30% de chance quand le timer est bon)
                                            if (_rnd.Next(100) < 100)
                                            {
                                                // Liste des phrases possibles pour varier
                                                string[] speeches = { "CHAT_STATE", "CHAT_RESP", "SEE_FRANKLIN", "GENERIC_CHEER", "HURRY_UP" };
                                                string chosenSpeech = speeches[_rnd.Next(speeches.Length)];

                                                // Le paramètre "SPEECH_PARAMS_FORCE_NORMAL" assure qu'il parle même s'il fait autre chose
                                                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, ped.Handle, chosenSpeech, "SPEECH_PARAMS_FORCE_NORMAL");
                                                                                            }

                                            // On reset le timer pour qu'il attende avant la prochaine phrase
                                            st.LastTalkTime = now;
                                        }
                                        // ==================================================================================

                                        // 2. Calcul de l'angle pour savoir si le joueur a "trop bougé"
                                        // On récupère la position relative du joueur par rapport au PNJ
                                        Vector3 pidPos = ped.Position;
                                        float dx = playerPos.X - pidPos.X;
                                        float dy = playerPos.Y - pidPos.Y;
                                        // Calcul de l'angle vers lequel le PNJ devrait regarder (en degrés)
                                        float headingToPlayer = (float)(System.Math.Atan2(dy, dx) * 180.0 / System.Math.PI) - 90.0f;
                                        float currentHeading = ped.Heading;
                                        // Différence absolue entre l'angle actuel et l'angle cible
                                        float headingDiff = System.Math.Abs((currentHeading - headingToPlayer + 540) % 360 - 180);

                                        // Si l'angle est trop grand (> 45 degrés), il doit se tourner physiquement
                                        bool needToTurn = headingDiff > 45.0f;

                                        if (needToTurn)
                                        {
                                            // Si on joue déjà un scénario, on l'arrête proprement pour se tourner
                                            if (st != null && st.ScenarioPlaying)
                                            {
                                                Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle); // Arrête le scénario
                                                st.ScenarioPlaying = false;
                                                st.IdleArrivalTime = now; // Reset du timer pour attendre un peu avant de reprendre
                                            }

                                            // On lance la rotation du corps
                                            // On ne le fait que si ce n'est pas déjà la tâche active (pour éviter le spam)
                                            if (!Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, ped.Handle, 225)) // 225 = CTaskComplexTurnToFaceEntityOrCoord
                                            {
                                                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, ped.Handle, playerHandle, 2000);
                                                if (st != null) st.IdleArrivalTime = now; // On repousse le scénario tant qu'il tourne
                                            }
                                        }
                                        else
                                        {
                                            // L'angle est bon (il te fait face), on peut dérouler la logique du scénario

                                            bool arrivedLongEnough = st != null && st.IdleArrivalTime > 0
                                                                    && (now - st.IdleArrivalTime) > _idleScenarioWaitMs;

                                            if (_idleScenarios.Count > 0)
                                            {
                                                // Si le timer est bon ET qu'il ne joue pas déjà
                                                if (arrivedLongEnough && st != null && !st.ScenarioPlaying)
                                                {
                                                    string scenario = _idleScenarios[_rnd.Next(_idleScenarios.Count)];
                                                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, scenario, 0, true);
                                                    st.ScenarioPlaying = true;
                                                }
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

                // TASK_VEHICLE_FOLLOW: proper pathfinding, follows target entity on road
                // Uses DrivingFlags from INI (cfg) for driving style
                if (bike.Handle == group.LeaderBikeHandle)
                {
                    // Leader follows player's vehicle
                    Function.Call((Hash)0xFC545A9F0626E3B6, // TASK_VEHICLE_FOLLOW
                        ped.Handle, bike.Handle, playerVeh.Handle,
                        cfg.DriveSpeed, cfg.DrivingFlags, _followMinDistance);
                }
                else
                {
                    // Followers follow the leader bike
                    Vehicle lb = (Vehicle)Entity.FromHandle(group.LeaderBikeHandle);
                    if (lb != null && lb.Exists())
                    {
                        Function.Call((Hash)0xFC545A9F0626E3B6, // TASK_VEHICLE_FOLLOW
                            ped.Handle, bike.Handle, lb.Handle,
                            cfg.DriveSpeed, cfg.DrivingFlags, _followMinDistance);
                    }
                }
                Function.Call(Hash.SET_ENTITY_MAX_SPEED, bike.Handle, cfg.MaxSpeed);
            }
        }

        /// <summary>
        /// Freestyle: finds two road waypoints near the player for back-and-forth cruising.
        /// Point A = closest road node (~20-40m), Point B = further node (~60-100m).
        /// </summary>
        private void SetupFreestyleWaypoints(ActiveGroup group, Vector3 playerPos)
        {
            Vector3 nodeA, nodeB;
            float headingA, headingB;

            // Point A: close road node (~10-25m from player)
            if (!FindRoadNode(playerPos, 25f, 50f, out nodeA, out headingA))
                nodeA = playerPos;

            // Point B: further road node (~40-65m from player)
            if (!FindRoadNode(playerPos, 25f, 50f, out nodeB, out headingB))
            {
                // Fallback: offset from point A
                nodeB = nodeA + new Vector3(50f, 0f, 0f);
            }
            
            group.FreestylePointA = nodeA;
            group.FreestylePointB = nodeB;
            group.FreestyleLastRefresh = Game.GameTime;

            if (_debug)
                Notification.PostTicker($"~b~Freestyle waypoints: A={nodeA.DistanceTo(playerPos):0}m  B={nodeB.DistanceTo(playerPos):0}m  span={nodeA.DistanceTo(nodeB):0}m", true);
        }

        /// <summary>
        /// Freestyle: initial task — sends all riders toward point A (half) or B (half) for variety.
        /// Clears previous tasks first to ensure clean transition from escort/follow.
        /// </summary>
        private void RetaskFreestyleLoop(ActiveGroup group)
        {
            for (int i = 0; i < group.PedHandles.Count; i++)
            {
                Ped ped = (Ped)Entity.FromHandle(group.PedHandles[i]);
                if (ped == null || !ped.Exists() || !ped.IsAlive || !ped.IsInVehicle()) continue;

                RiderState st;
                if (!_riders.TryGetValue(ped.Handle, out st)) continue;

                // Alternate riders between A and B for natural spread
                st.FreestyleTowardB = (i % 2 == 0);
                Vector3 target = st.FreestyleTowardB ? group.FreestylePointB : group.FreestylePointA;

                TaskRiderFreestyle(ped, target, st);
            }

            group.FreestyleLastRetask = Game.GameTime;
        }

        /// <summary>
        /// Tasks a single rider to drive to a freestyle waypoint at moderate speed.
        /// </summary>
        private void TaskRiderFreestyle(Ped ped, Vector3 target, RiderState st)
        {
            Vehicle bike = ped.CurrentVehicle;
            if (bike == null) return;

            WheelieConfig cfg = GetWheelieConfig(st?.BikeModel ?? "");

            Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                ped.Handle, bike.Handle,
                target.X, target.Y, target.Z,
                cfg.DriveSpeed * 0.7f, cfg.DrivingFlags, 15f);
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

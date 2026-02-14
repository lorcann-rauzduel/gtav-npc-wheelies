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
    /// Per-rider processing loop: vehicle state tracking, remount logic,
    /// fall recovery, police pursuit, wheelie tick dispatch, debug HUD.
    /// </summary>
    public partial class StreetRidersScript
    {
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
                // 1. On sauvegarde l'état AVANT le calcul physique
                Phase oldPhase = state.Phase;
                
                // >>> All wheelie logic (raycast, pitch, force) is in Physics/WheeliePhysics.cs <<<
                TickWheelie(bike, state, speed, pitch, cfg);

                // ==================================================================================
                // LOGIQUE CONTINUE PENDANT LE WHEELIE
                // On met à jour la cible régulièrement pour que le NPC ne s'arrête jamais
                // ==================================================================================
                if (state.Phase == Phase.Attempting || state.Phase == Phase.Sustained)
                {
                    // On throttle (limite) les appels pour ne pas spammer le moteur du jeu (toutes les ~200ms)
                    if ((Game.GameTime + ped.Handle) % 200 == 0)
                    {
                        // 1. Récupération du Leader
                        ActiveGroup myGroup = null;
                        foreach (var g in _activeGroups) 
                        { 
                            if (g.Id == state.GroupId) { myGroup = g; break; } 
                        }

                        Vehicle leaderBike = (Vehicle)Entity.FromHandle(myGroup?.LeaderBikeHandle ?? 0);
                        Vector3 targetPos;

                        // 2. Calcul de la cible : Toujours 60m DEVANT le leader (ou soi-même)
                        // On réduit à 60m pour mieux suivre les virages, comme on update souvent.
                        if (leaderBike != null && leaderBike.Exists() && bike.Handle != leaderBike.Handle)
                        {
                            targetPos = leaderBike.Position + (leaderBike.ForwardVector * 60.0f);
                        }
                        else
                        {
                            targetPos = bike.Position + (bike.ForwardVector * 60.0f);
                        }

                        // 3. Flags "Intelligents" : 4 (Swerve) + 16 (Avoid Peds) + 32 (Avoid Objects) = 52
                        int smartFlags = 52; 

                        // IMPORTANT : stopRange (p10) mis à 0.0f pour qu'il ne prépare pas d'arrêt
                        Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD,
                            ped.Handle, bike.Handle,
                            targetPos.X, targetPos.Y, targetPos.Z,
                            cfg.DriveSpeed, 
                            smartFlags, 
                            bike.Model.Hash, 
                            16777216, // Mode de conduite (User defined)
                            0.0f,     // StopRange = 0 (Ne jamais freiner à l'approche)
                            -1.0f);   // StraightLineDist
                    }
                }

                // ==================================================================================
                // FIN DU WHEELIE -> RETOUR GROUPE
                // ==================================================================================
                else if (oldPhase != Phase.Idle && state.Phase == Phase.Idle)
                {
                    RetaskRiderToGroup(ped, state);
                }

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

            // 2b. Force-warp: if rider hasn't remounted after RemountForceTimeout, warp onto bike
            if (cfg.RemountForceTimeout > 0 && (!ped.IsWalking || !ped.IsRunning))
            {
                int fallTime = state.RemountTimeout - cfg.RemountTimeout;
                // Si le temps limite est dépassé
                if (now > fallTime + cfg.RemountForceTimeout)
                {
                    // --- MODIFICATION ICI : DÉBLOCAGE COMPLET ---
                    Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle); // Reset total
                    Function.Call(Hash.SET_PED_INTO_VEHICLE, ped.Handle, bike.Handle, -1);
                    Function.Call(Hash.SET_VEHICLE_HANDBRAKE, bike.Handle, false);
                    Function.Call(Hash.SET_VEHICLE_ENGINE_ON, bike.Handle, true, true, false); // On force le moteur

                    state.RemountActive = false;
                    state.RemountLastTaskTime = 0;
                    state.Phase = Phase.Idle;
                    state.NextAttemptDelay = now + 3000;

                    ApplyPedFlags(ped, cfg);

                    if (state.IsSpawned)
                    {
                        RetaskRiderToGroup(ped, state);
                        
                        // "Kickstart" l'IA : on lui dit d'accélérer (Action 1) pendant 1 seconde
                        // pour être sûr qu'il ne reste pas planté là.
                        Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, ped.Handle, bike.Handle, 1, 1000);
                    }
                    // --------------------------------------------

                    if (_debug)
                        Notification.PostTicker($"~o~Remount: FORCE WARP & KICKSTART (timeout {cfg.RemountForceTimeout}ms)", true);
                    return;
                }
            }

            // 3. CRITICAL: If TASK_ENTER_VEHICLE (160) is active, let it play out.
            bool enterTaskActive = Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, ped.Handle, 160);
            if (enterTaskActive) return;

            // 4. Also check TASK_GO_TO_ENTITY (178) — ped is walking to bike
            bool goToTaskActive = Function.Call<bool>(Hash.GET_IS_TASK_ACTIVE, ped.Handle, 178);
            if (goToTaskActive)
            {
                if (dist < cfg.RemountMaxDistance) return; // still walking, let it be
            }

            // 5. If we reach here, ped has NO active remount task — assign one
            bool isFirstTask = state.RemountLastTaskTime == 0;
            bool cooldownElapsed = (now - state.RemountLastTaskTime) >= cfg.RemountTaskCooldown;

            if (!isFirstTask && !cooldownElapsed) return;

            if (dist < cfg.RemountEnterDistance)
            {
                // Close enough: Enter vehicle directly
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

            // If the group is following the player, use TASK_VEHICLE_FOLLOW
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
                            Function.Call((Hash)0xFC545A9F0626E3B6, // TASK_VEHICLE_FOLLOW
                                ped.Handle, bike.Handle, playerVeh.Handle,
                                cfg.DriveSpeed, cfg.DrivingFlags, _followMinDistance);
                        }
                        else
                        {
                            Vehicle lb = (Vehicle)Entity.FromHandle(targetGroup.LeaderBikeHandle);
                            Entity target = (lb != null && lb.Exists()) ? (Entity)lb : (Entity)playerVeh;
                            Function.Call((Hash)0xFC545A9F0626E3B6, // TASK_VEHICLE_FOLLOW
                                ped.Handle, bike.Handle, target.Handle,
                                cfg.DriveSpeed, cfg.DrivingFlags, _followMinDistance);
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
                    // Autonomous: followers also use TASK_VEHICLE_FOLLOW on leader
                    Function.Call((Hash)0xFC545A9F0626E3B6, // TASK_VEHICLE_FOLLOW
                        ped.Handle, bike.Handle, leaderBike.Handle,
                        cfg.DriveSpeed, cfg.DrivingFlags, _followMinDistance);
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
    }
}

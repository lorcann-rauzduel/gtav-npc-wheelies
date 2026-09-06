using System;
using System.Drawing;
using GTA;
using GTA.Native;
using GTA.Math;

namespace StreetRiders
{
    /// <summary>
    /// Wheelie physics — continuous smooth force with angular velocity damping.
    /// Cycle: Idle -> Attempting (short bursts) -> Sustained (long) -> Idle
    /// Force is applied every frame (not pulsed) with progressive pitch fade
    /// and velocity-based damping to prevent oscillation and jerky movement.
    /// </summary>
    public partial class StreetRidersScript
    {
        private string _wheelieSkipReason = "";

        private void TickWheelie(Vehicle bike, RiderState state, float speed, float rawPitch, WheelieConfig cfg)
        {
            int now = Game.GameTime;
            state.LastForceApplied = 0f;
            _wheelieSkipReason = "";

            // Positive wheelie angle (0 = flat, 35 = nice wheelie)
            float pitch = Math.Abs(rawPitch);

            // Pitch check
            if (pitch > cfg.MaxPitch)
            {
                // Apply downward force to bring the bike down
                float excessPitch = pitch - cfg.MaxPitch;
                float correctionForce = cfg.LiftForce * 0.9f * (excessPitch / 10f); // Progressive correction
                
                bike.ApplyForceRelative(
                    new Vector3(0f, 0f, -correctionForce), // Negative Z = down
                    new Vector3(0f, cfg.TorqueOffset, 0f));
                
                state.LastForceApplied = -correctionForce;
                _wheelieSkipReason = "PITCH";
            }

            // --- Speed check ---
            if (speed < cfg.MinSpeed)
            {
                if (state.Phase != Phase.Idle)
                {
                    CancelWheelie(state, now, 1000);
                }
                if (state.NextAttemptDelay < now)
                    state.NextAttemptDelay = now + 500;
                _wheelieSkipReason = "SLOW";
                return;
            }

            // --- Steering check ---
            if (Math.Abs(bike.SteeringAngle) > cfg.MaxSteerAngle)
            {
                if (state.Phase != Phase.Idle)
                {
                    CancelWheelie(state, now, 1500);
                }
                _wheelieSkipReason = "STEER";
                return;
            }

            // --- Braking check (only during active wheelie) ---
            if ((state.Phase == Phase.Attempting || state.Phase == Phase.Sustained)
                && state.LastSpeed > 0 && (state.LastSpeed - speed) > cfg.BrakeThreshold)
            {
                CancelWheelie(state, now, 1000);
                _wheelieSkipReason = "BRAKE";
                return;
            }

            // --- Obstacle check ---
            // Use flat forward direction (heading-based) so the raycast stays horizontal
            // even when the bike is pitched up during a wheelie. Without this, the ray
            // points toward the sky and misses vehicles/peds on the road.
            Vector3 flatForward = GetHeadingDir(bike.Heading);

            // During active wheelie: detect Map + Vehicles + Peds (safety)
            // During idle: Map only (don't block wheelie starts in normal traffic)
            IntersectFlags rayFlags = (state.Phase == Phase.Attempting || state.Phase == Phase.Sustained)
                ? (IntersectFlags)(1 | 2 | 4 | 8)  // Map + Vehicles + Peds
                : IntersectFlags.Map;               // Map only

            if (IsObstacleAhead(bike, state, flatForward, cfg.RaycastDist, rayFlags))
            {
                if (state.Phase != Phase.Idle)
                    CancelWheelie(state, now, 600);
                _wheelieSkipReason = "OBSTACLE";
                return;
            }
            // =================================================================
            //  IDLE
            // =================================================================
            if (state.Phase == Phase.Idle)
            {
                if (now < state.NextAttemptDelay)
                {
                    _wheelieSkipReason = "WAIT";
                    return;
                }

                if (state.AttemptsTarget == 0)
                {
                    state.AttemptsTarget = _rnd.Next(cfg.AttemptsMin, cfg.AttemptsMax + 1);
                    state.AttemptsDone = 0;
                }

                if (state.AttemptsDone < state.AttemptsTarget)
                {
                    state.Phase = Phase.Attempting;
                    state.PhaseEnd = now + _rnd.Next(cfg.AttemptDurMin, cfg.AttemptDurMax + 1);
                }
                else
                {
                    state.Phase = Phase.Sustained;
                    state.PhaseEnd = now + _rnd.Next(cfg.SustainDurMin, cfg.SustainDurMax + 1);
                    state.AttemptsTarget = 0;
                    state.AttemptsDone = 0;
                    state.WheelieAnimTriggerTime = now + _rnd.Next(300, 800);
                    state.WheelieAnimPlayed = false;
                }

                state.NextBoostTime = 0;
                _wheelieSkipReason = "START";
                return;
            }

            // =================================================================
            //  ATTEMPTING / SUSTAINED — continuous smooth force (every frame)
            // =================================================================
            if (state.Phase == Phase.Attempting || state.Phase == Phase.Sustained)
            {
                if (now > state.PhaseEnd)
                {
                    StopWheelieAnimWithBlend(state);
                    state.Phase = Phase.Idle;
                    state.AttemptsDone++;
                    state.NextAttemptDelay = now + _rnd.Next(cfg.DelayMin, cfg.DelayMax + 1);
                    state.WheelieAnimPlayed = false;
                    state.WheelieAnimTriggerTime = 0;
                    state.WheelieHoldingSince = 0;
                    _wheelieSkipReason = "DONE";
                    return;
                }

                // Promote Attempting → Sustained when pitch is held above threshold for 2s.
                // The time-based cycle only reaches Sustained after X bursts complete,
                // but a rider can physically hold the wheel up throughout an entire Attempting
                // burst. This bridges the gap between physics state and the phase label.
                if (state.Phase == Phase.Attempting)
                {
                    const int PromoteAfterMs = 2000;
                    float holdThreshold = cfg.TargetPitch * 0.6f;

                    if (pitch >= holdThreshold)
                    {
                        if (state.WheelieHoldingSince == 0)
                            state.WheelieHoldingSince = now;
                        else if (now - state.WheelieHoldingSince >= PromoteAfterMs)
                        {
                            state.Phase = Phase.Sustained;
                            state.PhaseEnd = now + _rnd.Next(cfg.SustainDurMin, cfg.SustainDurMax + 1);
                            state.AttemptsTarget = 0;
                            state.AttemptsDone = 0;
                            state.WheelieAnimTriggerTime = now + _rnd.Next(300, 800);
                            state.WheelieAnimPlayed = false;
                            state.WheelieHoldingSince = 0;
                        }
                    }
                    else
                    {
                        state.WheelieHoldingSince = 0;
                    }
                }

                if (state.Phase == Phase.Sustained
                    && !state.WheelieAnimPlayed
                    && state.WheelieAnimTriggerTime > 0
                    && now >= state.WheelieAnimTriggerTime)
                {
                    TryPlayWheelieAnim(state, cfg);
                }

                // --- Angular velocity (positive = bike rotating upward) ---
                float dt = Math.Max(Game.LastFrameTime, 0.001f);
                float prevPitch = Math.Abs(state.LastPitch);
                float pitchRate = (pitch - prevPitch) / dt; // deg/sec

                // --- Smooth pitch multiplier with velocity damping ---
                // NO gravity compensation — LiftForce directly controls the max angle:
                //   small LiftForce → gravity wins early → low wheelie
                //   large LiftForce → overcomes gravity → reaches TargetPitch
                // Force is scaled by 100 so INI values 0.01-1.0 produce visible results.
                //
                // 0..fadeStart:       full force, dampened by rise rate
                // fadeStart..target:  progressive fade + velocity damping
                // target..max:        coast zone (no force, gravity settles)
                // above max:          gentle push-down correction
                const float ForceScale = 2.5f;
                float fadeStart = cfg.TargetPitch * 0.5f;
                float pitchMult;

                if (pitch < fadeStart)
                {
                    // Early lift: full force, softened by rise rate
                    float velDamp = Clamp01(1f - pitchRate / 200f);
                    pitchMult = velDamp;
                }
                else if (pitch < cfg.TargetPitch)
                {
                    // Approaching target: fade force out progressively
                    float t = (pitch - fadeStart) / Math.Max(1f, cfg.TargetPitch - fadeStart);
                    float baseFade = 1f - t * 0.85f; // 1.0 at fadeStart -> 0.15 at target
                    float velDamp = Clamp01(1f - pitchRate / 120f);
                    pitchMult = baseFade * velDamp;
                }
                else if (pitch < cfg.MaxPitch)
                {
                    // Coast zone: no lift, let gravity and drag stabilize
                    pitchMult = 0f;
                }
                else
                {
                    pitchMult = 0f; // Correction handled below
                }

                if (!IsRearWheelGrounded(bike))
                {
                    _wheelieSkipReason = "AIRBORNE";
                    return;
                }

                if (pitchMult > 0.01f)
                {
                    float force = cfg.LiftForce * pitchMult * ForceScale;
                    state.LastForceApplied = force;

                    Function.Call(Hash.APPLY_FORCE_TO_ENTITY,
                        bike.Handle,
                        1,                             // forceType 1 (external force, one frame)
                        0f, 0f, force,                 // force Z = up in local space
                        0f, cfg.TorqueOffset, 0f,      // offset Y = forward along bike
                        0,                             // boneIndex
                        true, true, true, false, true);
                }
                else if (pitch >= cfg.MaxPitch)
                {
                    // Over max: push-down correction proportional to overshoot
                    float overshoot = (pitch - cfg.MaxPitch) / 10f;
                    float correction = cfg.LiftForce * ForceScale * 0.6f * Math.Min(overshoot, 1.0f);
                    state.LastForceApplied = -correction;

                    Function.Call(Hash.APPLY_FORCE_TO_ENTITY,
                        bike.Handle,
                        1,
                        0f, 0f, -correction,
                        0f, cfg.TorqueOffset, 0f,
                        0,
                        true, true, true, false, true);
                }
                else
                {
                    _wheelieSkipReason = "COAST";
                }
            }
        }

        /// <summary>
        /// Cancels the current wheelie phase and returns to Idle.
        /// Counts the interrupted attempt so the cycle progresses properly.
        /// </summary>
        private static void CancelWheelie(RiderState state, int now, int delayMs)
        {
            if (state.Phase == Phase.Attempting)
                state.AttemptsDone++;
            StopWheelieAnimWithBlend(state);
            state.Phase = Phase.Idle;
            state.NextAttemptDelay = now + delayMs;
            state.WheelieAnimPlayed = false;
            state.WheelieAnimTriggerTime = 0;
            state.WheelieHoldingSince = 0;
            state.WheelieNeedsRetask = false;
        }

        private static void StopWheelieAnimWithBlend(RiderState state)
        {
            if (!state.WheelieAnimPlayed
                || string.IsNullOrEmpty(state.LastWheelieAnimDict)
                || string.IsNullOrEmpty(state.LastWheelieAnimClip)) return;

            var ped = (Ped)Entity.FromHandle(state.PedHandle);
            if (ped == null || !ped.Exists()) return;

            Function.Call(Hash.STOP_ANIM_TASK, ped.Handle, state.LastWheelieAnimDict, state.LastWheelieAnimClip, 4f);
        }

        private void TryPlayWheelieAnim(RiderState state, WheelieConfig cfg)
        {
            if (!state.DoWheelieStunts) return;

            var anims = cfg.StuntAnims;
            if (anims == null || anims.Count == 0) return;

            var ped = (Ped)Entity.FromHandle(state.PedHandle);
            if (ped == null || !ped.Exists() || ped.IsDead) return;

            PointF screenPos = GTA.UI.Screen.WorldToScreen(ped.Position + new Vector3(0, 0, 1f));
            if (screenPos.X == 0 && screenPos.Y == 0) return;

            // Pick a random entry, excluding the last played key (no-repeat)
            string lastKey = (state.LastWheelieAnimDict ?? "") + ":" + (state.LastWheelieAnimClip ?? "");
            int excludeIdx = -1;
            for (int i = 0; i < anims.Count; i++)
                if (anims[i].Key == lastKey) { excludeIdx = i; break; }

            int chosen;
            if (anims.Count > 1 && excludeIdx >= 0)
            {
                chosen = _rnd.Next(anims.Count - 1);
                if (chosen >= excludeIdx) chosen++;
            }
            else
            {
                chosen = _rnd.Next(anims.Count);
            }

            WheelieAnimEntry entry = anims[chosen];

            if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, entry.Dict))
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, entry.Dict);
                return;
            }

            int animFlag = _rnd.Next(100) < 25 ? 16 : 0;

            Function.Call(Hash.TASK_PLAY_ANIM,
                ped.Handle, entry.Dict, entry.Clip,
                8f, -8f, -1, animFlag, 0f, false, false, false);

            state.LastWheelieAnimDict = entry.Dict;
            state.LastWheelieAnimClip = entry.Clip;
            state.WheelieAnimPlayed = true;
            state.WheelieNeedsRetask = true;
            state.WheelieAnimUpperBodyOnly = (animFlag == 16);
            state.WheelieAnimStartTime = Game.GameTime;
        }

        // In Sustained, the front wheel is off the ground continuously, preventing effective steering.
        // A single forward raycast misses vehicles on road curves. We use a 3-ray fan (center + ±20°)
        // with shorter side rays (vehicles only) to detect lateral threats without false-positiving
        // on road barriers and guardrails.
        private static bool IsObstacleAhead(Vehicle bike, RiderState state, Vector3 forward, float dist, IntersectFlags flags)
        {
            var center = World.Raycast(bike.Position, forward, dist, flags, bike);
            if (center.DidHit && !IsIgnorableRaycastHit(center.HitEntity, state)) return true;

            if (state.Phase != Phase.Sustained) return false;

            // Side raycasts: vehicles only (no map/objects) to avoid false positives from road geometry
            const IntersectFlags VehiclesAndPeds = (IntersectFlags)(2 | 4);
            float sideDist = dist * 0.65f;

            var left = World.Raycast(bike.Position, RotateDirection(forward, -20f), sideDist, VehiclesAndPeds, bike);
            if (left.DidHit && !IsIgnorableRaycastHit(left.HitEntity, state)) return true;

            var right = World.Raycast(bike.Position, RotateDirection(forward, 20f), sideDist, VehiclesAndPeds, bike);
            if (right.DidHit && !IsIgnorableRaycastHit(right.HitEntity, state)) return true;

            return false;
        }

        private static bool IsIgnorableRaycastHit(Entity hitEntity, RiderState state)
        {
            if (hitEntity == null) return false;
            if (hitEntity.Handle == state.PedHandle) return true;
            // Other riders traveling in vehicles (group members slightly ahead) are not real obstacles
            if (hitEntity is Ped p && p.IsInVehicle()) return true;
            return false;
        }

        // Rotates a flat (XY) direction vector clockwise by angleDeg (positive = right, negative = left)
        private static Vector3 RotateDirection(Vector3 dir, float angleDeg)
        {
            float rad = angleDeg * (float)Math.PI / 180f;
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);
            return new Vector3(dir.X * cos + dir.Y * sin, -dir.X * sin + dir.Y * cos, 0f);
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static bool IsRearWheelGrounded(Vehicle bike)
        {
            const float RayDist = 0.6f;
            EntityBone rearWheelBone = bike.Bones["wheel_lr"];
            Vector3 origin = rearWheelBone.IsValid ? rearWheelBone.Position : bike.Position;

            // Bike bouncing off a bump momentarily lifts both wheels — block lift force in that case
            RaycastResult hit = World.Raycast(origin, new Vector3(0f, 0f, -1f), RayDist, IntersectFlags.Map, bike);
            return hit.DidHit;
        }
    }
}

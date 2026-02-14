using System;
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

            var ray = World.Raycast(bike.Position, flatForward,
                cfg.RaycastDist, rayFlags, bike);

            if (ray.DidHit)
            {
                // Ignore the rider himself (false positive from ped flags)
                if (ray.HitEntity != null && ray.HitEntity.Handle == state.PedHandle)
                {
                    // Not a real obstacle, skip
                }
                else
                {
                    if (state.Phase != Phase.Idle)
                    {
                        CancelWheelie(state, now, 600);
                    }
                    _wheelieSkipReason = "OBSTACLE";
                    return;
                }
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
                    state.Phase = Phase.Idle;
                    state.AttemptsDone++;
                    state.NextAttemptDelay = now + _rnd.Next(cfg.DelayMin, cfg.DelayMax + 1);
                    _wheelieSkipReason = "DONE";
                    return;
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
            state.Phase = Phase.Idle;
            state.NextAttemptDelay = now + delayMs;
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}

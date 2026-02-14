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
            // 0..fadeStart:       full force, dampened by rise rate
            // fadeStart..target:  progressive fade + velocity damping
            // target..max:        coast zone (no force, gravity settles)
            // above max:          gentle correction
            //
            // Gravity compensation: as the bike tilts, gravity torque grows
            // proportionally to sin(pitch). Without compensation, the bike
            // hits a ceiling around 35 deg regardless of config. The gravComp
            // factor automatically scales up the force to match, so LiftForce
            // and TargetPitch actually control the wheelie angle as expected.
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
                float baseFade = 1f - t * 0.9f; // 1.0 at fadeStart -> 0.1 at target
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
                // Gravity compensation: counteract increasing gravity torque at higher angles
                float pitchRad = pitch * (float)(Math.PI / 180.0);
                float gravComp = 1f + (float)Math.Sin(pitchRad) * 2.0f;

                float force = cfg.LiftForce * pitchMult * gravComp;
                state.LastForceApplied = force;

                bike.ApplyForceRelative(
                    new Vector3(0f, 0f, force),
                    new Vector3(0f, cfg.TorqueOffset, 0f)
                );
            }
            else if (pitch >= cfg.MaxPitch)
            {
                // Over max: gentle correction proportional to overshoot
                // Much softer than before — let gravity do most of the work
                float overshoot = (pitch - cfg.MaxPitch) / 10f;
                float correction = cfg.LiftForce * 0.4f * Math.Min(overshoot, 1.0f);
                state.LastForceApplied = -correction;

                bike.ApplyForceRelative(
                    new Vector3(0f, 0f, -correction),
                    new Vector3(0f, cfg.TorqueOffset, 0f)
                );
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
} // namespace StreetRiders

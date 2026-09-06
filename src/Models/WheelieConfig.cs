using System.Collections.Generic;

namespace StreetRiders
{
    public partial class StreetRidersScript
    {
        /// <summary>
        /// Per-bike wheelie tuning. See Physics/WheeliePhysics.cs for how these are used.
        /// </summary>
        private struct WheelieAnimEntry
        {
            public string Dict;
            public string Clip;
            public string Key => Dict + ":" + Clip;
        }

        private class WheelieConfig
        {
            // -- Core --
            public float LiftForce;
            public float TorqueOffset;
            public int PulseIntervalMs;
            public float MinSpeed;

            // -- Pitch control --
            public float TargetPitch;
            public float MaxPitch;

            // -- Timing --
            public int AttemptDurMin;
            public int AttemptDurMax;
            public int SustainDurMin;
            public int SustainDurMax;
            public int DelayMin;
            public int DelayMax;
            public int AttemptsMin;
            public int AttemptsMax;
            public int StuntCooldown;
            public List<WheelieAnimEntry> StuntAnims;

            // -- Safety --
            public float RaycastDist;
            public float MaxSteerAngle;
            public float BrakeThreshold;

            // -- Driving --
            public float DriveSpeed;
            public float MaxSpeed;
            public float DriveAbility;
            public float DriveAggressiveness;
            public int DrivingFlags;

            // -- Ped Behavior Flags --
            public bool BlockNonTempEvents;
            public int FleeAttributes;
            public int CombatAttribute;
            public bool CombatAttributeValue;

            // -- Remount Settings --
            public int RemountTimeout;
            public float RemountEnterDistance;
            public float RemountMaxDistance;
            public float RemountMoveSpeed;
            public int RemountTaskCooldown;
            public int RemountForceTimeout;  // ms before force-warping rider onto bike (0 = disabled)

            // -- Police --
            public bool AllowPoliceArrest;
        }
    }
}

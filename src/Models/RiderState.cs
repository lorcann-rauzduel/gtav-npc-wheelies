namespace StreetRiders
{
    public partial class StreetRidersScript
    {
        private class RiderState
        {
            public Phase Phase = Phase.Idle;
            public int PedHandle;
            public int AttemptsTarget;
            public int AttemptsDone;
            public int PhaseEnd;
            public int NextAttemptDelay;
            public int NextBoostTime;
            public float LastPitch;
            public float LastSpeed;
            public bool WasInVehicle = true;
            public int LastBikeHandle;
            public bool RemountActive;
            public int RemountTimeout;
            public int RemountLastTaskTime;
            public int RemountAttempts;
            public string BikeModel = "";
            public int GroupId = -1;
            public bool IsSpawned;
            public bool DoWheelie = true;
        public bool DoWheelieStunts = true;
            public float LastForceApplied;
            public bool InIdleScenario;       // true = managed by WaitingForPlayer, skip remount
            public bool ScenarioPlaying;      // true = actively playing a WORLD_HUMAN scenario
            public int IdleArrivalTime;       // GameTime when rider arrived near player on foot
            public uint OriginalRelGroup;     // Original relationship group hash saved at spawn
            public bool FreestyleTowardB;    // Freestyle: true = heading to point B, false = heading to A

            public bool TurningToPlayer;
            public int TurnStartTime;

            public int LastTalkTime;
            public int BlipHandle;         // Minimap blip handle (0 = none)
            public int LastMovingTime;     // Last GameTime when bike.Speed > 2 (idle-on-bike detection)

        public bool WheelieAnimPlayed;
        public bool WheelieProtected; // true = fall prevention active for this wheelie (WheelieOnly chance roll)
        public int WheelieAnimTriggerTime;
        public int WheelieHoldingSince; // GameTime when pitch first crossed the hold threshold
        public string LastWheelieAnimDict;
        public string LastWheelieAnimClip;
        public bool WheelieNeedsRetask;
        public int WheelieAnimStartTime;
        public bool WheelieAnimUpperBodyOnly;

            public MeetingRiderState MeetingState;   // null = normal rider, set while a meeting runs
        }
    }
}

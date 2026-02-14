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
            public float LastForceApplied;
            public bool InIdleScenario;       // true = managed by WaitingForPlayer, skip remount
            public bool ScenarioPlaying;      // true = actively playing a WORLD_HUMAN scenario
            public int IdleArrivalTime;       // GameTime when rider arrived near player on foot
            public uint OriginalRelGroup;     // Original relationship group hash saved at spawn
        }
    }
}

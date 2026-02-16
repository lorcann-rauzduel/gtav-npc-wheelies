using System.Collections.Generic;
using GTA.Math;

namespace StreetRiders
{
    public partial class StreetRidersScript
    {
        private class ActiveGroup
        {
            public int Id;
            public Vector3 Destination;
            public Vector3 SpawnOrigin;       // Where the group originally spawned
            public bool IsTerritory;          // true = cruising in territory, false = going away
            public List<int> PedHandles = new List<int>();
            public List<int> BikeHandles = new List<int>();
            public int LeaderBikeHandle;
            public string ConfigName;
            public List<string> Zones;         // Allowed zones for territory destinations

            // -- Follow Player state --
            public GroupFollowMode FollowMode = GroupFollowMode.Autonomous;
            public bool PlayerWasInVehicle;
            public int WaitStartTime;
            public bool RidersInScenario;

            // -- Bodyguard state --
            public int BodyguardTarget;       // Handle of the ped attacking the player (0 = none)
            public int BodyguardStartTime;    // GameTime when bodyguard mode activated

            // -- Dynamic trigger state --
            public int DynamicTriggerStart;   // GameTime when speed condition was first met (0 = not met)
            public int LastDecisionTime;      // GameTime of last follow mode transition (cooldown)

            // -- Freestyle state --
            public Vector3 FreestylePointA;   // Close waypoint on nearby road
            public Vector3 FreestylePointB;   // Further waypoint on nearby road
            public int FreestyleLastRefresh;  // GameTime when waypoints were last updated
            public int FreestyleLastRetask;   // GameTime of last retask (grace period for idle check)
            public int LeaderStuckStartTime;
        }
    }
}

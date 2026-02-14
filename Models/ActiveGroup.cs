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
            public List<int> PedHandles = new List<int>();
            public List<int> BikeHandles = new List<int>();
            public int LeaderBikeHandle;
            public string ConfigName;

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
            public int FreestyleNextSwitch;   // GameTime to pick a new destination
        }
    }
}

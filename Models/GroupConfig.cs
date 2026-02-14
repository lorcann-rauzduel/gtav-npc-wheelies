using System.Collections.Generic;

namespace StreetRiders
{
    public partial class StreetRidersScript
    {
        private class GroupConfig
        {
            public string Name;
            public bool Enabled;
            public int Weight;
            public List<string> PedModels;
            public List<string> BikeModels;
            public List<string> Zones;
            public bool DoWheelie;
            public int ActiveHourStart;
            public int ActiveHourEnd;
            public List<string> Weapons = new List<string>();
            public bool CanFollowPlayer;
            public bool MixedVehicles;
            public List<int> BikeColors = new List<int>();
        }
    }
}

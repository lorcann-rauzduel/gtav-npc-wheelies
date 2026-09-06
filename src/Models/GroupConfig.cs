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
            public bool DoWheelieStunts;
            public int ActiveHourStart;
            public int ActiveHourEnd;
            public List<string> Weapons = new List<string>();
            public bool CanFollowPlayer;
            public bool MixedVehicles;
            public List<int> BikeColors = new List<int>();
            public CruiseMode CruiseMode;
            public int TerritoryChance;     // % chance to stay in territory (Mixed mode)
            public float TerritoryRadius;   // Max distance from spawn for Territory destinations

            // Relationship (per-group override)
            public string RelationshipGroup = "";  // GTA relationship group name (empty = use ped model default)
            public int PlayerRelationship = -1;    // -1 = None, 0 = Companion, 1 = Respect, ... 5 = Hate

            // Blip display (per-group override, falls back to General defaults)
            public int BlipSprite;           // GTA BlipSprite ID (0 = default circle)
            public int BlipColor;            // GTA BlipColor ID (e.g. 3 = blue)
            public float BlipScale;          // Blip scale on minimap (default 0.7)
            public string BlipName;          // Blip label on minimap
        }
    }
}

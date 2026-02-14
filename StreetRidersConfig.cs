using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GTA;
using GTA.Math;

namespace StreetRiders
{
    /// <summary>
    /// All fields, data classes, and configuration loading.
    /// </summary>
    public partial class StreetRidersScript
{
    // ===== Constants =====
    private static readonly string INI_PATH = "scripts\\StreetRiders.ini";
    private const int LOGIC_INTERVAL_MS = 1500;

    // ===== General Config =====
    private bool _enabled;
    private bool _debug;
    private Keys _debugKey;
    private Keys _reloadKey;
    private float _spawnDistMin;
    private float _spawnDistMax;
    private float _cleanupDist;
    private int _maxGroups;
    private int _ridersMin;
    private int _ridersMax;
    private int _spawnIntervalMs;
    private float _destDistMin;
    private float _destDistMax;
    private bool _applyToNatural;
    private float _naturalScanRadius;
    private float _spawnSpacing;
    private float _wheelieActiveDist;
    private int _defaultActiveHourStart;
    private int _defaultActiveHourEnd;
    private HashSet<string> _wheelieExcludedModels;

    // ===== Test Mode =====
    private bool _testMode;
    private Keys _forceSpawnKey;
    private Keys _cleanAllKey;
    private int _forceSpawnGroupIndex;

    // ===== Follow Player =====
    private bool _followEnabled;
    private Keys _followToggleKey;
    private FollowTrigger _followTrigger;
    private float _followRadius;
    private float _followLoseRadius;
    private float _dynamicSpeedThreshold;
    private int   _dynamicTriggerMs;       // How long player must stay fast before trigger (ms)
    private string _playerVehicleType;
    private DismountBehavior _dismountBehavior;
    private List<string> _idleScenarios;
    private int _idleDelayMs;
    private int _resumeDelayMs;
    private float _idleScenarioDist;
    private float _idleFollowDist;
    private float _idleMinStayDist;
    private int _idleScenarioWaitMs;

    // ===== Bodyguard =====
    private bool _bodyguardEnabled;
    private float _bodyguardRadius;
    private int _bodyguardDurationMs;

    // ===== Wheelie Configs =====
    private WheelieConfig _defaultWheelie;
    private Dictionary<string, WheelieConfig> _bikeConfigs;

    // ===== Group Configs =====
    private List<GroupConfig> _groupConfigs;

    // ===== Police Pursuit Config =====
    private float _policePursuitRadius;
    private int _policePursuitMinDurationMs;

    // ===== Runtime State =====
    private readonly Dictionary<int, RiderState> _riders = new Dictionary<int, RiderState>();
    private readonly List<ActiveGroup> _activeGroups = new List<ActiveGroup>();
    private readonly Dictionary<int, CopPursuitInfo> _copPursuits = new Dictionary<int, CopPursuitInfo>();
    private int _lastSpawnCheck;
    private int _lastLogicCheck;
    private int _nextGroupId;
    private readonly Random _rnd = new Random();

    // =========================================================================
    //  Data Classes
    // =========================================================================

    /// <summary>
    /// Per-bike wheelie tuning. See RidersWheelie.cs for how these are used.
    /// </summary>
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
        public int SuffersCriticalHits;

        // -- Remount Settings --
        public int RemountTimeout;
        public float RemountEnterDistance;
        public float RemountMaxDistance;
        public float RemountMoveSpeed;
        public int RemountTaskCooldown;

        // -- Police --
        public bool AllowPoliceArrest;
    }

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

    private enum Phase { Idle, Attempting, Sustained }
    private enum FollowTrigger { Proximity, Dynamic }
    private enum DismountBehavior { Wait, Continue, Freestyle }
    private enum GroupFollowMode { Autonomous, FollowingPlayer, WaitingForPlayer }

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

    private class CopPursuitInfo
    {
        public int TargetRiderHandle;
        public int AssignedTime;
    }

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

    // =========================================================================
    //  Config Loading
    // =========================================================================

    private void LoadConfig()
    {
        ScriptSettings ini = ScriptSettings.Load(INI_PATH);

        _enabled       = ini.GetValue("General", "Enabled", true);
        _debug         = ini.GetValue("General", "Debug", false);
        _debugKey      = ParseKey(ini.GetValue("General", "DebugKey", "Insert"));
        _reloadKey     = ParseKey(ini.GetValue("General", "ReloadKey", "F10"));
        _spawnDistMin  = ini.GetValue("General", "SpawnDistanceMin", 80.0f);
        _spawnDistMax  = ini.GetValue("General", "SpawnDistanceMax", 150.0f);
        _cleanupDist   = ini.GetValue("General", "CleanupDistance", 250.0f);
        _maxGroups     = ini.GetValue("General", "MaxActiveGroups", 3);
        _ridersMin     = ini.GetValue("General", "RidersPerGroupMin", 2);
        _ridersMax     = ini.GetValue("General", "RidersPerGroupMax", 5);
        _spawnIntervalMs   = ini.GetValue("General", "SpawnIntervalMs", 15000);
        _destDistMin       = ini.GetValue("General", "DestinationDistanceMin", 300.0f);
        _destDistMax       = ini.GetValue("General", "DestinationDistanceMax", 700.0f);
        _applyToNatural    = ini.GetValue("General", "ApplyToNaturalRiders", true);
        _naturalScanRadius = ini.GetValue("General", "NaturalRiderScanRadius", 150.0f);
        _spawnSpacing      = ini.GetValue("General", "SpawnSpacing", 3.5f);
        _wheelieActiveDist = ini.GetValue("General", "WheelieActiveDistance", 80.0f);
        _defaultActiveHourStart = ini.GetValue("General", "DefaultActiveHourStart", 0);
        _defaultActiveHourEnd   = ini.GetValue("General", "DefaultActiveHourEnd", 23);
        _wheelieExcludedModels  = new HashSet<string>(
            ParseCsv(ini.GetValue("General", "WheelieExcludedModels", ""), true),
            StringComparer.OrdinalIgnoreCase);

        // Test mode
        _testMode        = ini.GetValue("TestMode", "Enabled", false);
        _forceSpawnKey   = ParseKey(ini.GetValue("TestMode", "ForceSpawnKey", "NumPad1"));
        _cleanAllKey     = ParseKey(ini.GetValue("TestMode", "CleanAllKey", "NumPad0"));
        _forceSpawnGroupIndex = 0;

        // Follow Player
        _followEnabled          = ini.GetValue("FollowPlayer", "Enabled", false);
        _followToggleKey        = ParseKey(ini.GetValue("FollowPlayer", "ToggleKey", "F5"));
        string followMode       = ini.GetValue("FollowPlayer", "Mode", "Dynamic");
        _followTrigger          = followMode.Equals("Proximity", StringComparison.OrdinalIgnoreCase)
                                    ? FollowTrigger.Proximity : FollowTrigger.Dynamic;
        _followRadius           = ini.GetValue("FollowPlayer", "FollowRadius", 60.0f);
        _followLoseRadius       = ini.GetValue("FollowPlayer", "LoseRadius", 200.0f);
        _dynamicSpeedThreshold  = ini.GetValue("FollowPlayer", "DynamicMinSpeed", 5.0f);
        _dynamicTriggerMs       = ini.GetValue("FollowPlayer", "DynamicTriggerMs", 2000);
        _playerVehicleType      = ini.GetValue("FollowPlayer", "PlayerVehicleType", "Any");
        string dismountMode     = ini.GetValue("FollowPlayer", "DismountBehavior", "Freestyle");
        if (dismountMode.Equals("Continue", StringComparison.OrdinalIgnoreCase))
            _dismountBehavior = DismountBehavior.Continue;
        else if (dismountMode.Equals("Freestyle", StringComparison.OrdinalIgnoreCase))
            _dismountBehavior = DismountBehavior.Freestyle;
        else
            _dismountBehavior = DismountBehavior.Wait;
        _idleScenarios          = ParseCsv(ini.GetValue("FollowPlayer", "IdleScenarios",
            "WORLD_HUMAN_SMOKING, WORLD_HUMAN_STAND_MOBILE, WORLD_HUMAN_LEANING"));
        _idleDelayMs            = ini.GetValue("FollowPlayer", "IdleDelayMs", 5000);
        _resumeDelayMs          = ini.GetValue("FollowPlayer", "ResumeDelayMs", 2000);
        _idleScenarioDist       = ini.GetValue("FollowPlayer", "IdleScenarioDistance", 8.0f);
        _idleFollowDist         = ini.GetValue("FollowPlayer", "IdleFollowDistance", 15.0f);
        _idleMinStayDist        = ini.GetValue("FollowPlayer", "IdleMinStayDistance", 6.0f);
        _idleScenarioWaitMs     = ini.GetValue("FollowPlayer", "IdleScenarioWaitMs", 4000);

        // Bodyguard
        _bodyguardEnabled       = ini.GetValue("FollowPlayer", "BodyguardEnabled", true);
        _bodyguardRadius        = ini.GetValue("FollowPlayer", "BodyguardRadius", 30.0f);
        _bodyguardDurationMs    = ini.GetValue("FollowPlayer", "BodyguardDurationMs", 15000);

        // Police pursuit persistence
        _policePursuitRadius        = ini.GetValue("DefaultWheelie", "PolicePursuitRadius", 100.0f);
        _policePursuitMinDurationMs = ini.GetValue("DefaultWheelie", "PolicePursuitMinDuration", 15000);

        _defaultWheelie = ReadWheelieSection(ini, "DefaultWheelie", null);

        _bikeConfigs.Clear();
        string overrides = ini.GetValue("General", "BikeOverrides", "");
        if (!string.IsNullOrEmpty(overrides))
        {
            foreach (string raw in overrides.Split(','))
            {
                string key = raw.Trim().ToUpperInvariant();
                if (key.Length > 0)
                    _bikeConfigs[key] = ReadWheelieSection(ini, "Bike_" + key, _defaultWheelie);
            }
        }

        _groupConfigs.Clear();
        string groupList = ini.GetValue("General", "Groups", "");
        if (!string.IsNullOrEmpty(groupList))
        {
            foreach (string raw in groupList.Split(','))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;

                string section = "Group_" + name;
                var cfg = new GroupConfig
                {
                    Name       = name,
                    Enabled    = ini.GetValue(section, "Enabled", true),
                    Weight     = ini.GetValue(section, "Weight", 10),
                    PedModels  = ParseCsv(ini.GetValue(section, "PedModels", "")),
                    BikeModels = ParseCsv(ini.GetValue(section, "BikeModels", ""), true),
                    Zones      = ParseCsv(ini.GetValue(section, "Zones", ""), true),
                    DoWheelie  = ini.GetValue(section, "DoWheelie", true),
                    ActiveHourStart = ini.GetValue(section, "ActiveHourStart", _defaultActiveHourStart),
                    ActiveHourEnd   = ini.GetValue(section, "ActiveHourEnd",   _defaultActiveHourEnd),
                    Weapons    = ParseCsv(ini.GetValue(section, "Weapons", "")),
                    CanFollowPlayer = ini.GetValue(section, "CanFollowPlayer", false),
                    MixedVehicles   = ini.GetValue(section, "MixedVehicles", false),
                    BikeColors      = ParseIntCsv(ini.GetValue(section, "BikeColors", ""))
                };

                if (cfg.Enabled && cfg.PedModels.Count > 0 && cfg.BikeModels.Count > 0)
                    _groupConfigs.Add(cfg);
            }
        }

        if (_groupConfigs.Count == 0)
        {
            _groupConfigs.Add(new GroupConfig
            {
                Name       = "Default",
                Enabled    = true,
                Weight     = 10,
                PedModels  = new List<string> { "a_m_y_stbla_01", "a_m_y_stbla_02" },
                BikeModels = new List<string> { "BATI", "PCJ", "SANCHEZ" },
                Zones      = new List<string>(),
                DoWheelie  = true,
                ActiveHourStart = _defaultActiveHourStart,
                ActiveHourEnd   = _defaultActiveHourEnd
            });
        }
    }

    private WheelieConfig ReadWheelieSection(ScriptSettings ini, string section, WheelieConfig fb)
    {
        return new WheelieConfig
        {
            // Core
            LiftForce       = ini.GetValue(section, "LiftForce",          fb?.LiftForce       ?? 0.6f),
            TorqueOffset    = ini.GetValue(section, "TorqueOffset",       fb?.TorqueOffset    ?? 2.0f),
            PulseIntervalMs = ini.GetValue(section, "PulseIntervalMs",    fb?.PulseIntervalMs ?? 100),
            MinSpeed        = ini.GetValue(section, "MinSpeed",           fb?.MinSpeed        ?? 12.0f),

            // Pitch
            TargetPitch     = ini.GetValue(section, "TargetPitch",        fb?.TargetPitch     ?? 35.0f),
            MaxPitch        = ini.GetValue(section, "MaxPitch",           fb?.MaxPitch        ?? 43.0f),

            // Timing
            AttemptDurMin   = ini.GetValue(section, "AttemptDurationMin",  fb?.AttemptDurMin   ?? 1000),
            AttemptDurMax   = ini.GetValue(section, "AttemptDurationMax",  fb?.AttemptDurMax   ?? 3000),
            SustainDurMin   = ini.GetValue(section, "SustainDurationMin",  fb?.SustainDurMin  ?? 2500),
            SustainDurMax   = ini.GetValue(section, "SustainDurationMax",  fb?.SustainDurMax  ?? 5000),
            DelayMin        = ini.GetValue(section, "AttemptDelayMin",      fb?.DelayMin       ?? 1000),
            DelayMax        = ini.GetValue(section, "AttemptDelayMax",      fb?.DelayMax       ?? 5000),
            AttemptsMin     = ini.GetValue(section, "AttemptsMin",          fb?.AttemptsMin    ?? 1),
            AttemptsMax     = ini.GetValue(section, "AttemptsMax",          fb?.AttemptsMax    ?? 4),

            // Safety
            RaycastDist     = ini.GetValue(section, "RaycastDistance",      fb?.RaycastDist    ?? 15.0f),
            MaxSteerAngle   = ini.GetValue(section, "MaxSteeringAngle",    fb?.MaxSteerAngle  ?? 8.0f),
            BrakeThreshold  = ini.GetValue(section, "BrakeThreshold",      fb?.BrakeThreshold ?? 0.5f),

            // Driving
            DriveSpeed      = ini.GetValue(section, "DriveSpeed",           fb?.DriveSpeed     ?? 80.0f),
            MaxSpeed        = ini.GetValue(section, "MaxEntitySpeed",       fb?.MaxSpeed       ?? 120.0f),
            DriveAbility    = ini.GetValue(section, "DriveAbility",         fb?.DriveAbility   ?? 1.0f),
            DriveAggressiveness = ini.GetValue(section, "DriveAggressiveness", fb?.DriveAggressiveness ?? 1.0f),
            DrivingFlags    = ini.GetValue(section, "DrivingFlags",         fb?.DrivingFlags   ?? 786603),

            // Ped Behavior
            BlockNonTempEvents    = ini.GetValue(section, "BlockNonTempEvents",    fb?.BlockNonTempEvents    ?? true),
            FleeAttributes        = ini.GetValue(section, "FleeAttributes",        fb?.FleeAttributes        ?? 0),
            CombatAttribute       = ini.GetValue(section, "CombatAttribute",       fb?.CombatAttribute       ?? 17),
            CombatAttributeValue  = ini.GetValue(section, "CombatAttributeValue",  fb?.CombatAttributeValue  ?? false),
            SuffersCriticalHits   = ini.GetValue(section, "SuffersCriticalHits",   fb?.SuffersCriticalHits   ?? -1),

            // Remount
            RemountTimeout        = ini.GetValue(section, "RemountTimeout",        fb?.RemountTimeout        ?? 30000),
            RemountEnterDistance  = ini.GetValue(section, "RemountEnterDistance",  fb?.RemountEnterDistance  ?? 5.0f),
            RemountMaxDistance    = ini.GetValue(section, "RemountMaxDistance",    fb?.RemountMaxDistance    ?? 30.0f),
            RemountMoveSpeed      = ini.GetValue(section, "RemountMoveSpeed",      fb?.RemountMoveSpeed      ?? 2.0f),
            RemountTaskCooldown   = ini.GetValue(section, "RemountTaskCooldown",   fb?.RemountTaskCooldown   ?? 5000),

            // Police
            AllowPoliceArrest = ini.GetValue(section, "AllowPoliceArrest", fb?.AllowPoliceArrest ?? true)
        };
    }

    private static Keys ParseKey(string name)
    {
        if (Enum.TryParse(name, true, out Keys key))
            return key;
        return Keys.None;
    }

    private static List<string> ParseCsv(string csv, bool toUpper = false)
    {
        if (string.IsNullOrEmpty(csv)) return new List<string>();
        return csv.Split(',')
            .Select(s => toUpper ? s.Trim().ToUpperInvariant() : s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    private static List<int> ParseIntCsv(string csv)
    {
        var result = new List<int>();
        if (string.IsNullOrEmpty(csv)) return result;
        foreach (string s in csv.Split(','))
        {
            int val;
            if (int.TryParse(s.Trim(), out val))
                result.Add(val);
        }
        return result;
    }

    private WheelieConfig GetWheelieConfig(string bikeModel)
    {
        if (!string.IsNullOrEmpty(bikeModel) && _bikeConfigs.TryGetValue(bikeModel, out WheelieConfig cfg))
            return cfg;
        return _defaultWheelie;
    }

    private GroupConfig FindGroupConfig(string configName)
    {
        foreach (var gc in _groupConfigs)
            if (gc.Name == configName) return gc;
        return null;
    }
}
} // namespace StreetRiders
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.UI;

namespace StreetRiders
{
    /// <summary>
    /// All fields, runtime state, and configuration loading (INI parsing).
    /// </summary>
    public partial class StreetRidersScript
    {
        // ===== P/Invoke for Windows INI API =====
        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(
            string section, string key, string defaultValue,
            StringBuilder retVal, int size, string filePath);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern bool WritePrivateProfileString(
            string section, string key, string value, string filePath);

        // ===== Constants =====
        private static readonly string INI_PATH = "scripts\\StreetRiders\\config.ini";
        private static readonly string GROUPS_INI_PATH = "scripts\\StreetRiders\\groups.ini";
        private static readonly string VEHICLES_INI_PATH = "scripts\\StreetRiders\\vehicles.ini";
        private static readonly string BEHAVIOR_INI_PATH = "scripts\\StreetRiders\\behavior.ini";
        private static readonly string ADVANCED_INI_PATH = "scripts\\StreetRiders\\advanced.ini";
        private static readonly string MEETINGS_INI_PATH = "scripts\\StreetRiders\\meetings.ini";
        private const int LOGIC_INTERVAL_MS = 1500;

        // ===== General Config =====
        private bool _enabled;
        private bool _debug;
        private bool _debugShowBlips;
        private bool _debugShowOverhead;
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
        private CruiseMode _defaultCruiseMode;
        private int _defaultTerritoryChance;
        private float _defaultTerritoryRadius;
        private int _defaultBlipSprite;
        private int _defaultBlipColor;
        private float _defaultBlipScale;
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
        private int _dynamicTriggerMs;
        private string _playerVehicleType;
        private DismountBehavior _dismountBehavior;
        private List<string> _idleScenarios;
        private int _idleDelayMs;
        private int _resumeDelayMs;
        private float _idleScenarioDist;
        private float _idleFollowDist;
        private float _idleMinStayDist;
        private int _idleScenarioWaitMs;
        private int _followMinDistance;

        // ===== Bodyguard =====
        private bool _bodyguardEnabled;
        private float _bodyguardRadius;

        // ===== Meetings =====
        private bool _langEn;
        private bool _meetingEnabled;
        private Keys _meetingMenuKey;
        private HashSet<int> _meetingScheduleDays;
        private int _meetingStartHour;
        private int _meetingEndHour;
        private float _meetingMaterializeDist;
        private int _meetingMaxConcurrent;
        private int _meetingRidersMin;
        private int _meetingRidersMax;
        private int _meetingRoleChangeMinMs;
        private int _meetingRoleChangeMaxMs;
        private float _meetingFreestyleRadiusMin;
        private float _meetingFreestyleRadius;
        private int _meetingFreestyleMinMs;
        private int _meetingFreestyleMaxMs;
        private bool _meetingDepartureFormation;
        private List<MeetingVenue> _meetingVenues;
        private int _meetingLastUpdate;
        private int _meetingNextSchedulerCheck;

        // ===== Wheelie Configs =====
        private WheelieConfig _defaultWheelie;
        private WheelieConfig _defaultWheelieNoFall;
        private Dictionary<string, WheelieConfig> _bikeConfigs;
        private Dictionary<string, WheelieConfig> _bikeConfigsNoFall;

        // ===== Group Configs =====
        private List<GroupConfig> _groupConfigs;

        // ===== Behavior Config =====
        private PreventFallMode _preventFallMode;
        private int _preventFallChance;
        private int _leaderGracePeriodMs;
        private int _formationDistance;

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
        //  INI Helper Methods (Windows API)
        // =========================================================================

        private string GetIniString(string file, string section, string key, string defaultValue)
        {
            var sb = new StringBuilder(255);
            GetPrivateProfileString(section, key, defaultValue, sb, 255, file);
            return sb.ToString();
        }

        // Ajoute cette petite méthode utilitaire
        private string CleanValue(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // Trouve l'index du commentaire ';'
            int commentIndex = input.IndexOf(';');
            if (commentIndex >= 0)
            {
                // Garde uniquement ce qu'il y a avant le ';'
                input = input.Substring(0, commentIndex);
            }

            // Enlève les espaces avant et après (ex: " 45.0 " devient "45.0")
            return input.Trim();
        }

        private float GetIniFloat(string file, string section, string key, float defaultValue)
        {
            var sb = new StringBuilder(255);
            GetPrivateProfileString(section, key, "", sb, 255, file);
            
            // NETTOYAGE ICI
            string valStr = CleanValue(sb.ToString());

            if (string.IsNullOrEmpty(valStr))
            {
                return defaultValue;
            }

            // Utilise InvariantCulture pour forcer le point '.'
            if (float.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float result))
            {
                return result;
            }

            if (_debug)
            {
                // Affiche l'erreur si même après nettoyage ça ne passe pas
                GTA.UI.Notification.Show("FLOAT ERROR [" + section + "] " + key + ": '" + valStr + "'");
            }

            return defaultValue;
        }

        private int GetIniInt(string file, string section, string key, int defaultValue)
        {
            var sb = new StringBuilder(255);
            GetPrivateProfileString(section, key, "", sb, 255, file);
            
            // NETTOYAGE ICI
            string valStr = CleanValue(sb.ToString());

            if (int.TryParse(valStr, out int val))
            {
                return val;
            }
            return defaultValue;
        }

        private bool GetIniBool(string file, string section, string key, bool defaultValue)
        {
            var sb = new StringBuilder(255);
            GetPrivateProfileString(section, key, "", sb, 255, file);
            
            // NETTOYAGE ICI
            string value = CleanValue(sb.ToString()).ToLower();

            if (value == "true" || value == "1") return true;
            if (value == "false" || value == "0") return false;
            
            return defaultValue;
        }

        private List<string> GetIniSections(string file)
        {
            // Pass a null section to enumerate every section of the INI file.
            var sb = new StringBuilder(8192);
            GetPrivateProfileString(null, null, "", sb, 8192, file);
            var sections = new List<string>();
            if (sb.Length == 0) return sections;
            foreach (string s in sb.ToString().Split('\0'))
            {
                if (s.Length > 0) sections.Add(s);
            }
            return sections;
        }

        private void WriteIniValue(string file, string section, string key, string value)
        {
            WritePrivateProfileString(section, key, value, file);
        }

        private void DeleteIniSection(string file, string section)
        {
            // Null key = delete the whole section.
            WritePrivateProfileString(section, null, null, file);
        }

        private void DeleteIniValue(string file, string section, string key)
        {
            // Null value = delete the single key.
            WritePrivateProfileString(section, key, null, file);
        }

        // =========================================================================
        //  Config Loading
        // =========================================================================

        private void LoadConfig()
{
    string fullPath = System.IO.Path.GetFullPath(INI_PATH);
    string groupsPath = System.IO.Path.GetFullPath(GROUPS_INI_PATH);
    string vehiclesPath = System.IO.Path.GetFullPath(VEHICLES_INI_PATH);
    string behaviorPath = System.IO.Path.GetFullPath(BEHAVIOR_INI_PATH);
    string advancedPath = System.IO.Path.GetFullPath(ADVANCED_INI_PATH);

            // config.ini [General] — only what casual users need to touch
            _enabled = GetIniBool(fullPath, "General", "Enabled", true);
            _debug = GetIniBool(fullPath, "General", "Debug", false);
            _debugShowBlips = GetIniBool(fullPath, "General", "ShowBlips", false);
            _debugShowOverhead = GetIniBool(fullPath, "General", "ShowOverhead", false);
            _debugKey = ParseKey(GetIniString(fullPath, "General", "DebugKey", "Insert"));
            _reloadKey = ParseKey(GetIniString(fullPath, "General", "ReloadKey", "F10"));
            _langEn = GetIniString(fullPath, "General", "Language", "fr").Equals("en", StringComparison.OrdinalIgnoreCase);
            _maxGroups = GetIniInt(fullPath, "General", "MaxActiveGroups", 3);
            _ridersMin = GetIniInt(fullPath, "General", "RidersPerGroupMin", 2);
            _ridersMax = GetIniInt(fullPath, "General", "RidersPerGroupMax", 5);

            // advanced.ini [Advanced] — spawn distances and timing
            _spawnDistMin = GetIniFloat(advancedPath, "Advanced", "SpawnDistanceMin", 80.0f);
            _spawnDistMax = GetIniFloat(advancedPath, "Advanced", "SpawnDistanceMax", 150.0f);
            _cleanupDist = GetIniFloat(advancedPath, "Advanced", "CleanupDistance", 250.0f);
            _spawnIntervalMs = GetIniInt(advancedPath, "Advanced", "SpawnIntervalMs", 15000);
            _spawnSpacing = GetIniFloat(advancedPath, "Advanced", "SpawnSpacing", 3.5f);
            _wheelieActiveDist = GetIniFloat(advancedPath, "Advanced", "WheelieActiveDistance", 80.0f);
            _destDistMin = GetIniFloat(advancedPath, "Advanced", "DestinationDistanceMin", 300.0f);
            _destDistMax = GetIniFloat(advancedPath, "Advanced", "DestinationDistanceMax", 700.0f);

            // behavior.ini [Behavior] — natural riders
            _applyToNatural = GetIniBool(behaviorPath, "Behavior", "ApplyToNaturalRiders", false);
            _naturalScanRadius = GetIniFloat(behaviorPath, "Behavior", "NaturalRiderScanRadius", 150.0f);

            // groups.ini [General] — group defaults
            _defaultActiveHourStart = GetIniInt(groupsPath, "General", "DefaultActiveHourStart", 0);
            _defaultActiveHourEnd = GetIniInt(groupsPath, "General", "DefaultActiveHourEnd", 23);
            _defaultCruiseMode = ParseCruiseMode(GetIniString(groupsPath, "General", "DefaultCruiseMode", "GoAway"));
            _defaultTerritoryChance = GetIniInt(groupsPath, "General", "DefaultTerritoryChance", 50);
            _defaultTerritoryRadius = GetIniFloat(groupsPath, "General", "DefaultTerritoryRadius", 150.0f);
            _defaultBlipSprite = GetIniInt(groupsPath, "General", "DefaultBlipSprite", 226);
            _defaultBlipColor = GetIniInt(groupsPath, "General", "DefaultBlipColor", 3);
            _defaultBlipScale = GetIniFloat(groupsPath, "General", "DefaultBlipScale", 0.7f);

            // vehicles.ini [General] — wheelie exclusions
            _wheelieExcludedModels = new HashSet<string>(
                ParseCsv(GetIniString(vehiclesPath, "General", "WheelieExcludedModels", ""), true),
                StringComparer.OrdinalIgnoreCase);

            // Test mode
            _testMode = GetIniBool(fullPath, "TestMode", "Enabled", false);
            _forceSpawnKey = ParseKey(GetIniString(fullPath, "TestMode", "ForceSpawnKey", "NumPad1"));
            _cleanAllKey = ParseKey(GetIniString(fullPath, "TestMode", "CleanAllKey", "NumPad0"));
            _forceSpawnGroupIndex = 0;

            // behavior.ini [FollowPlayer]
            _followEnabled = GetIniBool(behaviorPath, "FollowPlayer", "Enabled", false);
            _followToggleKey = ParseKey(GetIniString(behaviorPath, "FollowPlayer", "ToggleKey", "F7"));
            string followMode = GetIniString(behaviorPath, "FollowPlayer", "Mode", "Dynamic");
            _followTrigger = followMode.Equals("Proximity", StringComparison.OrdinalIgnoreCase)
                ? FollowTrigger.Proximity : FollowTrigger.Dynamic;
            _followRadius = GetIniFloat(behaviorPath, "FollowPlayer", "FollowRadius", 60.0f);
            _followLoseRadius = GetIniFloat(behaviorPath, "FollowPlayer", "LoseRadius", 200.0f);
            _dynamicSpeedThreshold = GetIniFloat(behaviorPath, "FollowPlayer", "DynamicMinSpeed", 5.0f);
            _dynamicTriggerMs = GetIniInt(behaviorPath, "FollowPlayer", "DynamicTriggerMs", 2000);
            _playerVehicleType = GetIniString(behaviorPath, "FollowPlayer", "PlayerVehicleType", "Any");
            string dismountMode = GetIniString(behaviorPath, "FollowPlayer", "DismountBehavior", "Freestyle");
            if (dismountMode.Equals("Continue", StringComparison.OrdinalIgnoreCase))
                _dismountBehavior = DismountBehavior.Continue;
            else if (dismountMode.Equals("Freestyle", StringComparison.OrdinalIgnoreCase))
                _dismountBehavior = DismountBehavior.Freestyle;
            else
                _dismountBehavior = DismountBehavior.Wait;
            _idleScenarios = ParseCsv(GetIniString(behaviorPath, "FollowPlayer", "IdleScenarios",
                "WORLD_HUMAN_SMOKING, WORLD_HUMAN_STAND_MOBILE, WORLD_HUMAN_DRINKING, WORLD_HUMAN_LEANING"));
            _idleDelayMs = GetIniInt(behaviorPath, "FollowPlayer", "IdleDelayMs", 5000);
            _resumeDelayMs = GetIniInt(behaviorPath, "FollowPlayer", "ResumeDelayMs", 2000);
            _idleScenarioDist = GetIniFloat(behaviorPath, "FollowPlayer", "IdleScenarioDistance", 8.0f);
            _idleFollowDist = GetIniFloat(behaviorPath, "FollowPlayer", "IdleFollowDistance", 15.0f);
            _idleMinStayDist = GetIniFloat(behaviorPath, "FollowPlayer", "IdleMinStayDistance", 6.0f);
            _idleScenarioWaitMs = GetIniInt(behaviorPath, "FollowPlayer", "IdleScenarioWaitMs", 4000);
            _followMinDistance = GetIniInt(behaviorPath, "FollowPlayer", "FollowMinDistance", 15);

            // behavior.ini [FollowPlayer] — bodyguard
            _bodyguardEnabled = GetIniBool(behaviorPath, "FollowPlayer", "BodyguardEnabled", true);
            _bodyguardRadius = GetIniFloat(behaviorPath, "FollowPlayer", "BodyguardRadius", 30.0f);

            _preventFallMode = ParsePreventFallMode(GetIniString(behaviorPath, "Behavior", "PreventFall", "Always"));
            _preventFallChance = GetIniInt(behaviorPath, "Behavior", "PreventFallChance", 100);
            _leaderGracePeriodMs = GetIniInt(advancedPath, "Advanced", "LeaderGracePeriodMs", 4000);
            _formationDistance = GetIniInt(advancedPath, "Advanced", "FormationDistance", 50);

            _policePursuitRadius = GetIniFloat(behaviorPath, "Behavior", "PolicePursuitRadius", 100.0f);
            _policePursuitMinDurationMs = GetIniInt(behaviorPath, "Behavior", "PolicePursuitMinDuration", 15000);

            _defaultWheelie = ReadDefaultWheelieConfig(vehiclesPath, behaviorPath, advancedPath);
            // NoFall variant: reads [DefaultWheelie_NoFall], fallback to _defaultWheelie for unspecified keys
            _defaultWheelieNoFall = ReadBikeOverride(vehiclesPath, "DefaultWheelie_NoFall", _defaultWheelie);

            if (_debug)
                GTA.UI.Notification.Show($"Default TargetPitch loaded: {_defaultWheelie.TargetPitch}");

            _bikeConfigs.Clear();
            _bikeConfigsNoFall.Clear();
            string overrides = GetIniString(vehiclesPath, "General", "BikeOverrides", "");

            if (overrides.Length > 0)
            {
                foreach (string raw in overrides.Split(','))
                {
                    string key = raw.Trim().ToUpperInvariant();
                    if (key.Length > 0)
                    {
                        WheelieConfig overrideCfg = ReadBikeOverride(vehiclesPath, "Bike_" + key, _defaultWheelie);
                        _bikeConfigs[key] = overrideCfg;

                        // NoFall variant: reads [Bike_X_NoFall], fallback to the bike's normal config
                        _bikeConfigsNoFall[key] = ReadBikeOverride(vehiclesPath, "Bike_" + key + "_NoFall", overrideCfg);

                        if (_debug)
                            Notification.PostTicker("Loaded override: " + key + " | TargetPitch: " + overrideCfg.TargetPitch, true);
                    }
                }
            }

            _groupConfigs.Clear();
            string groupList = GetIniString(groupsPath, "General", "Groups", "");
            if (!string.IsNullOrEmpty(groupList))
            {
                foreach (string raw in groupList.Split(','))
                {
                    string name = raw.Trim();
                    if (name.Length == 0) continue;

                    string section = "Group_" + name;
                    var cfg = new GroupConfig
                    {
                        Name = name,
                        Enabled = GetIniBool(groupsPath, section, "Enabled", true),
                        Weight = GetIniInt(groupsPath, section, "Chance", 10),
                        PedModels = ParseCsv(GetIniString(groupsPath, section, "PedModels", "")),
                        BikeModels = ParseCsv(GetIniString(groupsPath, section, "BikeModels", ""), true),
                        Zones = ParseCsv(GetIniString(groupsPath, section, "Zones", ""), true),
                        DoWheelie = GetIniBool(groupsPath, section, "DoWheelie", true),
                        DoWheelieStunts = GetIniBool(groupsPath, section, "DoWheelieStunts", true),
                        ActiveHourStart = GetIniInt(groupsPath, section, "ActiveHourStart", _defaultActiveHourStart),
                        ActiveHourEnd = GetIniInt(groupsPath, section, "ActiveHourEnd", _defaultActiveHourEnd),
                        Weapons = ParseCsv(GetIniString(groupsPath, section, "Weapons", "")),
                        CanFollowPlayer = GetIniBool(groupsPath, section, "CanFollowPlayer", false),
                        MixedVehicles = GetIniBool(groupsPath, section, "MixedVehicles", false),
                        BikeColors = ParseIntCsv(GetIniString(groupsPath, section, "BikeColors", "")),
                        CruiseMode = ParseCruiseMode(GetIniString(groupsPath, section, "CruiseMode",
                            _defaultCruiseMode == CruiseMode.Territory ? "Territory" :
                            _defaultCruiseMode == CruiseMode.Mixed ? "Mixed" : "GoAway")),
                        TerritoryChance = GetIniInt(groupsPath, section, "TerritoryChance", _defaultTerritoryChance),
                        TerritoryRadius = GetIniFloat(groupsPath, section, "TerritoryRadius", _defaultTerritoryRadius),
                        RelationshipGroup = GetIniString(groupsPath, section, "RelationshipGroup", ""),
                        PlayerRelationship = ParsePlayerRelationship(GetIniString(groupsPath, section, "PlayerRelationship", "None")),
                        BlipSprite = GetIniInt(groupsPath, section, "BlipSprite", _defaultBlipSprite),
                        BlipColor = GetIniInt(groupsPath, section, "BlipColor", _defaultBlipColor),
                        BlipScale = GetIniFloat(groupsPath, section, "BlipScale", _defaultBlipScale),
                        BlipName = GetIniString(groupsPath, section, "BlipName", name)
                    };

                    if (cfg.Enabled && cfg.PedModels.Count > 0 && cfg.BikeModels.Count > 0)
                        _groupConfigs.Add(cfg);
                }
            }

            if (_groupConfigs.Count == 0)
            {
                _groupConfigs.Add(new GroupConfig
                {
                    Name = "Default",
                    Enabled = true,
                    Weight = 10,
                    PedModels = new List<string> { "a_m_y_stbla_01", "a_m_y_stbla_02" },
                    BikeModels = new List<string> { "SANCHEZ2" },
                    Zones = new List<string>(),
                    DoWheelie = true,
                    ActiveHourStart = _defaultActiveHourStart,
                    ActiveHourEnd = _defaultActiveHourEnd,
                    BlipSprite = _defaultBlipSprite,
                    BlipColor = _defaultBlipColor,
                    BlipScale = _defaultBlipScale,
                    BlipName = "Default"
                });
            }

            // ===== Meetings (meetings.ini) =====
            string meetingsPath = System.IO.Path.GetFullPath(MEETINGS_INI_PATH);
            _meetingEnabled = GetIniBool(meetingsPath, "Meetings", "Enabled", true);
            _meetingMenuKey = ParseKey(GetIniString(meetingsPath, "Meetings", "MenuKey", "F8"));
            _meetingScheduleDays = new HashSet<int>(ParseIntCsv(GetIniString(meetingsPath, "Meetings", "ScheduleDays", "5,0")));
            _meetingStartHour = GetIniInt(meetingsPath, "Meetings", "StartHour", 18);
            _meetingEndHour = GetIniInt(meetingsPath, "Meetings", "EndHour", 2);
            _meetingMaterializeDist = GetIniFloat(meetingsPath, "Meetings", "MaterializeDistance", 400.0f);
            _meetingMaxConcurrent = Math.Max(1, GetIniInt(meetingsPath, "Meetings", "MaxConcurrentVenues", 1));
            _meetingRidersMin = Math.Max(2, GetIniInt(meetingsPath, "Meetings", "RidersMin", 6));
            _meetingRidersMax = Math.Max(_meetingRidersMin, GetIniInt(meetingsPath, "Meetings", "RidersMax", 10));
            _meetingRoleChangeMinMs = GetIniInt(meetingsPath, "Meetings", "RoleChangeMinMs", 15000);
            _meetingRoleChangeMaxMs = Math.Max(_meetingRoleChangeMinMs, GetIniInt(meetingsPath, "Meetings", "RoleChangeMaxMs", 30000));
            _meetingFreestyleRadius = Math.Max(40f, GetIniFloat(meetingsPath, "Meetings", "FreestyleRadius", 80f));
            _meetingFreestyleRadiusMin = Math.Max(15f, GetIniFloat(meetingsPath, "Meetings", "FreestyleRadiusMin", 40f));
            if (_meetingFreestyleRadiusMin > _meetingFreestyleRadius) _meetingFreestyleRadiusMin = _meetingFreestyleRadius;
            _meetingFreestyleMinMs = GetIniInt(meetingsPath, "Meetings", "FreestyleBurstMinMs", 15000);
            _meetingFreestyleMaxMs = Math.Max(_meetingFreestyleMinMs, GetIniInt(meetingsPath, "Meetings", "FreestyleBurstMaxMs", 30000));
            _meetingDepartureFormation = GetIniBool(meetingsPath, "Meetings", "DepartureFormation", true);
            LoadMeetingVenues(meetingsPath);
        }

        private WheelieConfig ReadDefaultWheelieConfig(string vehiclesPath, string behaviorPath, string advancedPath)
        {
            return new WheelieConfig
            {
                // vehicles.ini [DefaultWheelie] — physics & timing
                LiftForce           = GetIniFloat(vehiclesPath, "DefaultWheelie", "LiftForce", 0.18f),
                TorqueOffset        = GetIniFloat(vehiclesPath, "DefaultWheelie", "TorqueOffset", 1.2f),
                MinSpeed            = GetIniFloat(vehiclesPath, "DefaultWheelie", "MinSpeed", 12.0f),
                TargetPitch         = GetIniFloat(vehiclesPath, "DefaultWheelie", "TargetPitch", 40.0f),
                MaxPitch            = GetIniFloat(vehiclesPath, "DefaultWheelie", "MaxPitch", 43.0f),
                AttemptDurMin       = GetIniInt(vehiclesPath, "DefaultWheelie", "AttemptDurationMin", 3000),
                AttemptDurMax       = GetIniInt(vehiclesPath, "DefaultWheelie", "AttemptDurationMax", 5000),
                SustainDurMin       = GetIniInt(vehiclesPath, "DefaultWheelie", "SustainDurationMin", 5000),
                SustainDurMax       = GetIniInt(vehiclesPath, "DefaultWheelie", "SustainDurationMax", 20000),
                DelayMin            = GetIniInt(vehiclesPath, "DefaultWheelie", "AttemptDelayMin", 1000),
                DelayMax            = GetIniInt(vehiclesPath, "DefaultWheelie", "AttemptDelayMax", 5000),
                AttemptsMin         = GetIniInt(vehiclesPath, "DefaultWheelie", "AttemptsMin", 1),
                AttemptsMax         = GetIniInt(vehiclesPath, "DefaultWheelie", "AttemptsMax", 4),

                // behavior.ini [Behavior] — driving, PED, stunts, police
                DriveSpeed          = GetIniFloat(behaviorPath, "Behavior", "DriveSpeed", 20.0f),
                MaxSpeed            = GetIniFloat(behaviorPath, "Behavior", "MaxEntitySpeed", 25.0f),
                DriveAbility        = GetIniFloat(behaviorPath, "Behavior", "DriveAbility", 0.8f),
                DriveAggressiveness = GetIniFloat(behaviorPath, "Behavior", "DriveAggressiveness", 0.5f),
                DrivingFlags        = GetIniInt(behaviorPath, "Behavior", "DrivingFlags", 868413),
                BlockNonTempEvents  = GetIniBool(behaviorPath, "Behavior", "BlockNonTempEvents", false),
                FleeAttributes      = GetIniInt(behaviorPath, "Behavior", "FleeAttributes", 0),
                CombatAttribute     = GetIniInt(behaviorPath, "Behavior", "CombatAttribute", 5),
                CombatAttributeValue = GetIniBool(behaviorPath, "Behavior", "CombatAttributeValue", false),
                StuntCooldown       = GetIniInt(behaviorPath, "Behavior", "StuntCooldown", 8000),
                StuntAnims          = ParseWheelieAnims(GetIniString(behaviorPath, "Behavior", "StuntAnimations", ""), null),
                AllowPoliceArrest   = GetIniBool(behaviorPath, "Behavior", "AllowPoliceArrest", true),

                // advanced.ini [Advanced] — remount & low-level safety
                PulseIntervalMs       = GetIniInt(advancedPath, "Advanced", "PulseIntervalMs", 0),
                RaycastDist           = GetIniFloat(advancedPath, "Advanced", "RaycastDistance", 10.0f),
                MaxSteerAngle         = GetIniFloat(advancedPath, "Advanced", "MaxSteeringAngle", 12.0f),
                BrakeThreshold        = GetIniFloat(advancedPath, "Advanced", "BrakeThreshold", 0.5f),
                RemountEnterDistance  = GetIniFloat(advancedPath, "Advanced", "RemountEnterDistance", 8.0f),
                RemountMaxDistance    = GetIniFloat(advancedPath, "Advanced", "RemountMaxDistance", 100.0f),
                RemountMoveSpeed      = GetIniFloat(advancedPath, "Advanced", "RemountMoveSpeed", 3.0f),
                RemountTimeout        = GetIniInt(advancedPath, "Advanced", "RemountTimeout", 15000),
                RemountTaskCooldown   = GetIniInt(advancedPath, "Advanced", "RemountTaskCooldown", 3000),
                RemountForceTimeout   = GetIniInt(advancedPath, "Advanced", "RemountForceTimeout", 10000),
            };
        }

        private WheelieConfig ReadBikeOverride(string vehiclesPath, string section, WheelieConfig fb)
        {
            // Only physics/timing keys can be overridden per-bike; behavior/advanced inherit from default.
            return new WheelieConfig
            {
                LiftForce     = GetIniFloat(vehiclesPath, section, "LiftForce", fb.LiftForce),
                TorqueOffset  = GetIniFloat(vehiclesPath, section, "TorqueOffset", fb.TorqueOffset),
                MinSpeed      = GetIniFloat(vehiclesPath, section, "MinSpeed", fb.MinSpeed),
                TargetPitch   = GetIniFloat(vehiclesPath, section, "TargetPitch", fb.TargetPitch),
                MaxPitch      = GetIniFloat(vehiclesPath, section, "MaxPitch", fb.MaxPitch),
                AttemptDurMin = GetIniInt(vehiclesPath, section, "AttemptDurationMin", fb.AttemptDurMin),
                AttemptDurMax = GetIniInt(vehiclesPath, section, "AttemptDurationMax", fb.AttemptDurMax),
                SustainDurMin = GetIniInt(vehiclesPath, section, "SustainDurationMin", fb.SustainDurMin),
                SustainDurMax = GetIniInt(vehiclesPath, section, "SustainDurationMax", fb.SustainDurMax),
                DelayMin      = GetIniInt(vehiclesPath, section, "AttemptDelayMin", fb.DelayMin),
                DelayMax      = GetIniInt(vehiclesPath, section, "AttemptDelayMax", fb.DelayMax),
                AttemptsMin   = GetIniInt(vehiclesPath, section, "AttemptsMin", fb.AttemptsMin),
                AttemptsMax   = GetIniInt(vehiclesPath, section, "AttemptsMax", fb.AttemptsMax),
                // Inherited from default (not overrideable per-bike)
                DriveSpeed           = fb.DriveSpeed,
                MaxSpeed             = fb.MaxSpeed,
                DriveAbility         = fb.DriveAbility,
                DriveAggressiveness  = fb.DriveAggressiveness,
                DrivingFlags         = fb.DrivingFlags,
                BlockNonTempEvents   = fb.BlockNonTempEvents,
                FleeAttributes       = fb.FleeAttributes,
                CombatAttribute      = fb.CombatAttribute,
                CombatAttributeValue = fb.CombatAttributeValue,
                StuntCooldown        = fb.StuntCooldown,
                StuntAnims           = fb.StuntAnims,
                AllowPoliceArrest    = fb.AllowPoliceArrest,
                PulseIntervalMs      = fb.PulseIntervalMs,
                RaycastDist          = fb.RaycastDist,
                MaxSteerAngle        = fb.MaxSteerAngle,
                BrakeThreshold       = fb.BrakeThreshold,
                RemountEnterDistance = fb.RemountEnterDistance,
                RemountMaxDistance   = fb.RemountMaxDistance,
                RemountMoveSpeed     = fb.RemountMoveSpeed,
                RemountTimeout       = fb.RemountTimeout,
                RemountTaskCooldown  = fb.RemountTaskCooldown,
                RemountForceTimeout  = fb.RemountForceTimeout,
            };
        }

        private static List<WheelieAnimEntry> ParseWheelieAnims(string raw, List<WheelieAnimEntry> fallback)
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var result = new List<WheelieAnimEntry>();
                foreach (string entry in raw.Split(','))
                {
                    string trimmed = entry.Trim();
                    int colon = trimmed.IndexOf(':');
                    if (colon > 0)
                    {
                        result.Add(new WheelieAnimEntry
                        {
                            Dict = trimmed.Substring(0, colon).Trim(),
                            Clip = trimmed.Substring(colon + 1).Trim()
                        });
                    }
                }
                if (result.Count > 0) return result;
            }
            return fallback ?? DefaultWheelieAnims();
        }

        private static List<WheelieAnimEntry> DefaultWheelieAnims()
        {
            return new List<WheelieAnimEntry>
            {
                new WheelieAnimEntry { Dict = "rcmextreme2atv", Clip = "idle_b" },
                new WheelieAnimEntry { Dict = "rcmextreme2atv", Clip = "idle_c" },
                new WheelieAnimEntry { Dict = "rcmextreme2atv", Clip = "idle_d" },
                new WheelieAnimEntry { Dict = "rcmextreme2atv", Clip = "idle_e" },
            };
        }

        private static PreventFallMode ParsePreventFallMode(string value)
        {
            if (value.Equals("WheelieOnly", StringComparison.OrdinalIgnoreCase)) return PreventFallMode.WheelieOnly;
            if (value.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) return PreventFallMode.Disabled;
            return PreventFallMode.Always;
        }

        private static Keys ParseKey(string name)
        {
            if (Enum.TryParse(name, true, out Keys key))
                return key;
            return Keys.None;
        }

        private static CruiseMode ParseCruiseMode(string value)
        {
            if (value.Equals("Territory", StringComparison.OrdinalIgnoreCase)) return CruiseMode.Territory;
            if (value.Equals("Mixed", StringComparison.OrdinalIgnoreCase)) return CruiseMode.Mixed;
            return CruiseMode.GoAway;
        }

        private static int ParsePlayerRelationship(string value)
        {
            if (string.IsNullOrEmpty(value)) return -1;
            if (value.Equals("Companion", StringComparison.OrdinalIgnoreCase)) return 0;
            if (value.Equals("Respect", StringComparison.OrdinalIgnoreCase)) return 1;
            if (value.Equals("Like", StringComparison.OrdinalIgnoreCase)) return 2;
            if (value.Equals("Neutral", StringComparison.OrdinalIgnoreCase)) return 3;
            if (value.Equals("Dislike", StringComparison.OrdinalIgnoreCase)) return 4;
            if (value.Equals("Hate", StringComparison.OrdinalIgnoreCase)) return 5;
            return -1; // None / unknown = don't set player relationship
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

        private WheelieConfig GetWheelieConfig(string bikeModel, bool wheelieProtected = false)
        {
            // [Bike_X_NoFall] / [DefaultWheelie_NoFall] = used when fall prevention IS active (rider doesn't fall)
            // [Bike_X] / [DefaultWheelie] = base, used when fall prevention is NOT active (or as fallback)
            if (wheelieProtected)
            {
                if (!string.IsNullOrEmpty(bikeModel) && _bikeConfigsNoFall.TryGetValue(bikeModel, out WheelieConfig noFallCfg))
                    return noFallCfg;
                return _defaultWheelieNoFall;
            }
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

        // =========================================================================
        //  Meetings: venue parsing / serialization (meetings.ini)
        // =========================================================================

        private void LoadMeetingVenues(string meetingsPath)
        {
            _meetingVenues = new List<MeetingVenue>();
            if (string.IsNullOrEmpty(meetingsPath) || !System.IO.File.Exists(meetingsPath)) return;

            foreach (string section in GetIniSections(meetingsPath))
            {
                if (!section.StartsWith("Venue_", StringComparison.OrdinalIgnoreCase)) continue;
                var venue = new MeetingVenue
                {
                    Name = section.Substring(6),
                    GroupName = GetIniString(meetingsPath, section, "Group", "")
                };
                for (int i = 1; i <= 24; i++)
                {
                    if (ParseVenuePosition(GetIniString(meetingsPath, section, "ParkingPoint" + i, ""),
                        out Vector3 pp, out float ph))
                        venue.ParkingPoints.Add(new MeetingPoint { Position = pp, Heading = ph });
                    else
                        break;
                }
                for (int i = 1; i <= 8; i++)
                {
                    if (ParseVenuePosition(GetIniString(meetingsPath, section, "CirclePoint" + i, ""),
                        out Vector3 cp, out float ch))
                        venue.CirclePoints.Add(new MeetingPoint { Position = cp, Heading = ch });
                    else
                        break;
                }
                // Rétro-compatibilité : l'ancien schéma à point unique devient le premier point de parking.
                if (venue.ParkingPoints.Count == 0
                    && ParseVenuePosition(GetIniString(meetingsPath, section, "Position", ""),
                        out Vector3 position, out float heading))
                    venue.ParkingPoints.Add(new MeetingPoint { Position = position, Heading = heading });

                if (venue.ParkingPoints.Count == 0) continue; // un lieu sans parking n'est pas exploitable
                _meetingVenues.Add(venue);
            }
        }

        public static string SerializeVenuePosition(Vector3 position, float heading)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return position.X.ToString("0.####", culture) + ","
                + position.Y.ToString("0.####", culture) + ","
                + position.Z.ToString("0.####", culture) + ","
                + heading.ToString("0.####", culture);
        }

        private static bool ParseVenuePosition(string value, out Vector3 position, out float heading)
        {
            position = Vector3.Zero;
            heading = 0f;
            if (string.IsNullOrEmpty(value)) return false;
            string[] parts = value.Split(',');
            if (parts.Length < 3) return false;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            float x, y, z;
            if (!float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Any, culture, out x)) return false;
            if (!float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any, culture, out y)) return false;
            if (!float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Any, culture, out z)) return false;
            position = new Vector3(x, y, z);
            if (parts.Length >= 4)
                float.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Any, culture, out heading);
            return true;
        }
    }
}
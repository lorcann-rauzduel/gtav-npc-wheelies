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

        // =========================================================================
        //  Config Loading
        // =========================================================================

        private void LoadConfig()
{
    string fullPath = System.IO.Path.GetFullPath(INI_PATH);
    
            _enabled = GetIniBool(fullPath, "General", "Enabled", true);
            _debug = GetIniBool(fullPath, "General", "Debug", false);
            _debugKey = ParseKey(GetIniString(fullPath, "General", "DebugKey", "Insert"));
            _reloadKey = ParseKey(GetIniString(fullPath, "General", "ReloadKey", "F10"));
            _spawnDistMin = GetIniFloat(fullPath, "General", "SpawnDistanceMin", 80.0f);
            _spawnDistMax = GetIniFloat(fullPath, "General", "SpawnDistanceMax", 150.0f);
            _cleanupDist = GetIniFloat(fullPath, "General", "CleanupDistance", 250.0f);
            _maxGroups = GetIniInt(fullPath, "General", "MaxActiveGroups", 3);
            _ridersMin = GetIniInt(fullPath, "General", "RidersPerGroupMin", 2);
            _ridersMax = GetIniInt(fullPath, "General", "RidersPerGroupMax", 5);
            _spawnIntervalMs = GetIniInt(fullPath, "General", "SpawnIntervalMs", 15000);
            _destDistMin = GetIniFloat(fullPath, "General", "DestinationDistanceMin", 300.0f);
            _destDistMax = GetIniFloat(fullPath, "General", "DestinationDistanceMax", 700.0f);
            _applyToNatural = GetIniBool(fullPath, "General", "ApplyToNaturalRiders", true);
            _naturalScanRadius = GetIniFloat(fullPath, "General", "NaturalRiderScanRadius", 150.0f);
            _spawnSpacing = GetIniFloat(fullPath, "General", "SpawnSpacing", 3.5f);
            _wheelieActiveDist = GetIniFloat(fullPath, "General", "WheelieActiveDistance", 80.0f);
            _defaultActiveHourStart = GetIniInt(fullPath, "General", "DefaultActiveHourStart", 0);
            _defaultActiveHourEnd = GetIniInt(fullPath, "General", "DefaultActiveHourEnd", 23);
            _wheelieExcludedModels = new HashSet<string>(
                ParseCsv(GetIniString(fullPath, "General", "WheelieExcludedModels", ""), true),
                StringComparer.OrdinalIgnoreCase);

            // Test mode
            _testMode = GetIniBool(fullPath, "TestMode", "Enabled", false);
            _forceSpawnKey = ParseKey(GetIniString(fullPath, "TestMode", "ForceSpawnKey", "NumPad1"));
            _cleanAllKey = ParseKey(GetIniString(fullPath, "TestMode", "CleanAllKey", "NumPad0"));
            _forceSpawnGroupIndex = 0;

            // Follow Player
            _followEnabled = GetIniBool(fullPath, "FollowPlayer", "Enabled", false);
            _followToggleKey = ParseKey(GetIniString(fullPath, "FollowPlayer", "ToggleKey", "F5"));
            string followMode = GetIniString(fullPath, "FollowPlayer", "Mode", "Dynamic");
            _followTrigger = followMode.Equals("Proximity", StringComparison.OrdinalIgnoreCase)
                ? FollowTrigger.Proximity : FollowTrigger.Dynamic;
            _followRadius = GetIniFloat(fullPath, "FollowPlayer", "FollowRadius", 60.0f);
            _followLoseRadius = GetIniFloat(fullPath, "FollowPlayer", "LoseRadius", 200.0f);
            _dynamicSpeedThreshold = GetIniFloat(fullPath, "FollowPlayer", "DynamicMinSpeed", 5.0f);
            _dynamicTriggerMs = GetIniInt(fullPath, "FollowPlayer", "DynamicTriggerMs", 2000);
            _playerVehicleType = GetIniString(fullPath, "FollowPlayer", "PlayerVehicleType", "Any");
            string dismountMode = GetIniString(fullPath, "FollowPlayer", "DismountBehavior", "Freestyle");
            if (dismountMode.Equals("Continue", StringComparison.OrdinalIgnoreCase))
                _dismountBehavior = DismountBehavior.Continue;
            else if (dismountMode.Equals("Freestyle", StringComparison.OrdinalIgnoreCase))
                _dismountBehavior = DismountBehavior.Freestyle;
            else
                _dismountBehavior = DismountBehavior.Wait;
            _idleScenarios = ParseCsv(GetIniString(fullPath, "FollowPlayer", "IdleScenarios",
                "WORLD_HUMAN_SMOKING, WORLD_HUMAN_STAND_MOBILE, WORLD_HUMAN_LEANING"));
            _idleDelayMs = GetIniInt(fullPath, "FollowPlayer", "IdleDelayMs", 5000);
            _resumeDelayMs = GetIniInt(fullPath, "FollowPlayer", "ResumeDelayMs", 2000);
            _idleScenarioDist = GetIniFloat(fullPath, "FollowPlayer", "IdleScenarioDistance", 8.0f);
            _idleFollowDist = GetIniFloat(fullPath, "FollowPlayer", "IdleFollowDistance", 15.0f);
            _idleMinStayDist = GetIniFloat(fullPath, "FollowPlayer", "IdleMinStayDistance", 6.0f);
            _idleScenarioWaitMs = GetIniInt(fullPath, "FollowPlayer", "IdleScenarioWaitMs", 4000);
            _followMinDistance = GetIniInt(fullPath, "FollowPlayer", "FollowMinDistance", 15);

            // Bodyguard
            _bodyguardEnabled = GetIniBool(fullPath, "FollowPlayer", "BodyguardEnabled", true);
            _bodyguardRadius = GetIniFloat(fullPath, "FollowPlayer", "BodyguardRadius", 30.0f);
            _bodyguardDurationMs = GetIniInt(fullPath, "FollowPlayer", "BodyguardDurationMs", 15000);

            // Police pursuit persistence
            _policePursuitRadius = GetIniFloat(fullPath, "DefaultWheelie", "PolicePursuitRadius", 100.0f);
            _policePursuitMinDurationMs = GetIniInt(fullPath, "DefaultWheelie", "PolicePursuitMinDuration", 15000);

            _defaultWheelie = ReadWheelieSection(fullPath, "DefaultWheelie", null);

            if (_debug)
                GTA.UI.Notification.Show($"Default TargetPitch loaded: {_defaultWheelie.TargetPitch}");

            // 5. Bike Overrides (C'est ici que le fix est important)
    _bikeConfigs.Clear();
    string overrides = GetIniString(fullPath, "General", "BikeOverrides", "");
    
    if (overrides.Length > 0)
    {
        foreach (string raw in overrides.Split(','))
        {
            string key = raw.Trim().ToUpperInvariant(); // ex: SANCHEZ2
            if (key.Length > 0)
            {
                // Construit le nom de section: "Bike_SANCHEZ2"
                string sectionName = "Bike_" + key;
                
                // Charge la config en héritant de _defaultWheelie
                WheelieConfig overrideCfg = ReadWheelieSection(fullPath, sectionName, _defaultWheelie);
                _bikeConfigs[key] = overrideCfg;

                if (_debug)
                {
                    Notification.PostTicker("Loaded override: " + key + " | TargetPitch: " + overrideCfg.TargetPitch, true);
                }
            }
        }
    }

            _groupConfigs.Clear();
            string groupList = GetIniString(fullPath, "General", "Groups", "");
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
                        Enabled = GetIniBool(fullPath, section, "Enabled", true),
                        Weight = GetIniInt(fullPath, section, "Chance", 10),
                        PedModels = ParseCsv(GetIniString(fullPath, section, "PedModels", "")),
                        BikeModels = ParseCsv(GetIniString(fullPath, section, "BikeModels", ""), true),
                        Zones = ParseCsv(GetIniString(fullPath, section, "Zones", ""), true),
                        DoWheelie = GetIniBool(fullPath, section, "DoWheelie", true),
                        ActiveHourStart = GetIniInt(fullPath, section, "ActiveHourStart", _defaultActiveHourStart),
                        ActiveHourEnd = GetIniInt(fullPath, section, "ActiveHourEnd", _defaultActiveHourEnd),
                        Weapons = ParseCsv(GetIniString(fullPath, section, "Weapons", "")),
                        CanFollowPlayer = GetIniBool(fullPath, section, "CanFollowPlayer", false),
                        MixedVehicles = GetIniBool(fullPath, section, "MixedVehicles", false),
                        BikeColors = ParseIntCsv(GetIniString(fullPath, section, "BikeColors", ""))
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
                    ActiveHourEnd = _defaultActiveHourEnd
                });
            }
        }

        private WheelieConfig ReadWheelieSection(string fullPath, string section, WheelieConfig fb)
        {
            return new WheelieConfig
            {
                // Core
                LiftForce = GetIniFloat(fullPath, section, "LiftForce", fb?.LiftForce ?? 0.6f),
                TorqueOffset = GetIniFloat(fullPath, section, "TorqueOffset", fb?.TorqueOffset ?? 2.0f),
                PulseIntervalMs = GetIniInt(fullPath, section, "PulseIntervalMs", fb?.PulseIntervalMs ?? 100),
                MinSpeed = GetIniFloat(fullPath, section, "MinSpeed", fb?.MinSpeed ?? 12.0f),

                // Pitch
                TargetPitch = GetIniFloat(fullPath, section, "TargetPitch", fb?.TargetPitch ?? 40.0f),
                MaxPitch = GetIniFloat(fullPath, section, "MaxPitch", fb?.MaxPitch ?? 43.0f),

                // Timing
                AttemptDurMin = GetIniInt(fullPath, section, "AttemptDurationMin", fb?.AttemptDurMin ?? 1000),
                AttemptDurMax = GetIniInt(fullPath, section, "AttemptDurationMax", fb?.AttemptDurMax ?? 3000),
                SustainDurMin = GetIniInt(fullPath, section, "SustainDurationMin", fb?.SustainDurMin ?? 2500),
                SustainDurMax = GetIniInt(fullPath, section, "SustainDurationMax", fb?.SustainDurMax ?? 5000),
                DelayMin = GetIniInt(fullPath, section, "AttemptDelayMin", fb?.DelayMin ?? 1000),
                DelayMax = GetIniInt(fullPath, section, "AttemptDelayMax", fb?.DelayMax ?? 5000),
                AttemptsMin = GetIniInt(fullPath, section, "AttemptsMin", fb?.AttemptsMin ?? 1),
                AttemptsMax = GetIniInt(fullPath, section, "AttemptsMax", fb?.AttemptsMax ?? 4),

                // Safety
                RaycastDist = GetIniFloat(fullPath, section, "RaycastDistance", fb?.RaycastDist ?? 15.0f),
                MaxSteerAngle = GetIniFloat(fullPath, section, "MaxSteeringAngle", fb?.MaxSteerAngle ?? 8.0f),
                BrakeThreshold = GetIniFloat(fullPath, section, "BrakeThreshold", fb?.BrakeThreshold ?? 0.5f),

                // Driving
                DriveSpeed = GetIniFloat(fullPath, section, "DriveSpeed", fb?.DriveSpeed ?? 80.0f),
                MaxSpeed = GetIniFloat(fullPath, section, "MaxEntitySpeed", fb?.MaxSpeed ?? 120.0f),
                DriveAbility = GetIniFloat(fullPath, section, "DriveAbility", fb?.DriveAbility ?? 1.0f),
                DriveAggressiveness = GetIniFloat(fullPath, section, "DriveAggressiveness", fb?.DriveAggressiveness ?? 1.0f),
                DrivingFlags = GetIniInt(fullPath, section, "DrivingFlags", fb?.DrivingFlags ?? 786603),

                // Ped Behavior
                BlockNonTempEvents = GetIniBool(fullPath, section, "BlockNonTempEvents", fb?.BlockNonTempEvents ?? true),
                FleeAttributes = GetIniInt(fullPath, section, "FleeAttributes", fb?.FleeAttributes ?? 0),
                CombatAttribute = GetIniInt(fullPath, section, "CombatAttribute", fb?.CombatAttribute ?? 17),
                CombatAttributeValue = GetIniBool(fullPath, section, "CombatAttributeValue", fb?.CombatAttributeValue ?? false),
                SuffersCriticalHits = GetIniInt(fullPath, section, "SuffersCriticalHits", fb?.SuffersCriticalHits ?? -1),

                // Remount
                RemountTimeout = GetIniInt(fullPath, section, "RemountTimeout", fb?.RemountTimeout ?? 30000),
                RemountEnterDistance = GetIniFloat(fullPath, section, "RemountEnterDistance", fb?.RemountEnterDistance ?? 5.0f),
                RemountMaxDistance = GetIniFloat(fullPath, section, "RemountMaxDistance", fb?.RemountMaxDistance ?? 30.0f),
                RemountMoveSpeed = GetIniFloat(fullPath, section, "RemountMoveSpeed", fb?.RemountMoveSpeed ?? 2.0f),
                RemountTaskCooldown = GetIniInt(fullPath, section, "RemountTaskCooldown", fb?.RemountTaskCooldown ?? 5000),
                RemountForceTimeout = GetIniInt(fullPath, section, "RemountForceTimeout", fb?.RemountForceTimeout ?? 10000),

                // Police
                AllowPoliceArrest = GetIniBool(fullPath, section, "AllowPoliceArrest", fb?.AllowPoliceArrest ?? true)
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
}
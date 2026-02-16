===============================================================================
  Street Riders v1.1.2 - Realistic NPC Wheelies
  for Grand Theft Auto V
===============================================================================

A GTA V mod that brings realistic physics-based wheelies to Los Santos. Spawn dynamic motorcycle gangs that patrol territories and ride in formation, or apply wheelies to natural traffic riders.

REQUIREMENTS:
  - GTA V (latest version)
  - ScriptHookV            
  - ScriptHookVDotNet v3   

INSTALLATION:
  1. Install ScriptHookV and ScriptHookVDotNet3 in your GTA V root folder.
  2. Copy the "scripts" folder from this archive into your GTA V root folder.
  3. Launch the game. You should see: "Street Riders v1.1.0: Loaded!"

  Your folder structure should look like this:

    Grand Theft Auto V/
      ScriptHookV.dll
      ScriptHookVDotNet3.dll
      scripts/
        StreetRiders.dll
        StreetRiders.ini

CONFIGURATION GUIDE:
  Edit StreetRiders.ini in any text editor. Press F10 in-game to hot-reload.

  [General]
    Enabled                  true       Enable/disable the entire mod
    Debug                    false      Enable notification debug messages
    ShowBlips                true       Show rider blips on minimap (independent of Debug)
    ShowOverhead             false      Show overhead text above riders (independent of Debug)
    DebugKey                 Insert     Toggle debug overlay in-game
    ReloadKey                F10        Hot-reload configuration

    MaxActiveGroups          1          Max simultaneous rider groups
    RidersPerGroupMin/Max    2 / 4      Riders per group
    SpawnDistanceMin/Max     120 / 150  Spawn distance from player (m)
    SpawnSpacing             5.0        Distance between riders when spawning (m)
    CleanupDistance          250        Remove riders beyond this distance
    SpawnIntervalMs          15000      Time between spawn attempts (ms)
    WheelieActiveDistance    200        Wheelie physics range from player (m)
    DestinationDistMin/Max   300 / 700  Destination range for GoAway mode (m)
    ApplyToNaturalRiders     false      Apply wheelie AI to traffic bikers
    NaturalRiderScanRadius   200        Scan radius for natural riders (m)

    DefaultActiveHourStart   0          Default active hours start (0-23)
    DefaultActiveHourEnd     23         Default active hours end (0-23)

    DefaultCruiseMode        Territory  Default cruise behavior (Territory/GoAway/Mixed)
    DefaultTerritoryChance   50         % chance to stay in territory (Mixed mode)
    DefaultTerritoryRadius   150.0      Max distance from spawn for Territory (m)

    DefaultBlipSprite        226        Blip icon (1=circle, 226=motorcycle, etc.)
    DefaultBlipColor         3          Blip color (0=white, 1=red, 2=green, 3=blue)
    DefaultBlipScale         0.7        Blip size (0.5=small, 0.7=normal, 1.0=large)

    WheelieExcludedModels    BMX, ...   Bikes that never do wheelies
    BikeOverrides            SANCHEZ2, BF400   Bikes with custom [Bike_XXX] configs
    Groups                   Families, Ballas, ...   Rider group sections

  [FollowPlayer]
    Enabled                  false      Enable follow player system
    ToggleKey                F7         Manual toggle key
    Mode                     Dynamic    Proximity = nearby / Dynamic = speed-based
    FollowRadius             100.0      Trigger distance (m)
    LoseRadius               200.0      Cancel distance (m)
    DynamicMinSpeed          12.0       Min player speed for Dynamic (m/s)
    DynamicTriggerMs         2000       Sustained speed time before trigger (ms)
    PlayerVehicleType        Any        Any / BikeOnly / TwoWheeler
    FollowMinDistance        8          Min distance for TASK_VEHICLE_FOLLOW (m)

  Dismount Behavior (in [FollowPlayer]):
    DismountBehavior         Freestyle  Wait / Continue / Freestyle
    IdleScenarios            WORLD_HUMAN_SMOKING, ...
    IdleDelayMs              8000       Delay before riders dismount (ms)
    ResumeDelayMs            3000       Delay before riders resume following (ms)
    IdleScenarioDistance     6.0        Distance to start idle scenarios (m)
    IdleFollowDistance       8.0        Distance to follow on foot (m)
    IdleMinStayDistance      6.0        Min distance riders stay near player (m)
    IdleScenarioWaitMs       4000       Delay before starting a scenario (ms)

  Bodyguard Mode (in [FollowPlayer]):
    BodyguardEnabled         false      WORK IN PROGRESS — DO NOT ENABLE
    BodyguardRadius          30.0       Detect attackers radius (m)
    BodyguardDurationMs      15000      Max engagement duration (ms)

  [Group_XXX] - Rider Groups:
    Enabled                  true       Enable this group
    Chance                   12         Spawn chance (relative, higher = more likely)
    PedModels                ...        Comma-separated ped model names
    BikeModels               ...        Comma-separated bike model names
    MixedVehicles            true       Each rider gets a different bike
    BikeColors               0, 2, 38   GTA V paint color IDs
    DoWheelie                true       Enable wheelies for this group
    Zones                    CHAMH, ... GTA V zone codes (empty = everywhere)
    ActiveHourStart/End      18 / 4     Active time window (wraps midnight)
    Weapons                  ...        (optional) Comma-separated weapon names
    CanFollowPlayer          true       Allow this group to follow the player
    CruiseMode               Territory  Override default cruise mode
    TerritoryChance          60         Override territory % (Mixed mode)
    TerritoryRadius          150.0      Override territory radius (m)
    BlipSprite               226        Override blip icon
    BlipColor                2          Override blip color
    BlipScale                0.7        Override blip size
    BlipName                 Families   Override blip label on minimap

  [DefaultWheelie] / [Bike_XXX] - Wheelie Tuning:
    LiftForce                0.18       Upward force (0.10=gentle, 0.30=strong)
    TorqueOffset             1.2        Lever arm in meters
    TargetPitch              40.0       Target wheelie angle (degrees)
    MaxPitch                 43.0       Emergency correction angle
    MinSpeed                 12.0       Min speed to attempt wheelie

  Driving (in [DefaultWheelie]):
    DriveSpeed               20.0       Cruise speed (m/s)
    MaxEntitySpeed           25.0       Max entity speed cap (m/s)
    DriveAbility             0.8        NPC driving ability (0.0-1.0)
    DriveAggressiveness      0.5        NPC aggressiveness (0.0-1.0)
    DrivingFlags             802877     Driving style bitmask (see INI comments)
                                        802877 = safe evasive (default)
                                        803644 = aggressive evasive (alternative)

  Ped Behavior (in [DefaultWheelie]):
    BlockNonTempEvents       true       Block ambient reactions
    FleeAttributes           0          Flee behavior flags
    CombatAttribute          17         Combat attribute ID
    CombatAttributeValue     false      Combat attribute toggle

  Remount (in [DefaultWheelie]):
    RemountEnterDistance      8.0       Distance to trigger TASK_ENTER_VEHICLE (m)
    RemountMaxDistance        100.0     Max distance before rider gives up (m)
    RemountMoveSpeed          3.0      Run speed toward bike (m/s)
    RemountTimeout            15000    Total remount timeout (ms)
    RemountTaskCooldown       3000     Cooldown between task reassignments (ms)
    RemountForceTimeout       10000    Force-warp timeout as last resort (ms, 0=off)

  Police (in [DefaultWheelie]):
    AllowPoliceArrest        false       Police can chase and arrest riders
    PolicePursuitRadius      100.0     Max lock-on distance (m)
    PolicePursuitMinDuration 60000     Min lock-on time per target (ms)

ZONE REFERENCE:
  CHAMH  = Chamberlain Hills    SANDY  = Sandy Shores
  DAVIS  = Davis                GRAPES = Grapeseed
  STRAW  = Strawberry           PALETO = Paleto Bay
  RANCHO = Rancho               KOREAT = Little Seoul
  LMESA  = La Mesa              VINE   = Vinewood
  CYPRE  = Cypress Flats        MIRR   = Mirror Park
  EBURO  = El Burro Heights     ROCKF  = Rockford Hills
  MURRI  = Murrieta Heights     DELPE  = Del Perro

TEST MODE & DEBUG:
  Enable with [TestMode] Enabled = true

  Insert   = Toggle debug overlay
  F10      = Hot-reload config
  NumPad1  = Force-spawn a group
  NumPad0  = Delete all riders

KNOWN ISSUES:
  - Weapons: equipping riders with weapons is optional and may cause them
    to enter combat, interrupting riding tasks. Use with caution.
  - Bodyguard mode: present in the INI but still under development.
    Do NOT enable BodyguardEnabled - will be released in a future update.
  - Remount: riders usually recover after falling, but edge cases exist.
    Idle riders stuck on their bike are auto-retasked after 5 seconds.

TROUBLESHOOTING:
  - Riders don't spawn? Check zone and active hours. Enable Debug.
  - Riders crash into cars? Adjust DrivingFlags in [DefaultWheelie].
    Try 803644 for aggressive evasion (off-road, wrong way overtaking).
  - Wheelies too strong? Lower LiftForce and/or TargetPitch.
  - Follow triggers too easily? Increase DynamicTriggerMs and DynamicMinSpeed.
  - Riders lose you too fast? Increase LoseRadius (try 300).
  - Combat interrupts riding? Remove Weapons or set BlockNonTempEvents = true.
  - Performance issues? Lower WheelieActiveDistance, reduce MaxActiveGroups.
  - Blips not showing? Check ShowBlips = true in [General].
  - Riders stay in wrong area? Check Zones and CruiseMode in group config.

USEFUL LINKS:
  Zone codes         https://docs.fivem.net/natives/?_0xCD90657D4C30E1CA
  Driving flags      https://docs.fivem.net/natives/?_0xDACE1BE37D88AF67
  Blip sprites       https://docs.fivem.net/docs/game-references/blips/
  Vehicle colors     https://wiki.rage.mp/index.php?title=Vehicle_Colors
  Motorcycle models  https://wiki.gtanet.work/index.php/Motorcycles
  Weapon list        https://wiki.rage.mp/index.php?title=Weapons

===============================================================================
  Street Riders v1.1.0 - Realistic NPC Wheelies
  for Grand Theft Auto V
===============================================================================

A GTA V mod that spawns motorcycle gangs who cruise through their turf and
pull realistic wheelies. Features physics-based wheelie mechanics,
zone-restricted gang territories, and a follow-the-player system.

FEATURES:
  - Realistic Wheelie Physics: smooth force-based system with angular
    velocity damping and gravity compensation.
  - Gang-Based Spawning: rider groups tied to specific zones and hours.
  - Follow Player: two trigger modes (Proximity / Dynamic speed-based).
  - Dismount Behaviors: Wait (idle near you), Continue (new leader),
    or Freestyle (ride loops and do wheelies).
  - Mixed Vehicles & Colors: different bike models and paint per group.
  - Weapons (optional): equip groups with specific weapons.
  - Police Interaction: cops can chase and arrest riders.
  - Per-Bike Tuning: override wheelie parameters per bike model.
  - Natural Rider Support: apply wheelie AI to NPC bikers in traffic.
  - Fully Configurable: every parameter in StreetRiders.ini. Hot-reload F10.
  - Test Mode: force-spawn, clean all, debug overlay with hotkeys.

REQUIREMENTS:
  - GTA V (latest version)
  - ScriptHookV            -> http://www.dev-c.com/gtav/scripthookv/
  - ScriptHookVDotNet v3   -> https://github.com/scripthookvdotnet/scripthookvdotnet/releases

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
    MaxActiveGroups          1          Max simultaneous rider groups
    RidersPerGroupMin/Max    2 / 4      Riders per group
    SpawnDistanceMin/Max     120 / 150   Spawn distance from player (m)
    CleanupDistance           250        Remove riders beyond this distance
    SpawnIntervalMs           15000      Time between spawn attempts (ms)
    WheelieActiveDistance     200        Wheelie physics range from player (m)
    ApplyToNaturalRiders      false      Apply wheelie AI to traffic bikers

  [FollowPlayer]
    Enabled                  true       Enable follow player system
    ToggleKey                F7         Manual toggle key
    Mode                     Proximity  Proximity = nearby / Dynamic = speed-based
    FollowRadius             60.0       Trigger distance (m)
    LoseRadius               200.0      Cancel distance (m)
    DynamicMinSpeed          12.0       Min player speed for Dynamic (m/s)
    DynamicTriggerMs         2000       Sustained speed time before trigger (ms)
    PlayerVehicleType        Any        Any / BikeOnly / TwoWheeler

  Dismount Behavior (in [FollowPlayer]):
    DismountBehavior         Wait       Wait / Continue / Freestyle
    IdleScenarios            WORLD_HUMAN_SMOKING, ...
    IdleScenarioDistance     8.0        Distance to start idle scenarios (m)
    IdleFollowDistance       15.0       Distance to follow on foot (m)
    IdleMinStayDistance      6.0        Min distance riders stay near player (m)
    IdleScenarioWaitMs       4000       Delay before starting a scenario (ms)

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

  [DefaultWheelie] / [Bike_XXX] - Wheelie Tuning:
    LiftForce                0.18       Upward force (0.10=gentle, 0.30=strong)
    TorqueOffset             1.2        Lever arm in meters
    TargetPitch              40.0       Target wheelie angle (degrees)
    MaxPitch                 43.0       Emergency correction angle
    MinSpeed                 12.0       Min speed to attempt wheelie

  Police (in [DefaultWheelie]):
    AllowPoliceArrest        false      Police can chase and arrest riders
    PolicePursuitRadius      100.0      Max lock-on distance (m)
    PolicePursuitMinDuration 15000      Min lock-on time per target (ms)

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

TROUBLESHOOTING:
  - Riders don't spawn? Check zone and active hours. Enable Debug.
  - Riders crash into cars? Adjust DrivingFlags in [DefaultWheelie].
  - Wheelies too strong? Lower LiftForce and/or TargetPitch.
  - Follow triggers too easily? Increase DynamicTriggerMs and DynamicMinSpeed.
  - Riders lose you too fast? Increase LoseRadius (try 300).
  - Combat interrupts riding? Remove Weapons or set BlockNonTempEvents = true.
  - Performance issues? Lower WheelieActiveDistance, reduce MaxActiveGroups.

USEFUL LINKS:
  Zone codes         https://docs.fivem.net/natives/?_0xCD90657D4C30E1CA
  Driving flags      https://docs.fivem.net/natives/?_0xDACE1BE37D88AF67
  Vehicle colors     https://wiki.rage.mp/index.php?title=Vehicle_Colors
  Motorcycle models  https://wiki.gtanet.work/index.php/Motorcycles
  Weapon list        https://wiki.rage.mp/index.php?title=Weapons

CREDITS:
  ScriptHookV        by Alexander Blade
  ScriptHookVDotNet  by crosire & community

LICENSE:
  This mod is provided as-is for personal use. You are free to modify it
  for your own enjoyment. If you redistribute or build upon it, please
  give credit.

===============================================================================

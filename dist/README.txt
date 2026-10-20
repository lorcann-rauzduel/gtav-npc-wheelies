===============================================================================
  Street Riders v2.0.1 - Realistic NPC Wheelies
  for Grand Theft Auto V
===============================================================================

Spawn motorcycle gangs that cruise their turf and pull realistic wheelies.
Physics-based system with stunt animations, fall prevention, and full
per-bike tuning.

-------------------------------------------------------------------------------
  WHAT'S NEW IN v2.0.0
-------------------------------------------------------------------------------

  BREAKING CHANGE — Config files have moved to a subfolder:
    scripts/StreetRiders/  (5 separate files, see Installation below)
    Old StreetRiders.ini and StreetRiders_Advanced.ini are no longer read.

  New features:
  - Stunt animations during sustained wheelies (showoff tricks, configurable)
  - PreventFall system: Always / WheelieOnly / Disabled + per-wheelie chance
  - Dual physics per bike: [Bike_X] (no protection) / [Bike_X_NoFall] (protected)
  - FormationDistance: configurable gap between followers and leader
  - LeaderGracePeriodMs: prevents rapid leader switching after brief unavailability
  - Police pursuit now restricted to Phase.Sustained only
  - Overtake flag added to default DrivingFlags
  - Wheelie phase detection: physics-driven Attempting → Sustained promotion
  - 3-ray fan obstacle detection during sustained wheelies

-------------------------------------------------------------------------------
  REQUIREMENTS
-------------------------------------------------------------------------------

  - GTA V (latest version)
  - ScriptHookV           http://www.dev-c.com/gtav/scripthookv/
  - ScriptHookVDotNet v3  https://github.com/scripthookvdotnet/scripthookvdotnet

-------------------------------------------------------------------------------
  INSTALLATION
-------------------------------------------------------------------------------

  1. Install ScriptHookV and ScriptHookVDotNet3 in your GTA V root folder.
  2. Copy StreetRiders.dll into Grand Theft Auto V/scripts/
  3. Copy the entire StreetRiders/ folder into Grand Theft Auto V/scripts/
  4. Launch the game. You should see: "Street Riders v2.0.1: Loaded!"

  Folder structure:

    Grand Theft Auto V/
      ScriptHookV.dll
      ScriptHookVDotNet3.dll
      scripts/
        StreetRiders.dll
        StreetRiders/
          config.ini      <- general settings, keybinds
          groups.ini      <- rider groups, zones, spawn hours
          vehicles.ini    <- wheelie physics, per-bike tuning
          behavior.ini    <- driving, stunts, police, follow-player
          advanced.ini    <- spawn distances, remount, low-level

  MIGRATING FROM v1.x:
    Your old StreetRiders.ini groups ([Group_Families], etc.) go into groups.ini.
    Wheelie physics ([DefaultWheelie], [Bike_X]) go into vehicles.ini.
    Everything else is split across config.ini, behavior.ini, and advanced.ini.
    The new files ship with sensible defaults — start fresh and copy your groups.

-------------------------------------------------------------------------------
  CONFIGURATION — QUICK OVERVIEW
-------------------------------------------------------------------------------

  All files are in scripts/StreetRiders/. Reload any file in-game with F10.

  config.ini — things you'll actually change:
    Enabled              true      Enable/disable the mod
    Debug                false     Enable debug notifications
    ShowBlips            true      Show rider blips on minimap
    ShowOverhead         false     Show overhead debug text above riders
    ReloadKey            F10       Hot-reload all config files
    DebugKey             Insert    Toggle debug overlay
    MaxActiveGroups      1         Max simultaneous rider groups
    RidersPerGroupMin    2         Min riders per group
    RidersPerGroupMax    4         Max riders per group

  groups.ini — define rider gangs:
    Groups = Families, Ballas, Vagos, ...   (active group list)
    DefaultActiveHourStart/End              Default hours for all groups
    DefaultCruiseMode  Territory/GoAway/Mixed
    [Group_Families], [Group_Ballas], ...   One section per group

    Per-group settings:
      PedModels, BikeModels, BikeColors, MixedVehicles
      DoWheelie, DoWheelieStunts
      Zones, ActiveHourStart/End
      Weapons, CanFollowPlayer
      RelationshipGroup, PlayerRelationship
      CruiseMode, TerritoryChance, TerritoryRadius
      BlipColor, BlipSprite, BlipScale, BlipName

  vehicles.ini — wheelie physics:
    WheelieExcludedModels    Bikes that never do wheelies
    BikeOverrides            Bikes with per-model tuning sections

    [DefaultWheelie]         Base physics (when fall prevention OFF)
      LiftForce = 0.18       Upward force. Too low = won't lift. Too high = flips.
      TorqueOffset = 1.2     Lever arm (higher = more rotation torque)
      TargetPitch = 40.0     Target angle in degrees
      MaxPitch = 43.0        Correction kicks in above this angle
      MinSpeed = 12.0        Min speed to attempt wheelie
      AttemptDurationMin/Max, SustainDurationMin/Max, AttemptDelayMin/Max

    [DefaultWheelie_NoFall]  Physics when fall prevention IS active (higher values safe)
    [Bike_SANCHEZ2]          Per-bike override (no protection)
    [Bike_SANCHEZ2_NoFall]   Per-bike override (with fall prevention)

    Migration note: your existing [Bike_X] configs map to the "no protection" variant.
    Add [Bike_X_NoFall] with higher values to take advantage of fall prevention.

  behavior.ini — how riders act:
    PreventFall      Always / WheelieOnly / Disabled
                     Always = riders never fall off
                     WheelieOnly = only protected during active wheelie
                     Disabled = vanilla GTA physics
    PreventFallChance  0-100 (WheelieOnly only): % chance protection triggers per wheelie
                       50 = occasional natural bails + higher angles when protected

    DriveSpeed, MaxEntitySpeed, DriveAbility, DriveAggressiveness
    DrivingFlags     Driving style bitmask (https://vespura.com/fivem/drivingstyle/)
    ApplyToNaturalRiders / NaturalRiderScanRadius
    BlockNonTempEvents, FleeAttributes, CombatAttribute

    StuntCooldown    Delay between stunt animations in Sustained (ms)
    StuntAnimations  dict:clip, dict:clip, ...  (default: rcmextreme2atv idle tricks)

    AllowPoliceArrest        Police chase riders in Phase.Sustained only
    PolicePursuitRadius      Lock-on distance (m)
    PolicePursuitMinDuration Min time cops stay on a target (ms)

    [FollowPlayer]   Full follow-player system config

  advanced.ini — rarely changed:
    SpawnDistanceMin/Max, CleanupDistance, SpawnIntervalMs, SpawnSpacing
    WheelieActiveDistance    Wheelie physics range from player (m)
    DestinationDistanceMin/Max
    FormationDistance        Gap between followers and leader (default 50m)
    LeaderGracePeriodMs      Grace period before leader replacement (default 4000ms)
    PulseIntervalMs, RaycastDistance, MaxSteeringAngle, BrakeThreshold
    RemountEnterDistance, RemountMaxDistance, RemountMoveSpeed
    RemountTimeout, RemountTaskCooldown, RemountForceTimeout

-------------------------------------------------------------------------------
  WHEELIE TUNING TIPS
-------------------------------------------------------------------------------

  Bike doesn't lift?       Raise LiftForce
  Lifts too hard?          Lower LiftForce
  Angle too low?           Raise TargetPitch (also needs enough LiftForce to reach it)
  Bike flips over?         Lower MaxPitch
  Still bouncy?            Lower LiftForce + raise TorqueOffset

  With PreventFall = Always or WheelieOnly:
    Use [Bike_X_NoFall] with higher TargetPitch/LiftForce for aggressive wheelies.
    [Bike_X] keeps conservative values for when the rider can fall.

  With PreventFall = WheelieOnly + PreventFallChance = 50:
    Half the time riders push aggressive angles (NoFall config).
    Half the time they ride conservatively (base config) and may bail.
    Best balance between spectacle and realism.

-------------------------------------------------------------------------------
  ZONE REFERENCE
-------------------------------------------------------------------------------

  CHAMH  = Chamberlain Hills    SANDY  = Sandy Shores
  DAVIS  = Davis                GRAPES = Grapeseed
  STRAW  = Strawberry           PALETO = Paleto Bay
  RANCHO = Rancho               KOREAT = Little Seoul
  LMESA  = La Mesa              VINE   = Vinewood
  CYPRE  = Cypress Flats        MIRR   = Mirror Park
  EBURO  = El Burro Heights     ROCKF  = Rockford Hills
  MURRI  = Murrieta Heights     DELPE  = Del Perro

-------------------------------------------------------------------------------
  DEBUG & TEST MODE
-------------------------------------------------------------------------------

  Enable TestMode in config.ini. Reload config with F10 at any time.

  Insert    Toggle debug notifications
  F10       Hot-reload all config files (no restart needed)
  NumPad1   Force-spawn a group near the player
  NumPad0   Delete all active riders

  Overhead debug (ShowOverhead = true):
    [S:BF400]  Sustained 2/3    <- rider type, bike, phase, attempts
    Pitch:42.1  Speed:18         <- current pitch angle and speed
    PreventFall:ON  LIFT:0.18    <- fall prevention state + force applied

-------------------------------------------------------------------------------
  TROUBLESHOOTING
-------------------------------------------------------------------------------

  Riders don't spawn?
    Check Zones and ActiveHourStart/End in groups.ini. Enable Debug.

  Wheelies too weak / too strong?
    Adjust LiftForce and TargetPitch in vehicles.ini.
    TargetPitch doesn't guarantee the angle — LiftForce must be high enough.

  Riders crash into cars?
    Adjust DrivingFlags in behavior.ini.
    Use https://vespura.com/fivem/drivingstyle/ to calculate flags.

  Riders keep overtaking each other?
    Increase FormationDistance in advanced.ini (default 50m, try 60-80m).

  Leader switches too often?
    Increase LeaderGracePeriodMs in advanced.ini (default 4000ms).

  Follow mode triggers too easily?
    Increase DynamicTriggerMs and DynamicMinSpeed in behavior.ini.

  Riders lose you too fast?
    Increase LoseRadius in behavior.ini [FollowPlayer] (try 300).

  Stunt animations cause riders to veer off?
    Reduce StuntCooldown or increase RaycastDistance in advanced.ini.
    This is a GTA engine limitation — see the note in behavior.ini.

  Performance issues?
    Lower WheelieActiveDistance and MaxActiveGroups.

-------------------------------------------------------------------------------
  USEFUL LINKS
-------------------------------------------------------------------------------

  Zone codes          https://docs.fivem.net/natives/?_0xCD90657D4C30E1CA
  Driving flags       https://vespura.com/fivem/drivingstyle/
  Blip sprites        https://docs.fivem.net/docs/game-references/blips/
  Blip colors         https://docs.fivem.net/docs/game-references/blips/
  Vehicle colors      https://wiki.rage.mp/index.php?title=Vehicle_Colors
  Motorcycle models   https://wiki.gtanet.work/index.php/Motorcycles
  Weapon list         https://wiki.rage.mp/index.php?title=Weapons
  Keys enum           https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.keys

-------------------------------------------------------------------------------
  CREDITS
-------------------------------------------------------------------------------

  ScriptHookV         Alexander Blade   http://www.dev-c.com/gtav/scripthookv/
  ScriptHookVDotNet   crosire & community

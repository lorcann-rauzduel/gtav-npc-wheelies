# Street Riders v2.0.1 — Realistic NPC Wheelies

A GTA V mod that spawns motorcycle gangs who cruise their turf and pull realistic wheelies. Physics-based system with stunt animations, fall prevention tuning, and full per-bike configuration.

> ⚠ **v2.0.0 breaking change** — Config files have moved to `scripts/StreetRiders/` (5 separate files). Old `StreetRiders.ini` and `StreetRiders_Advanced.ini` are no longer read. See [Migration](#migrating-from-v1x) below.

---

## Features

- **Physics-Based Wheelies** — Smooth continuous force with angular velocity damping. Bikes lift naturally, hold a stable angle, and settle under gravity.
- **Stunt Animations** — Riders in sustained wheelies randomly play showoff tricks. Configurable animation list, cooldown, and upper-body/full-body mix.
- **Fall Prevention System** — Three modes: `Always` (never falls), `WheelieOnly` (protected only during wheelie), `Disabled` (vanilla). WheelieOnly supports a per-wheelie chance for realistic variation.
- **Dual Physics Per Bike** — `[Bike_X]` for conservative physics (rider can fall), `[Bike_X_NoFall]` for aggressive physics (fall prevention active). Backwards compatible with old configs.
- **Gang-Based Spawning** — Groups tied to GTA V zones and active hours. Families in Chamberlain, Ballas in Davis, etc.
- **Follow Player** — Riders follow your vehicle with Proximity or Dynamic (speed-based) trigger modes.
- **Dismount Behaviors** — `Wait` (idle near you), `Continue` (pick new leader), `Freestyle` (ride loops and wheelies near you).
- **Formation Riding** — Configurable distance between followers and leader. Leader grace period prevents rapid switching.
- **Police Interaction** — Cops chase riders doing wheelies (Sustained phase only). Persistent pursuit tracking.
- **Per-Bike Tuning** — Override any physics parameter for individual models with `[Bike_X]` sections.
- **Natural Rider Support** — Optionally apply wheelie AI to NPC bikers already in traffic.
- **5-File Config System** — Settings split by concern. Hot-reload any file with F10.

---

## Requirements

- GTA V (latest version)
- [ScriptHookV](http://www.dev-c.com/gtav/scripthookv/) by Alexander Blade
- [ScriptHookVDotNet v3](https://github.com/scripthookvdotnet/scripthookvdotnet/releases) by crosire

---

## Installation

1. Install ScriptHookV and ScriptHookVDotNet3 in your GTA V root folder.
2. Copy `StreetRiders.dll` into `Grand Theft Auto V/scripts/`.
3. Copy the entire `StreetRiders/` folder into `Grand Theft Auto V/scripts/`.
4. Launch the game — you should see: *"Street Riders v2.0.1: Loaded!"*

```
Grand Theft Auto V/
  ScriptHookV.dll
  ScriptHookVDotNet3.dll
  scripts/
    StreetRiders.dll
    StreetRiders/
      config.ini      ← general settings, keybinds
      groups.ini      ← rider groups, zones, spawn hours
      vehicles.ini    ← wheelie physics, per-bike tuning
      behavior.ini    ← driving style, stunts, police, follow-player
      advanced.ini    ← spawn distances, remount, low-level tweaks
```

### Migrating from v1.x

The config files moved but the settings are the same. Quick mapping:

| Old location | New file |
|---|---|
| `StreetRiders.ini` [General] (spawn, display, keys) | `config.ini` |
| `StreetRiders.ini` [Group_XXX] sections | `groups.ini` |
| `StreetRiders_Advanced.ini` [DefaultWheelie] physics | `vehicles.ini` |
| `StreetRiders_Advanced.ini` driving, police, PED | `behavior.ini` |
| `StreetRiders_Advanced.ini` remount, safety | `advanced.ini` |

Easiest path: start with the new default files and copy your `[Group_XXX]` sections into `groups.ini`.

---

## Configuration

All files live in `scripts/StreetRiders/`. Press **F10** in-game to reload everything.

### config.ini — General

The only file most users need to touch for basic setup.

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `true` | Enable/disable the mod |
| `Debug` | `false` | Enable debug notifications |
| `ShowBlips` | `true` | Rider blips on minimap |
| `ShowOverhead` | `false` | Overhead debug text above riders |
| `ReloadKey` | `F10` | Hot-reload all config files |
| `DebugKey` | `Insert` | Toggle debug overlay |
| `MaxActiveGroups` | `1` | Max simultaneous rider groups |
| `RidersPerGroupMin/Max` | `2` / `4` | Riders per group |

### groups.ini — Rider Groups

Define gangs and their behavior. Add a name to `Groups =`, then create a `[Group_NAME]` section.

| Setting | Example | Description |
|---|---|---|
| `PedModels` | `g_m_y_famca_01, ...` | Ped model names |
| `BikeModels` | `BF400, SANCHEZ2` | Bike model names |
| `MixedVehicles` | `true` | Each rider gets a different bike |
| `BikeColors` | `0, 2, 38` | Paint color IDs |
| `DoWheelie` | `true` | Enable wheelies |
| `DoWheelieStunts` | `true` | Enable stunt animations during wheelies |
| `Zones` | `CHAMH, STRAW` | Zone codes (empty = everywhere) |
| `ActiveHourStart/End` | `18` / `4` | Active hours (wraps midnight) |
| `Weapons` | `WEAPON_PISTOL` | *(optional)* May cause combat interruptions |
| `CanFollowPlayer` | `false` | Allow this group to follow the player |
| `RelationshipGroup` | `AMBIENT_GANG_FAMILY` | GTA V relationship group |
| `PlayerRelationship` | `Neutral` | `Companion` / `Respect` / `Neutral` / `Hate` / ... |
| `CruiseMode` | `Territory` | `Territory` / `GoAway` / `Mixed` |

### vehicles.ini — Wheelie Physics

#### How it works

Force applied every frame, dampened by angular velocity. The bike can't overshoot.

```
0 ─── fadeStart (50% of Target) ─── TargetPitch ─── MaxPitch
      full force                     force fades     coast zone     correction
```

**Key insight**: `TargetPitch` controls where the force starts fading, not the guaranteed angle. `LiftForce` must be high enough to physically push the bike to that angle against gravity.

#### [DefaultWheelie] — base physics (no fall prevention)

| Setting | Default | Description |
|---|---|---|
| `LiftForce` | `0.18` | Upward force — too low: won't lift, too high: flips |
| `TorqueOffset` | `1.2` | Lever arm in meters |
| `TargetPitch` | `40.0` | Target wheelie angle (degrees) |
| `MaxPitch` | `43.0` | Correction kicks in above this |
| `MinSpeed` | `12.0` | Min speed to attempt a wheelie (m/s) |

#### [DefaultWheelie_NoFall] — physics when fall prevention is active

Same keys as above, higher values are safe since the rider won't bail.

#### Per-bike overrides

```ini
[Bike_BF400]          ; conservative — rider can fall
LiftForce = 0.15
TargetPitch = 45.0
MaxPitch = 50.0

[Bike_BF400_NoFall]   ; aggressive — fall prevention active
LiftForce = 0.28
TargetPitch = 60.0
MaxPitch = 65.0
```

**Migration**: your existing `[Bike_X]` sections are used as the no-protection config. Add `[Bike_X_NoFall]` to take advantage of fall prevention.

### behavior.ini — Riding Behavior

#### Fall Prevention

| Setting | Default | Description |
|---|---|---|
| `PreventFall` | `Always` | `Always` / `WheelieOnly` / `Disabled` |
| `PreventFallChance` | `100` | 0–100, WheelieOnly only — % chance protection triggers per wheelie |

**Recommended setup for realism**: `PreventFall = WheelieOnly` + `PreventFallChance = 50`. Half the time riders push aggressive angles (`_NoFall` config), the other half they ride conservatively and may bail naturally.

#### Driving

| Setting | Default | Description |
|---|---|---|
| `DriveSpeed` | `20.0` | Cruise speed (m/s) |
| `DrivingFlags` | `868413` | Bitmask — [calculator](https://vespura.com/fivem/drivingstyle/) |
| `ApplyToNaturalRiders` | `false` | Apply wheelie AI to traffic bikers |

#### Stunts

| Setting | Default | Description |
|---|---|---|
| `StuntCooldown` | `8000` | Delay between animations in Sustained (ms) |
| `StuntAnimations` | `rcmextreme2atv:idle_b, ...` | `dict:clip` pairs, comma-separated |

> ⚠ Full-body animations (75% of triggers) replace the driving task temporarily. Riders may collide with obstacles during this window. A forward raycast tries to cancel stunts early, but GTA collision geometry is complex — this is a known engine limitation. Increase `RaycastDistance` or reduce `StuntCooldown` to lower risk.

#### Police

| Setting | Default | Description |
|---|---|---|
| `AllowPoliceArrest` | `true` | Police chase riders **in Sustained phase only** |
| `PolicePursuitRadius` | `100.0` | Lock-on distance (m) |
| `PolicePursuitMinDuration` | `60000` | Min lock-on time per target (ms) |

### advanced.ini — Low-Level Tweaks

Most users never need this file.

| Setting | Default | Description |
|---|---|---|
| `FormationDistance` | `50` | Gap followers maintain behind leader (m) — increase to reduce overtaking |
| `LeaderGracePeriodMs` | `4000` | Grace period before promoting a new leader (ms) |
| `WheelieActiveDistance` | `200` | Wheelie physics range from player (m) — reduce for performance |
| `RaycastDistance` | `10.0` | Forward obstacle detection before stunts (m) |
| `RemountTimeout` | `15000` | Max time a fallen rider tries to remount (ms) |

---

## Tuning Tips

| Problem | Fix |
|---|---|
| Bike doesn't lift | Raise `LiftForce` |
| Lifts too hard | Lower `LiftForce` |
| Angle too low despite high TargetPitch | `LiftForce` must be high enough — TargetPitch only controls the fade zone |
| Bike flips over | Lower `MaxPitch` |
| Still bouncy/oscillating | Lower `LiftForce` + raise `TorqueOffset` |
| Riders keep overtaking each other | Increase `FormationDistance` |
| Leader switches too often | Increase `LeaderGracePeriodMs` |
| Stunts cause direction changes | Increase `RaycastDistance`, reduce `StuntCooldown` |

---

## Debug Overlay

Enable with `ShowOverhead = true` in `config.ini`.

```
[S:BF400]  Sustained 2/3
Pitch:42.1  Speed:18
PreventFall:ON  LIFT:0.18
```

Line 1: rider type (S=spawned, N=natural), bike model, current phase, attempt count  
Line 2: current pitch angle and speed  
Line 3: fall prevention state + active force or skip reason (BRAKE, STEER, OBSTACLE…)

---

## Test Mode & Keybinds

Enable `[TestMode] Enabled = true` in `config.ini`.

| Key | Action |
|---|---|
| `F10` | Hot-reload all config files |
| `Insert` | Toggle debug notifications |
| `NumPad1` | Force-spawn a group near the player |
| `NumPad0` | Despawn all active riders |
| `F7` | Toggle follow-player mode (configurable in `behavior.ini`) |

---

## Zone Reference

| Code | Zone | Code | Zone |
|---|---|---|---|
| `CHAMH` | Chamberlain Hills | `SANDY` | Sandy Shores |
| `DAVIS` | Davis | `GRAPES` | Grapeseed |
| `STRAW` | Strawberry | `PALETO` | Paleto Bay |
| `RANCHO` | Rancho | `KOREAT` | Little Seoul |
| `LMESA` | La Mesa | `VINE` | Vinewood |
| `CYPRE` | Cypress Flats | `MIRR` | Mirror Park |
| `EBURO` | El Burro Heights | `ROCKF` | Rockford Hills |
| `MURRI` | Murrieta Heights | `DELPE` | Del Perro |

---

## Known Issues

- **Weapons & combat**: Armed riders may enter combat with NPCs, interrupting riding tasks. Use with caution.
- **Bodyguard mode**: Present in the config but still under development. Do not enable — riders may attack each other.
- **Stunt collisions**: Full-body animations temporarily disable the driving task. Riders may hit obstacles during this window. This is a GTA engine limitation.

---

## Troubleshooting

- **Riders don't spawn?** Check `Zones` and `ActiveHourStart/End` in `groups.ini`. Enable `Debug`.
- **Riders crash into cars?** Adjust `DrivingFlags` in `behavior.ini` — use the [calculator](https://vespura.com/fivem/drivingstyle/).
- **Wheelies too weak?** `LiftForce` needs to be high enough to reach `TargetPitch` against gravity.
- **Follow mode triggers too easily?** Increase `DynamicTriggerMs` and `DynamicMinSpeed` in `behavior.ini`.
- **Performance issues?** Lower `WheelieActiveDistance`, reduce `MaxActiveGroups`.

---

## Useful Links

| Resource | URL |
|---|---|
| Zone codes | https://docs.fivem.net/natives/?_0xCD90657D4C30E1CA |
| Driving flags calculator | https://vespura.com/fivem/drivingstyle/ |
| Blip sprites | https://docs.fivem.net/docs/game-references/blips/ |
| Vehicle colors | https://wiki.rage.mp/index.php?title=Vehicle_Colors |
| Motorcycle models | https://wiki.gtanet.work/index.php/Motorcycles |
| Keys enum | https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.keys |
| Weapon list | https://wiki.rage.mp/index.php?title=Weapons |

---

## License

**Creative Commons Attribution-NonCommercial-ShareAlike 4.0 (CC BY-NC-SA 4.0)**

Free to share and adapt for personal, non-commercial use with credit and same license.

---

## Credits

- [ScriptHookV](http://www.dev-c.com/gtav/scripthookv/) — Alexander Blade
- [ScriptHookVDotNet](https://github.com/scripthookvdotnet/scripthookvdotnet) — crosire & community

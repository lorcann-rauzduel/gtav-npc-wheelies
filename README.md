# Street Riders: Realistic NPC Wheelies

A GTA V mod that spawns motorcycle gangs who cruise through their turf and pull realistic wheelies. Features physics-based wheelie mechanics, zone-restricted gang territories, and a follow-the-player system.

## Features

- **Realistic Wheelie Physics** — Smooth, continuous force-based system with angular velocity damping and gravity compensation. Bikes lift naturally and hold a stable angle.
- **Gang-Based Spawning** — Define rider groups tied to specific GTA V zones and active hours. Families ride through Chamberlain at night, Ballas cruise Davis, etc.
- **Follow Player** — Riders can follow your vehicle with two trigger modes: Proximity (distance) or Dynamic (speed-based overtaking).
- **Dismount Behaviors** — When you get off your vehicle, riders can Wait (idle near you), Continue (pick a new leader), or Freestyle (ride loops and do wheelies).
- **Mixed Vehicles & Colors** — Each rider can ride a different bike model with configurable paint colors per group.
- **Weapons** *(optional)* — Equip each group with specific weapons. See *Known Issues* below.
- **Police Interaction** — Cops can chase and arrest riders with persistent pursuit tracking.
- **Per-Bike Tuning** — Override wheelie parameters for individual bike models.
- **Natural Rider Support** — Optionally apply wheelie AI to NPC bikers already in traffic.
- **Fully Configurable** — Every parameter exposed in `StreetRiders.ini`. Hot-reload with F10.
- **Test Mode** — Force-spawn groups, clean all riders, advance game time with hotkeys.

## Requirements

- GTA V (latest version)
- [ScriptHookV](http://www.dev-c.com/gtav/scripthookv/)
- [ScriptHookVDotNet v3](https://github.com/scripthookvdotnet/scripthookvdotnet/releases)

## Installation

1. Install ScriptHookV and ScriptHookVDotNet3 in your GTA V root folder.
2. Copy `StreetRiders.dll` into `Grand Theft Auto V/scripts/`.
3. Copy `StreetRiders.ini` into `Grand Theft Auto V/scripts/`.
4. Launch the game. You should see: *"Street Riders v1.2.1: Loaded!"*

```
Grand Theft Auto V/
  ScriptHookV.dll
  ScriptHookVDotNet3.dll
  scripts/
    StreetRiders.dll
    StreetRiders.ini
```

## Configuration Guide

Edit `StreetRiders.ini` in any text editor. Press **F10** in-game to hot-reload changes.

### General Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Enable/disable the entire mod |
| `MaxActiveGroups` | `1` | Max simultaneous rider groups in the world |
| `RidersPerGroupMin/Max` | `2` / `4` | Number of riders per group |
| `SpawnDistanceMin/Max` | `120` / `150` | Spawn distance from player (meters) |
| `CleanupDistance` | `250` | Remove riders beyond this distance |
| `SpawnIntervalMs` | `15000` | Time between spawn attempts (ms) |
| `WheelieActiveDistance` | `200` | Wheelie physics range from player (m) |
| `ApplyToNaturalRiders` | `false` | Apply wheelie AI to traffic bikers |

### Follow Player (`[FollowPlayer]`)

Riders with `CanFollowPlayer = true` in their group config can follow your vehicle.

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Enable follow player system |
| `ToggleKey` | `F7` | Manual toggle key |
| `Mode` | `Proximity` | `Proximity` = follow when nearby / `Dynamic` = speed-based |
| `FollowRadius` | `60.0` | Trigger distance (m) |
| `LoseRadius` | `200.0` | Cancel distance (m) — keep high at vehicle speeds |
| `DynamicMinSpeed` | `12.0` | Min player speed for Dynamic mode (m/s) |
| `DynamicTriggerMs` | `2000` | How long player must maintain speed before trigger (ms) |
| `PlayerVehicleType` | `Any` | `Any`, `BikeOnly`, or `TwoWheeler` |

### Dismount Behavior

What happens when you get off your vehicle while riders are following.

| Setting | Default | Description |
|---------|---------|-------------|
| `DismountBehavior` | `Wait` | `Wait` / `Continue` / `Freestyle` |
| `IdleScenarios` | `WORLD_HUMAN_SMOKING, ...` | Scenarios riders play while waiting |
| `IdleScenarioDistance` | `8.0` | Distance to start idle scenarios (m) |
| `IdleFollowDistance` | `15.0` | Distance to follow on foot (m) |
| `IdleMinStayDistance` | `6.0` | Min distance riders stay near player (m) |
| `IdleScenarioWaitMs` | `4000` | Delay before starting a scenario (ms) |

### Rider Groups (`[Group_XXX]`)

Each group defines a gang with specific bikes, weapons, zones, and active hours.

| Setting | Example | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Enable this group |
| `Chance` | `12` | Spawn chance (relative, higher = more likely — no max, typically 1-100) |
| `PedModels` | `g_m_y_famca_01, ...` | Comma-separated ped model names |
| `BikeModels` | `BF400, SANCHEZ2` | Comma-separated bike model names |
| `MixedVehicles` | `true` | Each rider gets a different bike from the list |
| `BikeColors` | `0, 2, 38` | GTA V paint color IDs ([full list](https://wiki.rage.mp/index.php?title=Vehicle_Colors)) |
| `DoWheelie` | `true` | Enable wheelies for this group |
| `Zones` | `CHAMH, STRAW` | GTA V zone codes (empty = everywhere) |
| `ActiveHourStart/End` | `18` / `4` | Active time window (wraps around midnight) |
| `Weapons` | `WEAPON_PISTOL, ...` | *(optional)* Comma-separated weapon names — see Known Issues |
| `CanFollowPlayer` | `true` | Allow this group to follow the player |

### Wheelie Tuning (`[DefaultWheelie]` / `[Bike_XXX]`)

The physics system uses continuous force with angular velocity damping.

| Setting | Default | Description |
|---------|---------|-------------|
| `LiftForce` | `0.18` | Upward force per frame (`0.10`=gentle, `0.30`=strong) |
| `TorqueOffset` | `1.2` | Lever arm in meters (higher = more rotation) |
| `TargetPitch` | `40.0` | Target wheelie angle (degrees) |
| `MaxPitch` | `43.0` | Emergency correction angle |
| `MinSpeed` | `12.0` | Min speed to attempt wheelie |

Override per bike model with `[Bike_SANCHEZ]`, `[Bike_BF400]`, etc.

### Police

| Setting | Default | Description |
|---------|---------|-------------|
| `AllowPoliceArrest` | `false` | Police can chase and arrest riders |
| `PolicePursuitRadius` | `100.0` | Max lock-on distance (m) |
| `PolicePursuitMinDuration` | `15000` | Min lock-on time per target (ms) |

### Zone Reference

Common GTA V zone codes ([full list — `GET_NAME_OF_ZONE`](https://docs.fivem.net/natives/?_0xCD90657D4C30E1CA)):

| Code | Zone | Code | Zone |
|------|------|------|------|
| `CHAMH` | Chamberlain Hills | `SANDY` | Sandy Shores |
| `DAVIS` | Davis | `GRAPES` | Grapeseed |
| `STRAW` | Strawberry | `PALETO` | Paleto Bay |
| `RANCHO` | Rancho | `KOREAT` | Little Seoul |
| `LMESA` | La Mesa | `VINE` | Vinewood |
| `CYPRE` | Cypress Flats | `MIRR` | Mirror Park |
| `EBURO` | El Burro Heights | `ROCKF` | Rockford Hills |
| `MURRI` | Murrieta Heights | `DELPE` | Del Perro |

## Test Mode & Debug

Enable with `[TestMode] Enabled = true`.

| Key | Action |
|-----|--------|
| `Insert` | Toggle debug overlay |
| `F10` | Hot-reload config |
| `NumPad1` | Force-spawn a group |
| `NumPad0` | Delete all riders |

The debug overlay shows real-time rider state: phase, pitch angle, speed, and force applied.

## Known Issues

- **Weapons & combat**: Equipping riders with weapons (`Weapons` setting) is optional and may cause unexpected behavior. Armed riders can enter combat with other NPCs, which may interrupt their riding tasks. This feature is actively being improved.
- **Bodyguard mode**: A bodyguard system exists in the INI (`BodyguardEnabled`) but is still under development. **Do not enable it** — riders may attack each other or the player. This will be properly released in a future update.
- **Remount after falling**: Riders usually recover and remount, but in some edge cases they may get stuck.

## Troubleshooting

- **Riders don't spawn?** Check zone and active hours. Enable Debug for details.
- **Riders crash into cars?** Adjust `DrivingFlags` in `[DefaultWheelie]` — see [driving flags reference](https://docs.fivem.net/natives/?_0xDACE1BE37D88AF67).
- **Wheelies too strong?** Lower `LiftForce` and/or `TargetPitch`.
- **Follow mode triggers too easily?** Increase `DynamicTriggerMs` and `DynamicMinSpeed`.
- **Riders lose you too fast?** Increase `LoseRadius` (try 300).
- **Bike doesn't move after taking it from a rider?** This was fixed by releasing handbrake/ownership when the player takes a tracked bike. Update to v1.2.1+.
- **Riders enter combat and stop riding?** Remove `Weapons` from the group config, or set `BlockNonTempEvents = true`.
- **Performance issues?** Lower `WheelieActiveDistance`, reduce `MaxActiveGroups`.

## Credits

- [ScriptHookV](http://www.dev-c.com/gtav/scripthookv/) by Alexander Blade
- [ScriptHookVDotNet](https://github.com/scripthookvdotnet/scripthookvdotnet) by crosire & community

## Useful Links

| Resource | URL |
|----------|-----|
| Zone codes | https://docs.fivem.net/natives/?_0xCD90657D4C30E1CA |
| Driving flags | https://docs.fivem.net/natives/?_0xDACE1BE37D88AF67 |
| Vehicle colors | https://wiki.rage.mp/index.php?title=Vehicle_Colors |
| Motorcycle models | https://wiki.gtanet.work/index.php/Motorcycles |
| List of scenarios (for peds) | https://gtaforums.com/topic/796181-list-of-scenarios-for-peds |
| Weapon list | https://wiki.rage.mp/index.php?title=Weapons |

## License

This mod is provided as-is for personal use. You are free to modify it for your own enjoyment. If you redistribute or build upon it, please give credit.

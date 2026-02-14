# Changelog

All notable changes to Street Riders are documented here.

## [1.1.0] - 2026-02-11

### Added
- **Follow Player mode**: Riders with `CanFollowPlayer = true` can follow the player's vehicle
  - Two trigger modes: `Proximity` (distance-based) and `Dynamic` (speed-based)
  - Configurable follow/lose radius with `FollowRadius` and `LoseRadius`
  - Decision cooldown system prevents rapid state flickering (`DynamicTriggerMs`)
  - Manual toggle with configurable key (`ToggleKey`)
- **Dismount behaviors** when the player gets off their vehicle:
  - `Wait` — riders dismount, walk to the player, and play idle scenarios
  - `Continue` — a random rider becomes the new leader
  - `Freestyle` — riders stay on bikes, ride loops and do wheelies near the player
- **Bodyguard mode** (experimental): riders protect the player when attacked
  - Uses relationship locking to prevent friendly fire
  - Configurable radius and engagement duration
  - Auto-disengage when player remounts
- **Mixed vehicles**: each rider in a group can ride a different bike from `BikeModels`
- **Bike colors**: configurable paint colors per group with `BikeColors`
- **Weapons**: riders can be armed with configurable weapons per group
- **Per-group follow control**: `CanFollowPlayer` setting per group
- **Police pursuit persistence**: cops maintain their target for a configurable duration and radius
- **Relationship locking**: prevents friendly fire between same-group riders (LockPedForScript pattern)

### Improved
- Remount logic: riders no longer get stuck in remount loops after falling
- Dynamic follow mode: sustained speed check prevents accidental triggers
- Follow/lose conditions: configurable large radius prevents premature abandon
- Idle scenario timing: riders wait before starting scenarios, follow on foot when player moves
- Police pursuit: dedicated tracking system prevents target-switching with multiple riders

### Fixed
- Riders stuck idle after falling off their bike
- Police not pursuing riders despite option enabled
- Police stuck with sirens after suspect dies
- Riders attacking each other during bodyguard combat
- Riders attacking the player during bodyguard mode
- Riders dismounting too far from the player
- Riders not re-engaging follow mode after returning to autonomous
- Task spam causing ped animation glitches during remount

### Renamed
- Project renamed from "BikeLife" to "Street Riders: Realistic NPC Wheelies"
- DLL: `BikeLife.dll` → `StreetRiders.dll`
- INI: `BikeLife.ini` → `StreetRiders.ini`

## [1.0.1] - 2025

### Fixed
- Minor bug fixes and stability improvements

## [1.0.0] - 2025

### Added
- Initial release
- Zone-based gang spawning with configurable groups
- Physics-based wheelie system with continuous force
- Per-bike wheelie tuning with `[Bike_XXX]` sections
- Leader/follower formation system
- Automatic remount after crashes
- Police interaction system
- Natural rider support (apply wheelies to traffic NPCs)
- Test mode with debug overlay
- Hot-reload configuration with F10

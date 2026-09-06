# Changelog

All notable changes to Street Riders are documented here.

## [2.0.1] - 2026-09-06

### Fixed
- **Police pursuit no longer hijacks scripted cops**: when assigning a cop to chase a wheelie rider, Riders skipped nothing and issued `TASK_VEHICLE_CHASE`/`TASK_ARREST_PED` on **any** nearby police ped — including one already locked by another script (mission entity) as an arrest transport driver, police escort, or pursuit actor. That external retask broke the other script's role and made scripted vehicles drive away. Riders now skips `IsMissionEntity` cops at assignment time.
- **Cop release no longer sweeps the whole block**: `ReleasePursuingCops` used to scan every cop ped within **120 m** and clear his task + unblock his permanent events even when Riders had never tasked him — a passing rider removal could wipe a scripted driver's hold mid-arrest. It now releases only the cops Riders actually assigned to the removed rider, matched by the pursuit registry.

### Improved
- Police pursuit assignment and release are now strictly paired: a cop is only ever reset by the code that tasked him.

## [2.0.0] - 2026-06-03

### Added
- **Wheelie stunt animations**: riders in `Phase.Sustained` randomly play a showoff animation (`rcmextreme2atv`, clips `idle_b/c/d/e`) once visible on screen, after a random 300–800ms delay into the phase.
- No same animation twice in a row per rider (last played clip excluded from selection).
- `DoWheelieStunts` per-group INI option: set to `false` to disable stunt animations for a specific group.
- `StuntCooldown` INI option (default 8000ms): configurable delay before a rider can play another animation within the same Sustained phase. Enables multiple animations per long wheelie.
- 25% chance of upper-body animation (`ANIM_FLAG_UPPERBODY`): rider keeps steering while the anim plays, followed by a shorter 2s cooldown. Full-body animations (75%) use the normal `StuntCooldown`.
- **`PreventFall`** (`behavior.ini`): three-mode fall prevention — `Always` (riders never fall), `WheelieOnly` (protected only during active wheelie phases), or `Disabled` (vanilla GTA behavior). Default: `Always`.
- **`PreventFallChance`** (`behavior.ini`): probability (0–100) that fall prevention activates for a given wheelie. Only applies to `WheelieOnly` mode. At 50, riders occasionally bail naturally — useful for combining higher pitch angles with realistic variation. Default: `100`.
- **Dual physics configs per bike**: `[Bike_X]` defines physics when fall prevention is active (backwards compatible — old configs migrate as-is). Optional `[Bike_X_NoFall]` defines conservative physics when fall prevention is not active. Same pattern for `[DefaultWheelie]` / `[DefaultWheelie_NoFall]`. Falls back to the base section if `_NoFall` is absent.
- **`FormationDistance`** (`advanced.ini`): configurable minimum distance followers maintain behind the group leader during autonomous cruise. Separate from `FollowMinDistance` (player escort). Default: 50m.
- **`LeaderGracePeriodMs`** (`advanced.ini`): grace period before a temporarily absent leader is replaced. Prevents rapid leader switching caused by brief unavailability. Default: 4000ms.
- Debug HUD: overhead label turns cyan and shows `★STUNT` while an animation is actively playing.

### Improved
- **INI files restructured** into a dedicated `scripts/StreetRiders/` folder with 5 separate files: `config.ini` (general settings), `groups.ini` (rider groups), `vehicles.ini` (wheelie physics and per-bike tuning), `behavior.ini` (driving style, stunts, police, follow-player), `advanced.ini` (spawn distances, remount, low-level safety).
- **Wheelie phase detection**: physics-driven promotion from `Attempting` to `Sustained` when pitch is held above 60% of `TargetPitch` for 2 consecutive seconds. Fixes cases where a rider visually held a sustained wheelie but the state machine remained stuck in `Attempting`.
- **Obstacle detection in Sustained**: replaced single forward raycast with a 3-ray fan (center + ±20°, shorter side rays for vehicles/peds only). Catches lateral threats on road curves without false positives from road barriers or nearby group members.
- **Driving flags**: added flag 65536 (overtake). Riders now attempt to pass blocked vehicles instead of queuing behind them. New default: `868413`.
- **Police pursuit**: cops only engage riders actively in `Phase.Sustained`. Riders cruising normally or in `Attempting` no longer attract police attention.
- **Animation end detection**: driving task is re-issued via `IS_ENTITY_PLAYING_ANIM` with a 500ms guard to avoid the same-frame race condition where the flag is briefly false right after `TASK_PLAY_ANIM`.

### Fixed
- Riders going straight after animation ended: `TASK_PLAY_ANIM` (flag 0) replaces the primary driving task; once the clip finished the ped had no task and coasted straight for the rest of the phase. Fixed by detecting anim completion and re-issuing the group driving task.

## [1.2.1] - 2026-03-04

### Fixed
- Taking over a rider's bike could leave it unable to move because the remount handbrake state was still active.
- When the player enters a tracked rider bike, the script now immediately releases bike ownership and rider tracking.

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
- **Mixed vehicles**: each rider in a group can ride a different bike from `BikeModels`
- **Bike colors**: configurable paint colors per group with `BikeColors`
- **Weapons**: riders can be armed with configurable weapons per group
- **Per-group follow control**: `CanFollowPlayer` setting per group
- **Police pursuit persistence**: cops maintain their target for a configurable duration and radius

### Improved
- Dynamic follow mode: sustained speed check prevents accidental triggers
- Follow/lose conditions: configurable large radius prevents premature abandon
- Idle scenario timing: riders wait before starting scenarios, follow on foot when player moves
- Police pursuit: dedicated tracking system prevents target-switching with multiple riders
- Better pitch control

### Fixed
- Riders stuck idle after falling off their bike
- Police not pursuing riders despite option enabled
- Police stuck with sirens after suspect dies
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

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.1] - 2025-05-28

### Added

- `AssemblyMetadata("NexusModID", "1033")` for compatibility with "Updates Checker for Schedule I" mod.

### Changed

- Optimized reflection calls in `PlayerMovementMovePatch` using pre-compiled delegates for improved performance.
- Refactored `ShouldAutoJump` for improved clarity, performance, and to use inverted conditions for early exit.
- Updated `OnSceneWasLoaded` to trigger `DetectJumpAction` on "Menu" scene load instead of "Main".
- Modified `DetectJumpAction` to find UI elements starting from `GameObject.Find("MainMenu")`.
- Simplified jump state management by replacing `playerJumpStates` dictionary with a single `currentPlayerJumpState` due to `PlayerMovement` being a Singleton.
- Converted `ShouldSkipPatch` method to use C# expression-bodied member syntax.
- Used string interpolation for formatting patched method names in log messages.
- Removed redundant null check in `MonitorLiftoff` coroutine.
- Standardized logger usage to `Melon<Main>.Logger` within `PlayerMovementMovePatch`.

### Fixed

- Correctly defined `DEBUG` and `RELEASE` preprocessor constants using `<DefineConstants>` for conditional compilation, ensuring debug logs are only active in Debug builds.

### Build

- Added a PostBuild target to attempt termination of "Schedule I.exe" process using `taskkill` before copying mod files.

## [1.0.0] - 2025-05-25

### Added

- Initial implementation of the auto-bunny hopping feature.
- Automatic jump execution upon landing while the jump key is held.
- Ground detection logic to ensure jumps trigger appropriately.
- Support for both IL2CPP and Mono versions of the game.
- MelonLoader preferences for enabling/disabling the mod and configuring the auto-jump liftoff timeout.

[1.0.1]: https://github.com/RoachxD/ScheduleOne.BunnyHopper/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/RoachxD/ScheduleOne.BunnyHopper/releases/tag/v1.0.0
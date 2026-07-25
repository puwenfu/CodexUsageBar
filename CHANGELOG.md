# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.2.1] - 2026-07-26
### Added
- Added a compact taskbar menu with consistent icons, an About panel, and clearer access to refresh, startup, themes, debug tools, and exit.
- Added a persistent option to hide the five-hour quota meter while keeping the weekly meter centered and fully usable.
- Added an MIT-licensed open source project surface for the Windows 11 .NET 8 widget, with contributor, security, build, and release documentation.

### Changed
- Unified the menu, submenu, and debug-panel surfaces for a quieter Windows 11 appearance.
- Persisted local widget preferences without storing Codex credentials, account identifiers, or raw quota responses.

### Fixed
- Improved Codex process shutdown handling so canceled reads do not leave background transport work behind.

## [1.1.3] - 2026-07-25
### Fixed
- Kept the widget attached to the Windows taskbar across Show Desktop and Explorer recreation without placing it above fullscreen applications.

## [1.1.2] - 2026-07-23
### Changed
- Filled the interior of the circular meters with an invisible background brush (`#01000000`) so that the entire circular area responds to right-click context menus instead of just the outlines and text.

## [1.1.1] - 2026-07-23
### Changed
- Shifted the entire widget layout 2 pixels to the right for better visual alignment.
### Fixed
- Fixed an issue where right-clicking on the circular meters or text did not open the context menu due to hit-testing behavior.

## [1.1.0] - 2026-07-23
### Added
- Added an internal "Debug Panel" tool accessible from the right-click context menu, allowing users to manually tweak quota values and text strings to easily test and simulate various widget visual states.

## [1.0.3] - 2026-07-23
### Changed
- Changed the widget's background color to fully transparent so that only the meters and text are visible, blending seamlessly with the taskbar.

## [1.0.2] - 2026-07-23
### Fixed
- Fixed an issue where the widget would disappear behind the Windows 11 taskbar when the taskbar was clicked. The widget is now strictly owned by the taskbar to ensure it remains correctly layered above it.

## [1.0.1] - 2026-07-23
### Fixed
- Fixed an issue where the Codex CLI usage data could not be retrieved on Windows due to `Process.Start` failing to locate the `.cmd` wrapper. The protocol transport now properly wraps npm-installed CLI commands with `cmd.exe /c`.
- Fixed an issue where the usage widget failed to align to the bottom-left of the taskbar on multi-monitor or high-DPI (150%+) setups.
- Upgraded the application to support Per-Monitor V2 DPI awareness, ensuring crystal-clear rendering and accurate pixel positioning across all screens.
- Completely bypassed unreliable `SHAppBarMessage` coordinate virtualization by using raw native `GetWindowRect` on the taskbar handle for foolproof placement.

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.3.0] - 2026-07-27
### Added
- Added automatic light and dark appearance switching that follows the Windows system theme.
- Added light variants for all five quota meter color themes.

### Changed
- Made the taskbar widget, context menu, tooltips, and debug panel adapt their colors when the Windows system theme changes.

## [1.2.5] - 2026-07-26
### Added
- Added three persistent refresh animation styles and a rose meter theme.

### Changed
- Refreshed the repository overview with build, release, download, platform, and license status.
- Updated pinned GitHub Actions to their Node.js 24-backed stable releases.
- Refined menu icons, labels, and compact switches, and made the debug panel narrower.
- Made refresh feedback theme-aware with a smooth exit transition at 100%, 150%, and 200% scaling.

### Fixed
- Replaced the stale hard-coded app-server client version with the current CodexUsageBar product version.
- Kept nested menus right-first with native popup capture and a two-physical-pixel visual gap across DPI levels.
- Kept the context menu and debug panel clear of the taskbar while opening the debug panel beside the menu.
- Blocked release validation when the version source and changelog disagree, while retaining the v1.2.4 compressed archive and legal-file contract.

## [1.2.4] - 2026-07-26
### Changed
- Reduced menu icon size to 80% while preserving menu item hit targets and alignment.
- Compressed the self-contained single-file executable to reduce extracted disk usage.
- Pinned GitHub Actions to reviewed commit SHAs and added final release-candidate packaging to CI.
- Restricted local SDK roll-forward to the latest .NET 8 feature band and added weekly dependency checks.

### Fixed
- Restored the standard WPF submenu popup contract and right-first placement so nested menus use native menu capture and close behavior.
- Corrected public download and private vulnerability-reporting links after the repository transfer.
- Isolated singleton tests from a running CodexUsageBar instance.

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

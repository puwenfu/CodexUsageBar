# Architecture

CodexUsageBar has four boundaries: the WPF application that renders and accepts
widget input; the core quota and presentation rules; the local Codex app-server
protocol adapter; and Windows integration for taskbar and Codex-window
placement, DPI, Explorer recovery, the system tray, and optional startup
control.

## Data flow

The protocol adapter identifies itself with the current application product
version and requests quota information from the local Codex app-server.
The core layer converts safe results into the five-hour and weekly display
models. The WPF application renders those models as two compact meters and
uses a placement coordinator to select the requested surface. Authentication
credentials are neither copied nor persisted by the widget.

## Placement hosts

The Windows integration locates the primary taskbar and keeps the widget inside
its rectangle without crossing the top edge. The host does not take focus,
appear in Alt+Tab, or block unrelated taskbar controls. Position and size are
calculated for the active display scaling.

The same WPF surface can be detached from the taskbar and positioned in the
footer area of a visible desktop Codex window without becoming its child or
owner. A persistent notification-area icon remains available for opening the
same menu or using tray-only mode. Placement preference and taskbar/Codex
horizontal offsets are stored locally; unavailable surfaces fall back to a
safe available surface.

## Menu interaction

The taskbar widget is intentionally nonactivating, so normal focus-loss
notifications are not sufficient to dismiss its menu. While the menu is open,
a low-level mouse listener observes button-down coordinates and closes the menu
only when the click is outside the root menu and every open submenu. The
listener is removed as soon as the menu closes and never blocks the system
input chain.

## Refresh and recovery

Users can refresh from the widget, and normal refreshes also occur in the
background. While data is being refreshed, the UI shows a lightweight refresh
state. When a safe prior value exists, a protocol error, timeout, or temporary
Codex incompatibility preserves that value and leaves a clear retry path.
Windows integration also re-establishes placement after the taskbar or Explorer
is recreated.

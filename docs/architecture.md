# Architecture

CodexUsageBar has four boundaries: the WPF application that renders and accepts
widget input; the core quota and presentation rules; the local Codex app-server
protocol adapter; and Windows taskbar integration for discovery, placement, DPI,
Explorer recovery, and optional startup control.

## Data flow

The protocol adapter identifies itself with the current application product
version and requests quota information from the local Codex app-server.
The core layer converts safe results into the five-hour and weekly display
models. The WPF application renders those models as two compact meters and
places its host inside the primary bottom taskbar rectangle. Authentication
credentials are neither copied nor persisted by the widget.

## Taskbar host

The Windows integration locates the primary taskbar and keeps the widget inside
its rectangle without crossing the top edge. The host does not take focus,
appear in Alt+Tab, or block unrelated taskbar controls. Position and size are
calculated for the active display scaling.

## Refresh and recovery

Users can refresh from the widget, and normal refreshes also occur in the
background. While data is being refreshed, the UI shows a lightweight refresh
state. When a safe prior value exists, a protocol error, timeout, or temporary
Codex incompatibility preserves that value and leaves a clear retry path.
Windows integration also re-establishes placement after the taskbar or Explorer
is recreated.

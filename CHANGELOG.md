# Changelog

## Next

- Added automatic UI language selection based on the Windows display language: Japanese for `ja-*`, English otherwise.

## v1.2.0

- Fixed taskbar labels remaining at `--` when the notification tooltip exceeded the Windows `NotifyIcon.Text` limit.
- Renamed the wired Ethernet metric from `NET` to `LAN`.
- Added a separate `WIFI` metric for active IEEE 802.11 adapters.
- Added LAN and WIFI toggles to the display-label menu.
- Updated the taskbar summary, tooltip, README, and tests for the new labels.

# Changelog

## v1.3.0

- Added automatic UI language selection based on the Windows display language: Japanese for `ja-*`, English otherwise.
- Added Japanese and English README links and a complete English README.
- Saved display label and always-on-top settings across restarts.
- Added a one-time startup notification and a friendly duplicate-launch message.
- Added settings reset, log-folder, and latest-release menu actions.
- Saved the history window position and size across restarts.
- Showed `--` for LAN or WIFI when the corresponding adapter is unavailable.

## v1.2.0

- Fixed taskbar labels remaining at `--` when the notification tooltip exceeded the Windows `NotifyIcon.Text` limit.
- Renamed the wired Ethernet metric from `NET` to `LAN`.
- Added a separate `WIFI` metric for active IEEE 802.11 adapters.
- Added LAN and WIFI toggles to the display-label menu.
- Updated the taskbar summary, tooltip, README, and tests for the new labels.

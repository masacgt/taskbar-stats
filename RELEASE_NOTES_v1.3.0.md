# TaskbarStats v1.3.0

## 日本語

TaskbarStats v1.3.0では、Windowsの表示言語に合わせた英語対応と、日常的に使いやすくする設定・診断機能を追加しました。

### 主な変更

- Windowsの表示言語を自動判定
  - 日本語環境では日本語で表示
  - 日本語以外の環境では英語で表示
  - 言語メニューや個別の言語設定は不要
- 表示ラベルの選択状態を再起動後も保持
- 最前面表示の設定を再起動後も保持
- 履歴画面の位置とサイズを再起動後も保持
- 「設定を初期化」を追加
- 「ログフォルダーを開く」を追加
- 「最新版を確認」を追加
- 二重起動時に分かりやすいメッセージを表示
- 初回起動時に通知を1回表示
- LANまたはWi-Fiが未接続の場合は`--`を表示
- 有線LANを`LAN`、無線LANを`WIFI`として個別に表示

### ダウンロード

- [ZIP版](https://github.com/masacgt/taskbar-stats/releases/download/v1.3.0/taskbar-stats-v1.3.0-win-x64.zip)
- [単体EXE版](https://github.com/masacgt/taskbar-stats/releases/download/v1.3.0/TaskbarStats-v1.3.0-win-x64.exe)

どちらも自己完結型のWindows x64版です。.NET 8 Runtimeを別途インストールする必要はありません。

## English

TaskbarStats v1.3.0 adds automatic English support based on the Windows display language, along with settings and troubleshooting features for everyday use.

### Highlights

- Automatically selects the UI language from the Windows display language
  - Japanese on Japanese Windows systems
  - English on all other Windows display languages
  - No language menu or separate language setting is required
- Preserves display label choices across restarts
- Preserves the always-on-top setting across restarts
- Preserves the history window position and size across restarts
- Added **Reset settings**
- Added **Open log folder**
- Added **Check for updates**
- Shows a clear message when a second instance is launched
- Shows a one-time notification on the first launch
- Shows `--` when LAN or Wi-Fi is unavailable
- Displays wired and wireless network speeds separately as `LAN` and `WIFI`

### Download

- [ZIP package](https://github.com/masacgt/taskbar-stats/releases/download/v1.3.0/taskbar-stats-v1.3.0-win-x64.zip)
- [Standalone EXE](https://github.com/masacgt/taskbar-stats/releases/download/v1.3.0/TaskbarStats-v1.3.0-win-x64.exe)

Both packages are self-contained Windows x64 builds. The .NET 8 Runtime does not need to be installed separately.

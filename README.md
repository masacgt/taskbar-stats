# TaskbarStats

Windowsのタスクバー右端に、CPU・メモリ・GPU・ディスク・ネットワークの状態を常時表示する軽量なシステムモニターです。

タスクバー上の表示は1秒ごとに更新され、トレイアイコンの右クリックメニューから表示項目、最前面表示、スタートアップ起動を変更できます。インストール作業は不要で、ダウンロードしたEXEまたはZIPから起動できます。

## スクリーンショット

通常表示:

![TaskbarStats 通常表示](docs/images/taskbar-stats.jpg)

右クリックメニューと表示項目の選択:

![TaskbarStats 設定メニュー](docs/images/taskbar-stats-menu.jpg)

## ダウンロード

最新版は[Releasesページ](https://github.com/masacgt/taskbar-stats/releases)からダウンロードできます。

| ファイル | 用途 |
|---|---|
| [ZIP版](https://github.com/masacgt/taskbar-stats/releases/download/v1.2.0/taskbar-stats-v1.2.0-win-x64.zip) | EXEと必要なファイルをまとめた通常版。展開して使用します。 |
| [単体EXE版](https://github.com/masacgt/taskbar-stats/releases/download/v1.2.0/TaskbarStats-v1.2.0-win-x64.exe) | 1ファイルで起動できます。持ち運びに便利です。 |
| [最新版を表示](https://github.com/masacgt/taskbar-stats/releases/latest) | 今後の最新版を確認できます。 |

ZIP版と単体EXE版は自己完結型です。実行するPCに.NET 8 Runtimeを別途インストールする必要はありません。

### 初回起動

1. ZIP版は任意のフォルダーに展開します。
2. `TaskbarStats.exe`を起動します。
3. タスクバー右端に統計情報が表示されます。
4. 終了する場合は、表示部分またはトレイアイコンを右クリックして「終了」を選択します。

Windows SmartScreenが表示された場合は、配布元とファイル名を確認してから実行してください。

## 表示される項目

- **CPU**: CPU使用率
- **RAM**: メモリ使用率
- **GPU**: GPU使用率
- **VRAM**: GPUメモリ使用率
- **Disk1, Disk2, ...**: 物理ディスクごとのアクティブ時間
- **LAN**: 有線LANの送受信速度（Mbps）
- **WIFI**: Wi-Fiの送受信速度（Mbps）

表示項目は、トレイアイコンを右クリックして「表示ラベル」から個別にON/OFFできます。使用していない項目を非表示にすると、タスクバー上の表示が詰まって見やすくなります。

## 操作方法

トレイアイコンまたはタスクバーの統計表示を右クリックすると、次の操作ができます。

| 操作 | 内容 |
|---|---|
| 履歴を表示 | CPU・RAM・GPU・VRAMの直近約5分のグラフを表示します。ダブルクリックでも開けます。 |
| 表示ラベル | CPU、RAM、GPU、VRAM、Disk、LAN、WIFIの表示を切り替えます。 |
| 最前面に固定 | タスクバー表示を他のウィンドウより前面に固定します。 |
| 自動実行 | Windowsへのサインイン時に自動起動するかを切り替えます。 |
| 終了 | アプリを終了します。 |

負荷が70%以上になるとアイコンが黄色、90%以上になると赤色に変わります。

## 表示言語

表示言語はWindowsの表示言語から自動判定します。言語メニューや個別設定はありません。

- Windowsの表示言語が日本語（`ja-*`）の場合: 日本語
- それ以外の表示言語の場合: 英語

数値ラベル（CPU、RAM、GPU、LAN、WIFIなど）はどの言語でも共通です。

## GPU対応

| GPU | 使用率 | VRAM | 温度 | クロック |
|---|---:|---:|---:|---:|
| NVIDIA | ○ | ○ | ○ | ○ |
| AMD | ○ | ○ | ○ | ○ |
| その他 | ○ | ○ | - | - |

NVIDIAではNVAPI、AMDではADL2を優先して使用します。メーカーAPIを利用できない場合は、WindowsのPerformance Data Helper（PDH）とDXGIを使用してGPU使用率とVRAMを取得します。

GPUが検出できない環境でも、CPU・RAM・ディスク・LAN・WIFIの表示は利用できます。GPUとVRAMは`-`または`--`で表示されます。

## 動作環境

- Windows 10 / 11
- x64 PC
- ZIP版・単体EXE版は.NET 8 Runtime不要
- GPU機能は搭載GPUとドライバーの対応状況に依存します

## 起動できない場合

1. タスクマネージャーで`TaskbarStats.exe`が既に起動していないか確認します。アプリは二重起動しません。
2. ZIP版を使用している場合は、ZIPの中身をすべて展開してからEXEを起動します。
3. Windows Defenderまたはセキュリティソフトが隔離していないか確認します。
4. 実行後に表示が更新されない場合は、`%LOCALAPPDATA%\TaskbarStats\crash.log`を確認します。
5. それでも起動しない場合は、使用したファイル名、Windowsのバージョン、`crash.log`の内容を添えてIssueを作成してください。

## 開発者向けビルド

開発には.NET 8 SDKが必要です。

```bash
dotnet restore
dotnet build TaskbarStats.sln -c Release
dotnet test TaskbarStats.sln -c Release
```

自己完結型のWindows x64版を作成する場合:

```bash
dotnet publish TaskbarStats/TaskbarStats.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o dist
```

フレームワーク依存版を作成する場合:

```bash
dotnet publish TaskbarStats/TaskbarStats.csproj \
  -c Release \
  -r win-x64 \
  --self-contained false \
  -o dist-framework
```

## プロジェクト構成

```text
TaskbarStats/
  Program.cs                    エントリポイントと単一インスタンス管理
  Samplers/                     CPU、RAM、GPU、ディスク、LAN、WIFIの取得
  UI/                           トレイメニュー、タスクバー表示、履歴画面
  Utils/                        自動起動、ログなどのWindows連携
TaskbarStats.Core/              OSに依存しない計算・履歴・表示整形
TaskbarStats.Tests/             Coreの単体テスト
```

## ライセンス

MIT License



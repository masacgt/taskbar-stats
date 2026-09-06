# TaskbarStats

**v1.1**

Windows のシステムトレイに CPU / RAM / GPU / VRAM / ディスク / ネットワークの使用率を表示する軽量なトレイアプリケーションです。

## 機能

- **トレイアイコン + ホバーツールチップ** で 1 秒ごとに更新
  - CPU 使用率
  - RAM 使用率（使用量 / 総量）
  - GPU 使用率（NVIDIA: NVAPI / AMD: ADL2 を自動検出）
  - VRAM 使用量 / 総量
  - GPU 温度・クロック（取得できる場合）
  - 物理ディスクごとのアクティブな時間 %（Disk1, Disk2, …）
  - イーサネット速度（NET, Mbps）
- **タスクバー右端の常時ラベル**（固定幅、非表示ラベルは右に詰まる）
- **表示ラベルの選択**（右クリック「表示ラベル」で各ラベルのチェック ON/OFF）
- **負荷によるアイコンの色変化**: 70% 以上で黄色、90% 以上で赤
- **履歴グラフ**（ダブルクリックまたは右クリックメニューから表示）
  - CPU / RAM / GPU / VRAM を直近約 5 分分の折れ線で表示（min / avg / max 付き）
- **スタートアップ自動実行**（右クリックメニューで ON/OFF、`HKCU\...\Run` に登録）
- 単一インスタンス（Mutex で二重起動を防止）
- 自己完結したビルド（.NET 8 不要で実行可能）

## 実行

配布物のフォルダ内の `TaskbarStats.exe` をダブルクリックするだけです（自己完結型は .NET 8 のインストール不要）。

- ホバー: 使用率を表示
- ダブルクリック / 右クリック「履歴を表示」: 履歴グラフ
- 右クリック「表示ラベル」: 各ラベル（CPU / RAM / GPU / VRAM / Disk / NET）の表示切替
- 右クリック「自動実行: OFF/ON」: スタートアップ登録の切替
- 右クリック「終了」: アプリを終了

## ビルド

.NET 8 SDK が必要です。

```bash
dotnet build -c Release
dotnet test
```

### Windows 用ビルドを作成

```bash
dotnet publish TaskbarStats/TaskbarStats.csproj -c Release -r win-x64 --self-contained true -o dist
dotnet publish TaskbarStats/TaskbarStats.csproj -c Release -r win-x64 --self-contained false -o dist-framework
```

`dist/` の中身（`TaskbarStats.exe` 一式）が完成品です（Linux からのクロスコンパイルでも動作します）。

- `dist/`: .NET 8 未インストールの PC でも動作する自己完結型（約 130 MB）。
- `dist-framework/`: 軽量ですが、実行先に .NET 8 Desktop Runtime が必要です。

## プロジェクト構成

```
TaskbarStats/
  Program.cs            エントリポイント（単一インスタンス管理）
  Samplers/CpuSampler.cs        CPU（GetSystemTimes 差分）
  Samplers/RamSampler.cs        RAM（GlobalMemoryStatusEx）
  Samplers/NvidiaSampler.cs     GPU（NVAPI）
  Samplers/AmdSampler.cs        GPU（ADL2）
  Samplers/GpuDetector.cs       GPU ベンダ自動検出
  Samplers/DiskSampler.cs       ディスクのアクティブ時間 %（PDH）
  Samplers/NetSampler.cs        イーサネット速度（NetworkInformation）
  UI/TrayApp.cs           トレイアイコン / メニュー / タイマー
  UI/HistoryForm.cs       履歴グラフ
  UI/IconFactory.cs       負荷に応じたアイコン生成
  UI/StatsLabel.cs        タスクバー右端の常時ラベル
  Utils/AutoStart.cs      スタートアップ登録
TaskbarStats.Core/        純ロジック（OS 非依存）
  Models/SystemStatsSample.cs   1 サンプルのデータ構造
  Samplers/CpuMath.cs       CPU 使用率の計算
  Samplers/DiskAggregator.cs  ディスク番号ごとの集約
  Samplers/NetMath.cs       Mbps 換算・上限クランプ
  UI/StatsHistory.cs        サンプル履歴の保持
  UI/LoadLevel.cs           負荷レベル判定（閾値 70/90）
  UI/StatsFormatter.cs      ラベル / ツールチップ文字列生成
TaskbarStats.Tests/       単体テスト（Core の純ロジック、Linux でも実行可）
```

## 対応 GPU

| ベンダ | API | 使用率 | VRAM | 温度 | クロック |
|--------|-----|--------|------|------|----------|
| NVIDIA | NVAPI (nvapi64.dll) | ○ | ○ | ○ | ○ |
| AMD    | ADL2 (atiadlxx.dll)  | ○ | ○ | ○ | ○ |
| その他 | Performance (PDH) + DXGI | ○ | ○ | - | - |

ベンダ API (NVAPI / ADL2) が読み込めない場合は Performance Sampler (PDH + DXGI) に自動フォールバックし、GPU 使用率・VRAM を表示します。
- VRAM は DXGI の `AdapterLuid` で対象アダプタを特定し、そのアダプタ分だけ合計するため、複数 GPU 環境でも過大計上しません。
- PDH の GPU オブジェクトが見つからない場合はカウンターを自動検出します。

GPU が検出できない場合、GPU / VRAM の行は「-」で表示され、CPU / RAM の表示は通常通り動作します。

## 配布物

- `taskbar-stats-v1.1.0-win-x64-selfcontained.zip`: 自己完結型（.NET 8 不要、展開後約 130 MB）
- `taskbar-stats-v1.1.0-win-x64-frameworkdependent.zip`: 軽量版（.NET 8 Desktop Runtime が必要）

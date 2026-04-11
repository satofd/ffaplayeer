# FFmPlayer 仕様書 (実装準拠)

最終更新: 2026-04-11
対象: 現行リポジトリ実装 (Avalonia + FFmpeg.AutoGen + NAudio)

## 1. 目的とスコープ

この文書は、FFmPlayer を他の AI または別実装で再構築するための技術仕様である。

- 目的:
  - 現行実装の振る舞いを、README では不足する詳細を含めて固定する
  - 再実装時の挙動差異を最小化する
- 対象範囲:
  - UI/入力、再生制御、プレイリスト、A-B リピート
  - A/V 同期、シーク、フレームステップ、フレームキャッシュ
  - 設定保存、ショートカット設定、エラー処理とログ
- 非対象:
  - 将来機能案、UI デザイン改善案、リファクタ提案

## 2. 技術スタックと構成

- UI: Avalonia (MVVM)
- MVVM 基盤: CommunityToolkit.Mvvm
- デコード: FFmpeg.AutoGen
- 音声出力: NAudio (WaveOutEvent + BufferedWaveProvider)
- 設定永続化: JSON (settings.json)

主要責務:

- `FFmPlayer/ViewModels/MainViewModel.cs`
  - 再生状態管理、コマンド、デコードループ、同期制御、OSD
- `FFmPlayer/Services/FFmpegDecoder.cs`
  - メディア open/close、フレーム/音声デコード、シーク適用
- `FFmPlayer/Services/AudioPlayer.cs`
  - 音声再生、相対再生位置の提供
- `FFmPlayer/Models/AppSettings.cs`
  - 設定モデル、デフォルト値、ショートカット初期値
- `FFmPlayer/Services/SettingsService.cs`
  - 設定読込/保存

## 3. アプリ起動/終了

起動フロー:

1. `SettingsService.Load()` で設定を読込
2. `MainViewModel` を設定付きで生成
3. `MainWindow` に DataContext を紐付け
4. テーマ設定を起動時に反映
5. FFmpeg 初期化失敗時は OSD でエラー表示

終了フロー:

1. アプリ終了イベントで `mainVm.SaveSettings()` 呼び出し
2. `mainVm.Dispose()` 実行

## 4. 画面仕様

### 4.1 MainWindow

- 3段構成: カスタムタイトルバー / ビデオ表示領域 / コントロールバー
- `Topmost` は `IsAlwaysOnTop` バインドで制御
- Seek バー:
  - `PointerPressed` でユーザーシーク中フラグ ON
  - `PointerReleased` で `SeekToCommand` を 1 回実行
- ビデオ領域:
  - マウス中クリックでプレイリスト表示
  - 右クリックでコンテキスト操作
  - 左ダブルクリックで Fit (Fill + Crop)

### 4.2 SettingsWindow

タブ:

- General: Theme, Time Display Mode, Always On Top
- Playback: Playback Speed
- Performance:
  - Memory Limit (Enable/MB)
  - Frame Buffer Size
  - Step Scan Window (Forward/Backward)
  - Audio Master Sync (Video Lead Sleep / Video Drop Lag)
  - A/V Sync OSD Mode
- Shortcuts:
  - 全ショートカット編集
  - 重複検証あり (重複時は保存失敗)

### 4.3 PlaylistWindow

- リスト表示、再生、削除、全削除、上下移動
- 再生モードサイクルボタンあり

### 4.4 MediaInfoWindow

- ファイル情報、映像/音声 codec、fps、bitrate、duration、サイズ等を表示

### 4.5 OSD

- `ShowOsd()` で表示、3秒で自動消去
- A/V 同期情報の付加は `AvSyncOsdMode` で制御

## 5. 再生状態と遷移

状態:

- Stopped (`IsStopped=true`)
- Playing (`IsPlaying=true`, `IsPaused=false`)
- Paused (`IsPaused=true`)

主遷移:

- Play/Pause:
  - Playing -> Paused
  - Paused -> Playing
  - Stopped -> 現在 index または 0 を再生開始
- Stop:
  - タスク停止、audio stop、decoder close、位置初期化、バッファクリア
- EOS:
  - RepeatOne: 同一 index を再生
  - Sequential: 次 index、末尾で停止
  - Random: ランダム選択 (要素 > 1)、単一要素なら停止

## 6. 再生制御仕様

### 6.1 シーク

- 相対シーク (`SeekCommand`): 現在位置 ± 秒
- 絶対シーク (`SeekToCommand`): 指定秒へ移動
- 共通動作:
  - 位置を [0, Duration] に clamp
  - フレームバッファクリア
  - decoder に seek 要求
  - 音声クロック基準と音声バッファを再初期化

### 6.2 1フレーム前進

- 再生中なら一時停止してから実行
- `StepForwardOneVideoFrame()` を使用し、次動画フレームを直接デコード
- 成功時は表示位置更新

### 6.3 1フレーム後退

- 再生中なら一時停止してから実行
- 目標位置: `current - 1/fps`
- C# フレームリングバッファを優先検索
- ミス時:
  - `target - StepScanWindowBackwardSeconds` へ戻って再デコード
  - 最大 `FrameBufferSize` 枚をキャッシュ再構築
  - 目標直前フレームを表示

### 6.4 速度・音量

- 速度: 0.1x〜3.0x、0.1刻み
- リセット: 1.0x
- ミュート時は audio volume を 0

## 7. A/V 同期仕様

### 7.1 基本式

- `audioMasterSec = baseSeconds + audioPlayedSeconds - AudioSyncDelaySeconds`
- `drift = videoPtsSec - audioMasterSec`

補足:

- `audioPlayedSeconds` は NAudio の再生位置を相対化した値
- `baseSeconds` は seek 後などに再アンカーされる基準秒
- `AudioSyncDelaySeconds` は意図的な映像の遅延確保（デフォルト 0.500 秒）。描画オーバーヘッド等を吸収するための特例ワークアラウンド。

### 7.2 閾値動作

- `drift > VideoLeadSleepThresholdSeconds`
  - 映像先行として短時間待機
- `videoPts + VideoDropLagThresholdSeconds < audioMasterSec`
  - 映像遅延として当該フレームをドロップ
- 異常に遅れが大きい場合
  - 音声基準を映像位置へ再アンカーし、連続ドロップを防止

### 7.3 Seek 後の再同期

- seek 要求は decoder 側でペンディング化し、デコードスレッドで適用
- seek 適用後、最初の動画フレーム PTS で音声基準を再設定
- 音声バッファはクリアし、再生位置基準をリセット

## 8. フレームキャッシュ仕様

実装:

- 固定長リングバッファ (`FrameRingBuffer`)
- 保持要素: `byte[] pixels`, width, height, position

削除/制限:

- 容量上限: `FrameBufferSize`
- メモリ上限有効時: `MemoryLimitMB` を超えたら古いフレームから削除

検索:

- 後退時は目標時刻以前で最も近いフレームを後方探索

## 9. プレイリスト・再生モード

- 項目型: `PlaylistItem`
- 追加:
  - ファイルダイアログ
  - URL 入力
  - ドラッグ&ドロップ
- 重複追加防止:
  - 同一 path は重複登録しない
- 再生モード:
  - Sequential / Random / RepeatOne

## 10. A-B リピート

- A 設定: 現在位置を `AbStart`
- B 設定: 現在位置を `AbEnd`
- A/B が揃うと有効化
- ループ中は `position >= AbEnd` で `AbStart` へ seek
- OFF 時は A/B をクリア

## 11. 設定仕様

保存先:

- `AppContext.BaseDirectory/settings.json`

保存トリガー:

- SettingsWindow の Save
- アプリ終了時

### 11.1 デフォルト値

- Volume: 1.0
- IsMuted: false
- PlaybackMode: Sequential
- PlaybackSpeed: 1.0
- Theme: Default
- AlwaysOnTop: false
- TimeDisplayMode: 0
- MemoryLimitEnabled: true
- MemoryLimitMB: 2048
- WindowWidth/Height: 1280 / 720
- FrameBufferSize: 120
- StepScanWindowForwardSeconds: 5.0
- StepScanWindowBackwardSeconds: 8.0
- VideoLeadSleepThresholdSeconds: 0.010
- VideoDropLagThresholdSeconds: 0.050
- AvSyncOsdMode: 0

### 11.2 設定クランプ

- StepScanWindowForward/BackwardSeconds: 1.0〜20.0
- VideoLeadSleepThresholdSeconds: 0.001〜0.200
- VideoDropLagThresholdSeconds: 0.005〜0.500
- AvSyncOsdMode: 0〜2

## 12. ショートカット仕様

ショートカットは settings に保存され、起動時読み込みで反映される。

既定値:

- SeekForward1s: Right
- SeekBackward1s: Left
- SeekForward10s: Shift+Right
- SeekBackward10s: Shift+Left
- SeekForward60s: Ctrl+Right
- SeekBackward60s: Ctrl+Left
- PlayPause: Space
- Stop: S
- StepForward: OemPeriod
- StepBackward: OemComma
- ToggleMute: M
- ToggleFullscreen: F
- ExitFullscreen: Escape
- OpenFile: Ctrl+O
- OpenUrl: Ctrl+U
- ShowPlaylist: L
- ShowMediaInfo: I
- IncreaseSpeed: OemPlus
- IncreaseSpeedAlt: Add
- DecreaseSpeed: OemMinus
- DecreaseSpeedAlt: Subtract
- ResetSpeed: D0
- SetAbStart: A
- SetAbEnd: B
- CycleTimeDisplay: T
- TakeScreenshot: PrintScreen

入力判定:

- 形式: 修飾キー + Key 名
- 修飾キー: Ctrl / Shift / Alt / Meta
- 完全一致判定 (`Key` と `KeyModifiers`)

## 13. エラー処理とロギング

- FFmpeg 初期化失敗: OSD へ詳細表示
- open 失敗: `Cannot open: {DisplayName}`
- DecodeLoop 例外: logger 記録 + OSD へ再生エラー表示
- ログファイル:
  - `AppContext.BaseDirectory/ffmplayer.log`
  - 追記形式、スレッドセーフ

## 14. README との差分

README に未記載または薄いが実装済みの主項目:

- URL 再生 (Ctrl+U)
- PrintScreen スクリーンショット保存
- ショートカット完全カスタマイズと重複検証
- A/V 同期閾値と OSD モード
- seek 後の同期再アンカー詳細

## 15. メモリ反映事項 (再発防止ルール)

リポジトリメモの知見を公式仕様として採用する。

1. Seek 後 A/V 同期
   - WaveOut の累積再生位置を絶対値として扱わない
   - seek 時に音声クロック基準をリセットし、相対位置で同期計算する
2. シークバーのドラッグ操作
   - ドラッグ中はプレビューのみ
   - 確定 seek は PointerReleased 時に 1 回だけ実行する

## 16. 再実装時の最小検証項目

1. 起動直後
   - settings.json があれば反映される
   - テーマが即時適用される
2. 再生系
   - Play/Pause/Stop が状態遷移通りに動作
   - EOS 時に再生モード通り遷移
3. シーク/同期
   - 10秒/60秒シークで音ズレが継続しない
   - A-B ループで安定して先頭へ戻る
4. フレーム操作
   - 前進は次フレーム表示
   - 後退は cache hit/miss 両方で破綻しない
5. 設定保存
   - Settings 保存後に再起動して値が保持される
6. ショートカット
   - 変更が反映される
   - 重複登録時は保存失敗する

## 17. 参照ファイル

- `README.md`
- `FFmPlayer/App.axaml.cs`
- `FFmPlayer/Models/AppSettings.cs`
- `FFmPlayer/Services/SettingsService.cs`
- `FFmPlayer/Services/FFmpegDecoder.cs`
- `FFmPlayer/Services/AudioPlayer.cs`
- `FFmPlayer/Services/Logger.cs`
- `FFmPlayer/ViewModels/MainViewModel.cs`
- `FFmPlayer/ViewModels/SettingsViewModel.cs`
- `FFmPlayer/ViewModels/PlaylistViewModel.cs`
- `FFmPlayer/Views/MainWindow.axaml`
- `FFmPlayer/Views/MainWindow.axaml.cs`
- `FFmPlayer/Views/SettingsWindow.axaml`
- `FFmPlayer/Views/SettingsWindow.axaml.cs`
- `FFmPlayer/Views/PlaylistWindow.axaml`

## 18. 再実装タスク分解 (他AI向け)

### Phase 1: 土台構築

1. プロジェクト作成

- Avalonia アプリを作成
- MVVM Toolkit と FFmpeg.AutoGen、NAudio を導入

2. 設定基盤

- `AppSettings` を本仕様のデフォルト値で実装
- `SettingsService` を JSON 読込/保存で実装

3. ViewModel 骨格

- `MainViewModel` の主要状態 (`IsPlaying`, `IsPaused`, `Position`, `Duration`) を実装

受け入れ条件:

- アプリ起動で settings が読込まれる
- 終了時に settings が保存される

### Phase 2: 再生エンジン

1. FFmpegDecoder 実装

- open/close
- video/audio デコード
- pending seek 適用

2. AudioPlayer 実装

- `WaveOutEvent` + `BufferedWaveProvider`
- 相対再生位置 (`GetPlayedSeconds`) を提供

3. DecodeLoop 実装

- 再生/一時停止制御
- EOS 時のモード遷移

受け入れ条件:

- ファイル再生が開始/停止できる
- シーケンシャル再生で EOS 時に次曲へ遷移する

### Phase 3: UI と操作

1. MainWindow 構成

- タイトルバー/ビデオ/コントロールバー
- SeekBar、ボタン群、OSD

2. 入力処理

- ショートカット判定 (`Key` + `Modifiers`)
- マウス操作 (中クリック、右クリック、ダブルクリック)

3. ダイアログ

- Settings/Playlist/MediaInfo/URL 入力

受け入れ条件:

- 既定ショートカットで主要操作が実行できる
- ドラッグ&ドロップでファイル再生開始できる

### Phase 4: 同期・シーク・フレーム操作

1. A/V 同期

- `drift = video - audioMaster` 実装
- lead sleep / lag drop 閾値適用

2. seek 再同期

- seek 後に音声基準を再設定
- 音声バッファクリア

3. frame step

- 前進: direct decode
- 後退: cache hit 優先、miss 時 backfill

受け入れ条件:

- 連続 seek 後も同期が破綻しない
- 1フレーム後退が cache hit/miss 両方で動作する

### Phase 5: 設定UI と品質担保

1. SettingsWindow

- General/Playback/Performance/Shortcuts タブ

2. ショートカット重複検証

- 重複時は保存拒否し、エラーメッセージ表示

3. ログと例外

- `ffmplayer.log` へ記録
- UI には要点のみ表示

受け入れ条件:

- 設定変更が保存され、再起動後に復元される
- 重複ショートカット保存がブロックされる

### 追加ルール (重要)

1. seek 同期:

- WaveOut の累積位置を絶対時刻として使わない
- 必ず基準リセット + 相対位置計算を行う

2. SeekBar 操作:

- ドラッグ中は確定 seek を連発しない
- PointerReleased で 1 回だけ seek する

### 引き継ぎ時チェックリスト

1. `settings.json` なし起動で既定値になる
2. settings 保存後に再起動して値が維持される
3. Play/Pause/Stop/Next/Prev が期待通り
4. A/B ループが B 到達時に A へ戻る
5. ショートカット変更が実動作へ反映される
6. `ffmplayer.log` に例外情報が記録される

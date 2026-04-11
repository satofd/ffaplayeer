# FFmPlayer

FFmPlayer is a media player built with .NET and Avalonia UI, utilizing FFmpeg.AutoGen for video/audio decoding and NAudio for sound output. It offers a variety of playback features with a focus on robust A/V synchronization and customizable controls.

## 🌟 主な機能 (Features)

- **充実した再生制御**: 再生、一時停止、停止、シーク操作
- **A-B リピート**: 指定区間のループ再生
- **コマ送り・コマ戻し**: 1フレーム単位での前進・後退機能と専用キャッシュ機構
- **プレイリスト機能**: ドラッグ＆ドロップによる追加、ローカルパス及びURLからの直接再生、再生モード設定（順次・ランダム・1曲リピート）
- **ショートカットのカスタマイズ**: アプリの主要操作を完全にユーザー好みのキーボードショートカットに設定可能（重複検知機能付き）
- **A/V 同期制御**: FFmpegとNAudioを用いた精密な映像と音声の同期制御（遅延補正スレッショルド等の詳細設定可能）
- **メディア情報の表示＆OSD**: 動画・音声コーデックや各種メディア情報を瞬時に表示。状態変化のオンスクリーンディスプレイ（OSD）表示に対応

## 💻 技術スタック (Tech Stack)

- **UI Framework**: Avalonia UI
- **MVVM Framework**: CommunityToolkit.Mvvm
- **Media Decoding**: FFmpeg.AutoGen
- **Audio Playback**: NAudio
- **Configuration**: JSONベースの設定保存処理

## 📁 主要なファイル構成と責務

- `FFmPlayer/ViewModels/MainViewModel.cs`: アプリケーションのUIロジック、再生状態管理、デコードループ、同期制御
- `FFmPlayer/Services/FFmpegDecoder.cs`: FFmpegを利用した動画・音声のパケットデコード処理、シーク位置の適用
- `FFmPlayer/Services/AudioPlayer.cs`: NAudioを用いた音声再生と現在位置の提供
- `spec.md`: 実装に対する詳細なアーキテクチャ・動作仕様を記録した仕様書

## 🚀 ビルドと実行方法

### 必要な環境
- .NET SDK
- Windows OS (音声出力に NAudio の WaveOutEvent に依存しているため)
- 実行環境に応じたFFmpegバイナリ (`ffmpeg` フォルダ下などに配置)

### 手順
リポジトリ直下にあるバッチファイルからビルド及びパッケージ化（配布用）が可能です。
- `build_release.bat`: リリースモードでアプリケーションをビルドします。
- `package_dist.bat`: ビルド済みのバイナリをまとめて配布用の構成にパッケージングします。

開発時に手動で実行する場合は `FFmPlayer` ディレクトリに移動し、以下のコマンドを実行します：
```bash
cd FFmPlayer
dotnet run
```

## ⚙️ アプリケーション設定 (Settings)
アプリ起動時や終了時に `settings.json` （または設定画面から即時）へ自動保存されます。
- UIのテーマ変更や再生スピードの設定
- パフォーマンス関連（メモリ制限、フレームバッファサイズ、A/V同期閾値など）の設定
- ショートカットキーの割り当て等

内部挙動のさらに詳細な仕様（状態遷移やキャッシュアルゴリズム等）については、リポジトリ内の `spec.md` をご確認ください。

using Avalonia;
using System;

namespace FFmPlayer;

class Program
{
    /// <summary>
    /// プログラムのメインエントリポイントです。Avaloniaアプリの設定と起動を行います。
    /// （Avaloniaが初期化される前なので、ここでUI関連のAPIは使用できません）
    /// </summary>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Avaloniaアプリケーションの設定ビルダーです。プラットフォーム検出やフォント設定を行います。
    /// （UIデザイナーからも呼び出されるため、削除・変更は慎重に行ってください）
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using FFmPlayer.Services;
using FFmPlayer.ViewModels;
using FFmpeg.AutoGen;

namespace FFmPlayer;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;

    /// <summary>
    /// AvalonUI アプリケーションの初期化エントリポイントです。
    /// XAML の読み込みと、FFmpegライブラリのルートパス設定を行います。
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        if (OperatingSystem.IsMacOS())
        {
            if (System.IO.Directory.Exists("/opt/homebrew/lib"))
            {
                ffmpeg.RootPath = "/opt/homebrew/lib";
                System.Console.WriteLine($"ffmpeg.RootPath:homebrew: {ffmpeg.RootPath}");

            }
            else if (System.IO.Directory.Exists("/usr/local/lib"))
            {
                ffmpeg.RootPath = "/usr/local/lib";
                System.Console.WriteLine($"ffmpeg.RootPath:usr/local: {ffmpeg.RootPath}");
            }
            else
            {
                ffmpeg.RootPath = AppContext.BaseDirectory;
                System.Console.WriteLine($"ffmpeg.RootPath:base: {ffmpeg.RootPath}");
            }

            System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(typeof(SDL2.SDL).Assembly, (libraryName, assembly, searchPath) =>
            {
                if (libraryName == "SDL2" || libraryName == "SDL2.dll")
                {
                    if (System.IO.File.Exists("/opt/homebrew/lib/libSDL2.dylib"))
                        return System.Runtime.InteropServices.NativeLibrary.Load("/opt/homebrew/lib/libSDL2.dylib", assembly, searchPath);
                    if (System.IO.File.Exists("/usr/local/lib/libSDL2.dylib"))
                        return System.Runtime.InteropServices.NativeLibrary.Load("/usr/local/lib/libSDL2.dylib", assembly, searchPath);
                }
                return System.IntPtr.Zero;
            });
        }
        else
        {
            ffmpeg.RootPath = AppContext.BaseDirectory;
        }
    }

    /// <summary>
    /// UIフレームワークの初期化が完了した直後に呼ばれます。
    /// 設定の読み込みと、MainViewModel および MainWindow の生成・紐付けを行います。
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settingsService = new SettingsService();
            var settings = settingsService.Load();
            
            _mainViewModel = new MainViewModel(settingsService, settings);

            desktop.MainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };

            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;

            desktop.Exit += (s, e) =>
            {
                _mainViewModel.SaveSettings();
                _mainViewModel.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
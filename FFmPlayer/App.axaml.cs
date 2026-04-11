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
        ffmpeg.RootPath = AppContext.BaseDirectory;
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

            desktop.Exit += (s, e) =>
            {
                _mainViewModel.SaveSettings();
                _mainViewModel.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
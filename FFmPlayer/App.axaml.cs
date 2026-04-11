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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ffmpeg.RootPath = AppContext.BaseDirectory;
    }

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
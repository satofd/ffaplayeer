using System;
using System.Text.Json.Serialization;
using Avalonia.Input;

namespace FFmPlayer.Models;

public enum PlaybackMode
{
    Sequential,
    Random,
    RepeatOne
}

public class AppSettings
{
    public double Volume { get; set; } = 1.0;
    public bool IsMuted { get; set; } = false;
    public PlaybackMode PlaybackMode { get; set; } = PlaybackMode.Sequential;
    public double PlaybackSpeed { get; set; } = 1.0;
    public string Theme { get; set; } = "Default";
    public bool AlwaysOnTop { get; set; } = false;
    public int TimeDisplayMode { get; set; } = 0;
    public bool MemoryLimitEnabled { get; set; } = true;
    public int MemoryLimitMB { get; set; } = 2048;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 720;
    public int FrameBufferSize { get; set; } = 120;

    private double _stepScanWindowForwardSeconds = 5.0;
    public double StepScanWindowForwardSeconds
    {
        get => _stepScanWindowForwardSeconds;
        set => _stepScanWindowForwardSeconds = Math.Clamp(value, 1.0, 20.0);
    }

    private double _stepScanWindowBackwardSeconds = 8.0;
    public double StepScanWindowBackwardSeconds
    {
        get => _stepScanWindowBackwardSeconds;
        set => _stepScanWindowBackwardSeconds = Math.Clamp(value, 1.0, 20.0);
    }

    private double _videoLeadSleepThresholdSeconds = 0.010;
    public double VideoLeadSleepThresholdSeconds
    {
        get => _videoLeadSleepThresholdSeconds;
        set => _videoLeadSleepThresholdSeconds = Math.Clamp(value, 0.001, 0.200);
    }

    private double _videoDropLagThresholdSeconds = 0.050;
    public double VideoDropLagThresholdSeconds
    {
        get => _videoDropLagThresholdSeconds;
        set => _videoDropLagThresholdSeconds = Math.Clamp(value, 0.005, 0.500);
    }

    private int _avSyncOsdMode = 0;
    public int AvSyncOsdMode
    {
        get => _avSyncOsdMode;
        set => _avSyncOsdMode = Math.Clamp(value, 0, 2);
    }

    // Shortcuts
    public string ShortcutSeekForward1s { get; set; } = "Right";
    public string ShortcutSeekBackward1s { get; set; } = "Left";
    public string ShortcutSeekForward10s { get; set; } = "Shift+Right";
    public string ShortcutSeekBackward10s { get; set; } = "Shift+Left";
    public string ShortcutSeekForward60s { get; set; } = "Ctrl+Right";
    public string ShortcutSeekBackward60s { get; set; } = "Ctrl+Left";
    public string ShortcutPlayPause { get; set; } = "Space";
    public string ShortcutStop { get; set; } = "S";
    public string ShortcutStepForward { get; set; } = "OemPeriod";
    public string ShortcutStepBackward { get; set; } = "OemComma";
    public string ShortcutToggleMute { get; set; } = "M";
    public string ShortcutToggleFullscreen { get; set; } = "F";
    public string ShortcutExitFullscreen { get; set; } = "Escape";
    public string ShortcutOpenFile { get; set; } = "Ctrl+O";
    public string ShortcutOpenUrl { get; set; } = "Ctrl+U";
    public string ShortcutShowPlaylist { get; set; } = "L";
    public string ShortcutShowMediaInfo { get; set; } = "I";
    public string ShortcutIncreaseSpeed { get; set; } = "OemPlus";
    public string ShortcutIncreaseSpeedAlt { get; set; } = "Add";
    public string ShortcutDecreaseSpeed { get; set; } = "OemMinus";
    public string ShortcutDecreaseSpeedAlt { get; set; } = "Subtract";
    public string ShortcutResetSpeed { get; set; } = "D0";
    public string ShortcutSetAbStart { get; set; } = "A";
    public string ShortcutSetAbEnd { get; set; } = "B";
    public string ShortcutCycleTimeDisplay { get; set; } = "T";
    public string ShortcutTakeScreenshot { get; set; } = "PrintScreen";
}

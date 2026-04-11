using System;
using System.IO;

namespace FFmPlayer.Services;

public static class Logger
{
    private static readonly string LogFilePath = Path.Combine(AppContext.BaseDirectory, "ffmplayer.log");
    private static readonly object SyncLock = new object();

    public static void Info(string message)
    {
        WriteLog("INFO", message);
    }

    public static void Error(string message, Exception? ex = null)
    {
        var msg = ex != null ? $"{message} | Exception: {ex}" : message;
        WriteLog("ERROR", msg);
    }

    private static void WriteLog(string level, string message)
    {
        lock (SyncLock)
        {
            try
            {
                File.AppendAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Ignore log write errors to not crash the app
            }
        }
    }
}

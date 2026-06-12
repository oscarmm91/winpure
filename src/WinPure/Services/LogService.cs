using System.IO;

namespace WinPure.Services;

public static class LogService
{
    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinPure", "Logs");

    private static readonly object Gate = new();
    private static string? _sessionFile;

    public static void Log(string message)
    {
        lock (Gate)
        {
            try
            {
                if (_sessionFile is null)
                {
                    Directory.CreateDirectory(LogDirectory);
                    _sessionFile = Path.Combine(LogDirectory, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                }
                File.AppendAllText(_sessionFile, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
                // logging must never break the app
            }
        }
    }
}

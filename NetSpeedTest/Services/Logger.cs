using System;
using System.IO;

namespace NetSpeedTest.Services;

public static class Logger
{
    private static bool _enabled;
    private static string? _path;
    private static readonly object _lock = new();

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (_enabled && _path == null)
                _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
        }
    }

    public static void Log(string msg)
    {
        if (!_enabled || _path == null) return;
        lock (_lock)
        {
            try { File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); }
            catch { System.Diagnostics.Debug.WriteLine($"Logger write failed: {msg}"); }
        }
    }
}

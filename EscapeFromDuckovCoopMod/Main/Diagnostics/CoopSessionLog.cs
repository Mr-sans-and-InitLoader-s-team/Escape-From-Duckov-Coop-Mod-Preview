using System;
using System.IO;
using System.Text;

namespace EscapeFromDuckovCoopMod;

internal static class CoopSessionLog
{
    private static readonly object Sync = new();
    private static string _path;
    private static string _lastCategory, _lastMessage;
    private static DateTime _lastWrite;
    private static int _repeated;

    internal static void Start(string directory, string version)
    {
        lock (Sync)
        {
            if (_path != null) return;
            try
            {
                Directory.CreateDirectory(directory);
                var latest = Path.Combine(directory, "latest.log");
                var previous = Path.Combine(directory, "previous.log");
                if (File.Exists(latest))
                {
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(latest, previous);
                }
                File.WriteAllText(latest, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Session] Co-op mod {version}{Environment.NewLine}", new UTF8Encoding(false));
                _path = latest;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
    }

    internal static void Write(string category, string message)
    {
        lock (Sync)
        {
            if (_path == null) return;
            try
            {
                var now = DateTime.Now;
                if (category == _lastCategory && message == _lastMessage && now - _lastWrite < TimeSpan.FromSeconds(5))
                {
                    _repeated++;
                    return;
                }
                var repeated = _repeated > 0 ? $"[{now:yyyy-MM-dd HH:mm:ss.fff}] [Repeated] Previous entry repeated {_repeated} times{Environment.NewLine}" : string.Empty;
                File.AppendAllText(_path, repeated + $"[{now:yyyy-MM-dd HH:mm:ss.fff}] [{category}] {message}{Environment.NewLine}", new UTF8Encoding(false));
                _lastCategory = category;
                _lastMessage = message;
                _lastWrite = now;
                _repeated = 0;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
    }
}

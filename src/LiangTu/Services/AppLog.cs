namespace LiangTu.Services;

public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _file;

    public static void Init()
    {
        AppPaths.EnsureCreated();
        _file = Path.Combine(AppPaths.Logs, $"liangtu-{DateTime.Now:yyyyMMdd}.log");
        Info("STARTED 晾图 LiangTu");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  [{level}]  {message}";
        try
        {
            lock (Gate)
            {
                if (_file is null) return;
                File.AppendAllText(_file, line + Environment.NewLine);
            }
        }
        catch
        {
            // ignore logging failures
        }
    }
}

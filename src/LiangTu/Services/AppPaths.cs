namespace LiangTu.Services;

public static class AppPaths
{
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiangTu");

    public static string DefaultInbox => Path.Combine(Root, "Inbox");

    public static string Logs => Path.Combine(Root, "logs");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(DefaultInbox);
        Directory.CreateDirectory(Logs);
    }
}

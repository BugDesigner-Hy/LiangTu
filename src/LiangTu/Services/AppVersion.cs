using System.Reflection;

namespace LiangTu.Services;

public static class AppVersion
{
    public static string Display
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "1.2.1" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static string TrayText => $"晾图 v{Display}";
}

namespace TelegramMonitor;

public static class TelegramMonitorConstants
{
    public const string MonitorApi = "https://raw.githubusercontent.com/Riniba/TelegramMonitor/refs/heads/main/ad/ad.txxt";
    public const int ApiId = 6;
    public const string ApiHash = "eb06d4abfb49dc3eeb1aeb98ae0f581e";
    public static readonly string SessionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "session");
}

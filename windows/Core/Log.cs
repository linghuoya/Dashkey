namespace DashKey.Core;

/// <summary>极简文件日志，便于排查热键注册失败之类只能在目标机器上复现的问题。</summary>
internal static class Log
{
    private static readonly object Gate = new();

    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DashKey");

    public static string FilePath => Path.Combine(DataDir, "dashkey.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DataDir);

                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > 256 * 1024) file.Delete();

                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }
}

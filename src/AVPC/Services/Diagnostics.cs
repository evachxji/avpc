using System.Diagnostics;

namespace AVPC.Services;

public static class Diagnostics
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AVPC", "diagnostics.log");
    private static readonly object Sync = new();

    public static void Log(string message) => Write(message);

    public static void LogException(string context, Exception exception) =>
        Write($"{context}{Environment.NewLine}{exception}");

    private static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AVPC 日志写入失败：{ex}");
        }
    }
}

using NSCheatManager.Windows.Core;
using System.IO;
using System.Text;

namespace NSCheatManager.Windows.Services;

public static class AppLog
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly string SessionId = Guid.NewGuid().ToString("N")[..8];
    private static bool _initialized;
    public static string CurrentFile => Path.Combine(AppPaths.Logs, $"{DateTime.Now:yyyy-MM-dd}.log");

    public static async Task InitializeAsync()
    {
        AppPaths.Ensure();
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            _initialized = true;
            var version = typeof(AppLog).Assembly.GetName().Version?.ToString() ?? "unknown";
            var header = $"{DateTimeOffset.Now:O}\tINFO\tS={SessionId}\tPID={Environment.ProcessId}\tT={Environment.CurrentManagedThreadId}\t应用启动 | version={version} | os={Environment.OSVersion} | runtime={Environment.Version} | base={AppContext.BaseDirectory}{Environment.NewLine}";
            await File.AppendAllTextAsync(CurrentFile, header, new UTF8Encoding(false)).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    public static Task InfoAsync(string message) => WriteAsync("INFO", message);
    public static Task WarningAsync(string message, Exception? error = null) => WriteAsync("WARN", message, error);
    public static Task ErrorAsync(string message, Exception? error = null) => WriteAsync("ERROR", message, error);

    public static Task WriteAsync(string message, Exception? error = null) =>
        WriteAsync(error is null ? "INFO" : "ERROR", message, error);

    private static async Task WriteAsync(string level, string message, Exception? error = null)
    {
        if (!_initialized) await InitializeAsync().ConfigureAwait(false);
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var normalized = message.Replace("\r", " ").Replace("\n", " ");
            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O")).Append('\t')
                .Append(level).Append("\tS=").Append(SessionId)
                .Append("\tPID=").Append(Environment.ProcessId)
                .Append("\tT=").Append(Environment.CurrentManagedThreadId)
                .Append('\t').Append(normalized).AppendLine();
            if (error is not null)
            {
                foreach (var line in error.ToString().Replace("\r\n", "\n").Split('\n'))
                    builder.Append("    ").AppendLine(line);
            }
            await File.AppendAllTextAsync(CurrentFile, builder.ToString(), new UTF8Encoding(false)).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }
}

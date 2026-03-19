using System.IO;

namespace ZayFlow.App.Services.AI;

public interface IActionAuditService
{
    Task LogAsync(string category, string message, CancellationToken ct = default);
    Task<string> ReadRecentAsync(int maxLines = 200, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}

public sealed class ActionAuditService : IActionAuditService
{
    private readonly string _logPath;

    public ActionAuditService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZayFlow");
        Directory.CreateDirectory(appData);
        _logPath = Path.Combine(appData, "actions.log");
    }

    public async Task LogAsync(string category, string message, CancellationToken ct = default)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{category}] {message}{Environment.NewLine}";
        await File.AppendAllTextAsync(_logPath, line, ct);
    }

    public async Task<string> ReadRecentAsync(int maxLines = 200, CancellationToken ct = default)
    {
        if (!File.Exists(_logPath))
        {
            return "No logs available yet.";
        }

        var content = await File.ReadAllTextAsync(_logPath, ct);
        var lines = content
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(Math.Max(1, maxLines));

        return string.Join(Environment.NewLine, lines);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        File.WriteAllText(_logPath, string.Empty);
        return Task.CompletedTask;
    }
}
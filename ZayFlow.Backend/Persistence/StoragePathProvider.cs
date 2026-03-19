namespace ZayFlow.Backend.Persistence;

internal static class StoragePathProvider
{
    public static string Root
    {
        get
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZayFlow");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string TokensPath => Path.Combine(Root, "tokens.json");
    public static string QueuePath => Path.Combine(Root, "queue.json");
    public static string BackendStatePath => Path.Combine(Root, "backend-state.json");
    public static string AuditLogPath => Path.Combine(Root, "execution-audit.jsonl");
    public static string TaskMemoryPath => Path.Combine(Root, "task-memory.json");
}

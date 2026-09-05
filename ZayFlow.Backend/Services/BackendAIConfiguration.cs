using System.IO;
using System.Text.Json;

namespace ZayFlow.Backend.Services;

/// <summary>
/// Backend-side AI configuration. The OpenRouter API key and model selection
/// are controlled HERE, not in the frontend UI.
/// 
/// Configuration file location:
///   %LOCALAPPDATA%\ZayFlow\backend-config.json
/// 
/// Example content:
/// {
///   "OpenRouterApiKey": "sk-or-v1-YOUR_API_KEY_HERE",
///   "Model": "thudm/glm-4-plus",
///   "MaxTokensPerRequest": 8000,
///   "Temperature": 0.7
/// }
/// 
/// You can also set the API key via environment variable:
///   ZAYFLOW_OPENROUTER_API_KEY=sk-or-v1-YOUR_API_KEY_HERE
/// </summary>
public sealed class BackendAIConfig
{
    /// <summary>
    /// OpenRouter API key. Get one at https://openrouter.ai/keys
    /// </summary>
    public string OpenRouterApiKey { get; set; } = string.Empty;

    /// <summary>
    /// AI model to use. Default: thudm/glm-4-plus (GLM-4-Plus on OpenRouter).
    /// Code model: anthropic/claude-opus-4
    /// </summary>
    public string Model { get; set; } = "thudm/glm-4-plus";

    /// <summary>
    /// Max tokens per AI request.
    /// </summary>
    public int MaxTokensPerRequest { get; set; } = 8000;

    /// <summary>
    /// Temperature for AI responses (0.0 = deterministic, 1.0 = creative).
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Enable AI response speed mode (uses faster/smaller model when true).
    /// </summary>
    public bool SpeedMode { get; set; } = false;

    /// <summary>
    /// Speed mode model (smaller, faster).
    /// </summary>
    public string SpeedModeModel { get; set; } = "meta-llama/llama-3.3-70b-instruct:free";
}

/// <summary>
/// Loads and manages the backend AI configuration.
/// API key resolution order:
///   1. Environment variable: ZAYFLOW_GROQ_API_KEY
///   2. Config file: %LOCALAPPDATA%\ZayFlow\backend-config.json
///   3. Settings stored via the app preferences (fallback)
/// </summary>
public interface IBackendAIConfiguration
{
    BackendAIConfig GetConfig();
    void SaveConfig(BackendAIConfig config);
    string ResolveOpenRouterApiKey(string? fallbackFromPreferences = null);
}

public sealed class BackendAIConfigurationService : IBackendAIConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _configPath;
    private BackendAIConfig? _cached;
    private readonly object _lock = new();

    public BackendAIConfigurationService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZayFlow");
        Directory.CreateDirectory(appData);
        _configPath = Path.Combine(appData, "backend-config.json");

        // Create default config file if it doesn't exist
        if (!File.Exists(_configPath))
        {
            SaveConfig(new BackendAIConfig());
        }
    }

    public BackendAIConfig GetConfig()
    {
        lock (_lock)
        {
            if (_cached != null) return _cached;

            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    _cached = JsonSerializer.Deserialize<BackendAIConfig>(json, JsonOptions) ?? new BackendAIConfig();
                }
                else
                {
                    _cached = new BackendAIConfig();
                }
            }
            catch
            {
                _cached = new BackendAIConfig();
            }

            return _cached;
        }
    }

    public void SaveConfig(BackendAIConfig config)
    {
        lock (_lock)
        {
            _cached = config;
            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(_configPath, json);
        }
    }

    /// <summary>
    /// Resolves the OpenRouter API key using priority order:
    /// 1. Environment variable ZAYFLOW_OPENROUTER_API_KEY
    /// 2. Backend config file (always reloads from disk)
    /// 3. Fallback from app preferences (user-entered)
    /// </summary>
    public string ResolveOpenRouterApiKey(string? fallbackFromPreferences = null)
    {
        // Priority 1: Environment variable
        var envKey = Environment.GetEnvironmentVariable("ZAYFLOW_OPENROUTER_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
            return envKey;

        // Priority 2: Backend config file (force reload from disk, bypass cache)
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<BackendAIConfig>(json, JsonOptions);
                if (config != null && !string.IsNullOrWhiteSpace(config.OpenRouterApiKey))
                {
                    // Update cache with fresh data
                    lock (_lock) { _cached = config; }
                    return config.OpenRouterApiKey;
                }
            }
        }
        catch { /* Ignore read errors */ }

        // Priority 3: Fallback from preferences
        return fallbackFromPreferences ?? string.Empty;
    }
}

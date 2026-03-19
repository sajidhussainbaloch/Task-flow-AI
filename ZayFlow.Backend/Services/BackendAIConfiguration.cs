using System.IO;
using System.Text.Json;

namespace ZayFlow.Backend.Services;

/// <summary>
/// Backend-side AI configuration. The Groq API key and model selection
/// are controlled HERE, not in the frontend UI.
/// 
/// Configuration file location:
///   %LOCALAPPDATA%\ZayFlow\backend-config.json
/// 
/// Example content:
/// {
///   "GroqApiKey": "gsk_YOUR_API_KEY_HERE",
///   "Model": "llama-3.3-70b-versatile",
///   "MaxTokensPerRequest": 8000,
///   "Temperature": 0.7
/// }
/// 
/// You can also set the API key via environment variable:
///   ZAYFLOW_GROQ_API_KEY=gsk_YOUR_API_KEY_HERE
/// </summary>
public sealed class BackendAIConfig
{
    /// <summary>
    /// Groq API key. Get a free one at https://console.groq.com
    /// </summary>
    public string GroqApiKey { get; set; } = string.Empty;

    /// <summary>
    /// AI model to use. Default: llama-3.3-70b-versatile (free on Groq).
    /// Other options: llama-3.1-8b-instant, mixtral-8x7b-32768
    /// </summary>
    public string Model { get; set; } = "llama-3.3-70b-versatile";

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
    public string SpeedModeModel { get; set; } = "llama-3.1-8b-instant";
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
    string ResolveGroqApiKey(string? fallbackFromPreferences = null);
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
    /// Resolves the Groq API key using priority order:
    /// 1. Environment variable ZAYFLOW_GROQ_API_KEY
    /// 2. Backend config file (always reloads from disk)
    /// 3. Fallback from app preferences (user-entered)
    /// </summary>
    public string ResolveGroqApiKey(string? fallbackFromPreferences = null)
    {
        // Priority 1: Environment variable
        var envKey = Environment.GetEnvironmentVariable("ZAYFLOW_GROQ_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
            return envKey;

        // Priority 2: Backend config file (force reload from disk, bypass cache)
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<BackendAIConfig>(json, JsonOptions);
                if (config != null && !string.IsNullOrWhiteSpace(config.GroqApiKey))
                {
                    // Update cache with fresh data
                    lock (_lock) { _cached = config; }
                    return config.GroqApiKey;
                }
            }
        }
        catch { /* Ignore read errors */ }

        // Priority 3: Fallback from preferences
        return fallbackFromPreferences ?? string.Empty;
    }
}

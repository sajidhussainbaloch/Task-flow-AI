using System.IO;
using System.Text.Json;

namespace ZayFlow.Backend.Services;

/// <summary>
/// Backend-side AI configuration for Cloudflare Workers AI.
///
/// Configuration file location:
///   %LOCALAPPDATA%\ZayFlow\backend-config.json
///
/// Example content:
/// {
///   "CloudflareApiToken": "cfut_YOUR_TOKEN",
///   "CloudflareAccountId": "your_account_id",
///   "Model": "@cf/meta/llama-3.3-70b-instruct-fp8-fast",
///   "MaxTokensPerRequest": 8000,
///   "Temperature": 0.2
/// }
/// </summary>
public sealed class BackendAIConfig
{
    public string CloudflareApiToken { get; set; } = string.Empty;
    public string CloudflareAccountId { get; set; } = string.Empty;
    public string Model { get; set; } = "@cf/meta/llama-3.3-70b-instruct-fp8-fast";
    public int MaxTokensPerRequest { get; set; } = 8000;
    public double Temperature { get; set; } = 0.2;
    public bool SpeedMode { get; set; } = false;
    public string SpeedModeModel { get; set; } = "@cf/meta/llama-3.1-8b-instruct";
}

public interface IBackendAIConfiguration
{
    BackendAIConfig GetConfig();
    void SaveConfig(BackendAIConfig config);
    string ResolveCloudflareApiToken(string? fallbackFromPreferences = null);
    string ResolveCloudflareAccountId(string? fallbackFromPreferences = null);
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

        if (!File.Exists(_configPath))
        {
            SaveConfig(new BackendAIConfig());
        }
    }

    public BackendAIConfig GetConfig()
    {
        lock (_lock)
        {
            if (_cached != null)
            {
                return _cached;
            }

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

    public string ResolveCloudflareApiToken(string? fallbackFromPreferences = null)
    {
        var envValue = Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue;
        }

        envValue = Environment.GetEnvironmentVariable("ZAYFLOW_CLOUDFLARE_API_TOKEN");
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue;
        }

        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<BackendAIConfig>(json, JsonOptions);
                if (config != null && !string.IsNullOrWhiteSpace(config.CloudflareApiToken))
                {
                    lock (_lock)
                    {
                        _cached = config;
                    }

                    return config.CloudflareApiToken;
                }
            }
        }
        catch
        {
            // Ignore read errors and fall back to preferences.
        }

        return fallbackFromPreferences ?? string.Empty;
    }

    public string ResolveCloudflareAccountId(string? fallbackFromPreferences = null)
    {
        var envValue = Environment.GetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID");
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue;
        }

        envValue = Environment.GetEnvironmentVariable("ZAYFLOW_CLOUDFLARE_ACCOUNT_ID");
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue;
        }

        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<BackendAIConfig>(json, JsonOptions);
                if (config != null && !string.IsNullOrWhiteSpace(config.CloudflareAccountId))
                {
                    lock (_lock)
                    {
                        _cached = config;
                    }

                    return config.CloudflareAccountId;
                }
            }
        }
        catch
        {
            // Ignore read errors and fall back to preferences.
        }

        return fallbackFromPreferences ?? string.Empty;
    }
}

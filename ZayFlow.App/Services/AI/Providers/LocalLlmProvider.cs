using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Contracts;

namespace ZayFlow.App.Services.AI.Providers;

/// <summary>
/// Stub implementation for local LLM provider. Supports Ollama, LM Studio, etc.
/// </summary>
public class LocalLlmProvider : IAIProvider
{
    public string ProviderName => "Local LLM";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiUrl) && !string.IsNullOrWhiteSpace(_modelName);

    private readonly HttpClient _httpClient;
    private string? _apiUrl;
    private string? _modelName;
    private readonly ILogger<LocalLlmProvider> _logger;

    public LocalLlmProvider(HttpClient httpClient, ILogger<LocalLlmProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public void Initialize(string configKey)
    {
        var endpoint = "http://localhost:11434";
        var model = "llama3";

        if (!string.IsNullOrWhiteSpace(configKey) && configKey.Contains('|'))
        {
            var parts = configKey.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
            {
                endpoint = parts[0];
            }

            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                model = parts[1];
            }
        }

        _apiUrl = endpoint.TrimEnd('/');
        _modelName = model;
        _logger.LogInformation("Local LLM initialized with model {Model} at {Endpoint}", _modelName, _apiUrl);
    }

    public async Task<AIResponse> SendChatMessageAsync(string userMessage, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new AIResponse
            {
                Success = false,
                Message = "Local LLM not configured",
                Intent = "chat"
            };

        try
        {
            var prompt = BuildPrompt(userMessage);
            var requestBody = JsonSerializer.Serialize(new
            {
                model = _modelName,
                prompt,
                stream = false
            });

            using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync($"{_apiUrl}/api/generate", content, ct);

            if (!response.IsSuccessStatusCode)
            {
                return new AIResponse
                {
                    Success = false,
                    Message = $"Local LLM error: {response.StatusCode}",
                    Intent = "chat"
                };
            }

            var payload = await response.Content.ReadAsStringAsync(ct);
            var json = JsonSerializer.Deserialize<JsonElement>(payload);
            var modelText = json.TryGetProperty("response", out var responseText)
                ? responseText.GetString() ?? string.Empty
                : string.Empty;

            return ParseAiResponse(modelText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local LLM chat failed");
            return new AIResponse
            {
                Success = false,
                Message = $"Local provider failed: {ex.Message}",
                Intent = "chat"
            };
        }
    }

    public async Task<string> SummarizeFileAsync(string fileName, string fileContent, CancellationToken ct = default)
    {
        var prompt = $"Summarize this file and provide key points, simplified version, topic, and notes. File: {fileName}\nContent:\n{fileContent}";
        var response = await SendChatMessageAsync(prompt, ct);
        return response.Message;
    }

    public async Task<string> GenerateTextAsync(string prompt, CancellationToken ct = default)
    {
        var response = await SendChatMessageAsync(prompt, ct);
        return response.Message;
    }

    private static string BuildPrompt(string userMessage)
    {
                return """
You are TaskFlow AI local assistant.
Reply ONLY with JSON in this shape:
{
    "intent": "chat|rename_files|move_files|delete_files|create_folder|change_wallpaper|clean_temp|get_disk_info|read_file|organize_folder|detect_duplicates|open_application|folder_insights",
    "message": "human friendly reply",
    "parameters": {},
    "confirmationMessage": "preview of what will execute",
    "requiresConfirmation": true,
    "tokenCost": 1
}

User message:
""" + userMessage;
    }

    private static AIResponse ParseAiResponse(string modelOutput)
    {
        if (string.IsNullOrWhiteSpace(modelOutput))
        {
            return new AIResponse
            {
                Success = false,
                Message = "Local model returned empty output",
                Intent = "chat"
            };
        }

        try
        {
            var trimmed = modelOutput.Trim();
            if (trimmed.Contains("```json"))
            {
                trimmed = trimmed.Split("```json", StringSplitOptions.None)[1].Split("```", StringSplitOptions.None)[0];
            }
            else if (trimmed.Contains("```"))
            {
                trimmed = trimmed.Split("```", StringSplitOptions.None)[1].Split("```", StringSplitOptions.None)[0];
            }

            var parsed = JsonSerializer.Deserialize<JsonElement>(trimmed);
            var response = new AIResponse
            {
                Success = true,
                Message = parsed.TryGetProperty("message", out var message)
                    ? message.GetString() ?? string.Empty
                    : modelOutput,
                Intent = parsed.TryGetProperty("intent", out var intent)
                    ? intent.GetString() ?? "chat"
                    : "chat",
                ConfirmationMessage = parsed.TryGetProperty("confirmationMessage", out var confirm)
                    ? confirm.GetString() ?? string.Empty
                    : string.Empty,
                RequiresConfirmation = parsed.TryGetProperty("requiresConfirmation", out var requires)
                    ? requires.GetBoolean()
                    : false,
                TokenCost = parsed.TryGetProperty("tokenCost", out var token)
                    ? token.GetInt32()
                    : 1
            };

            if (parsed.TryGetProperty("parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in parameters.EnumerateObject())
                {
                    response.Parameters[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                        JsonValueKind.Number => prop.Value.TryGetInt64(out var n) ? n : prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => prop.Value.GetRawText()
                    };
                }
            }

            return response;
        }
        catch
        {
            return new AIResponse
            {
                Success = true,
                Message = modelOutput,
                Intent = "chat",
                TokenCost = 1
            };
        }
    }
}

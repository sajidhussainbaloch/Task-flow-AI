using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Contracts;

namespace ZayFlow.App.Services.AI.Providers;

/// <summary>
/// DeepSeek R1 API provider.
/// Sends requests to DeepSeek's REST API endpoint.
/// </summary>
public class DeepSeekProvider : IAIProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DeepSeekProvider> _logger;
    private string? _apiKey;

    public string ProviderName => "DeepSeek R1";
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public DeepSeekProvider(HttpClient httpClient, ILogger<DeepSeekProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public void Initialize(string apiKey)
    {
        _apiKey = apiKey;
        _logger.LogInformation("DeepSeekProvider initialized");
    }

    public async Task<AIResponse> SendChatMessageAsync(string userMessage, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new AIResponse
            {
                Success = false,
                Message = "DeepSeek API key not configured"
            };
        }

        try
        {
            var request = new DeepSeekRequest
            {
                Model = "deepseek-reasoner",
                Messages = new[]
                {
                    new Message
                    {
                        Role = "user",
                        Content = BuildSystemPrompt(userMessage)
                    }
                },
                Temperature = 1,
                MaxTokens = 16000
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.PostAsync(
                "https://api.deepseek.com/chat/completions",
                content,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"DeepSeek API error: {response.StatusCode}");
                return new AIResponse
                {
                    Success = false,
                    Message = $"API Error: {response.StatusCode}"
                };
            }

            var responseText = await response.Content.ReadAsStringAsync(ct);
            var deepSeekResponse = JsonSerializer.Deserialize<DeepSeekResponse>(responseText);

            if (deepSeekResponse?.Choices == null || deepSeekResponse.Choices.Count == 0)
            {
                return new AIResponse
                {
                    Success = false,
                    Message = "Empty response from DeepSeek"
                };
            }

            var assistantMessage = deepSeekResponse.Choices[0].Message.Content;
            return ParseAIResponse(assistantMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError($"DeepSeek error: {ex.Message}");
            return new AIResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    public async Task<string> SummarizeFileAsync(string fileName, string fileContent, CancellationToken ct = default)
    {
        var prompt = $"""
You are a professional document analyst.
Return concise markdown with these sections:
- Summary
- Key points
- Simplified version
- Topic
- Actionable notes

File: {fileName}
Content:
{fileContent}
""";

        var response = await SendChatMessageAsync(prompt, ct);
        return response.Message;
    }

    public async Task<string> GenerateTextAsync(string prompt, CancellationToken ct = default)
    {
        var response = await SendChatMessageAsync(prompt, ct);
        return response.Message;
    }

    private string BuildSystemPrompt(string userMessage)
    {
        return $@"You are an AI assistant for file management and system tasks.

IMPORTANT: Always respond with a JSON object in this format:
{{
  ""intent"": ""rename_files|move_files|delete_files|create_folder|change_wallpaper|clean_temp|get_disk_info|read_file|chat"",
  ""message"": ""User-friendly response"",
  ""parameters"": {{
    ""folder"": ""path/to/folder"",
    ""pattern"": ""new_name_pattern"",
    ""filePath"": ""path/to/file""
  }},
  ""confirmationMessage"": ""What will happen"",
  ""requiresConfirmation"": true,
  ""tokenCost"": 1
}}

For 'chat' intent, just have message and parameters empty.

User message: {userMessage}";
    }

    private AIResponse ParseAIResponse(string jsonResponse)
    {
        try
        {
            // Extract JSON from markdown code blocks if present
            var jsonText = jsonResponse;
            if (jsonText.Contains("```json"))
            {
                jsonText = jsonText.Split("```json")[1].Split("```")[0];
            }
            else if (jsonText.Contains("```"))
            {
                jsonText = jsonText.Split("```")[1].Split("```")[0];
            }

            var parsed = JsonSerializer.Deserialize<JsonElement>(jsonText);
            
            return new AIResponse
            {
                Success = true,
                Message = parsed.GetProperty("message").GetString() ?? "",
                Intent = parsed.GetProperty("intent").GetString() ?? "chat",
                ConfirmationMessage = parsed.TryGetProperty("confirmationMessage", out var confirm)
                    ? confirm.GetString() ?? ""
                    : "",
                RequiresConfirmation = parsed.TryGetProperty("requiresConfirmation", out var req)
                    ? req.GetBoolean()
                    : false,
                Parameters = ParseParameters(parsed.GetProperty("parameters")),
                TokenCost = parsed.TryGetProperty("tokenCost", out var cost)
                    ? cost.GetInt32()
                    : 1
            };
        }
        catch (Exception)
        {
            return new AIResponse
            {
                Success = true,
                Message = jsonResponse,
                Intent = "chat",
                TokenCost = 1
            };
        }
    }

    private Dictionary<string, object> ParseParameters(JsonElement paramsElement)
    {
        var result = new Dictionary<string, object>();
        foreach (var prop in paramsElement.EnumerateObject())
        {
            result[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString() ?? "",
                JsonValueKind.Number => prop.Value.GetInt32(),
                _ => prop.Value.GetRawText()
            };
        }
        return result;
    }
}

// DeepSeek API request/response models
internal class DeepSeekRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "deepseek-reasoner";

    [JsonPropertyName("messages")]
    public Message[] Messages { get; set; } = Array.Empty<Message>();

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 1;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 16000;
}

internal class Message
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

internal class DeepSeekResponse
{
    [JsonPropertyName("choices")]
    public List<Choice> Choices { get; set; } = new();
}

internal class Choice
{
    [JsonPropertyName("message")]
    public Message Message { get; set; } = new();
}

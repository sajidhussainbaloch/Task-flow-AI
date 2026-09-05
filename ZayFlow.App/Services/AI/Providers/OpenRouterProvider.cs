using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Contracts;
using ZayFlow.App.Services.Assistant;

namespace ZayFlow.App.Services.AI.Providers;

/// <summary>
/// OpenRouter API provider — routes to multiple AI models.
/// Chat: GLM-4-Plus (thudm/glm-4-plus)  |  Code: Claude Opus 4 (anthropic/claude-opus-4)
/// Uses OpenAI-compatible API format via https://openrouter.ai/api/v1
/// </summary>
public class OpenRouterProvider : IAIProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenRouterProvider> _logger;
    private string? _apiKey;
    private string _model = DefaultChatModel;
    private readonly List<OpenRouterMessage> _conversationHistory = new();
    private const int MaxHistoryMessages = 30;
    private const string ChatCompletionsUrl = "https://openrouter.ai/api/v1/chat/completions";
    private const string ImageGenerationsUrl = "https://openrouter.ai/api/v1/images/generations";

    /// <summary>Default model for general chat / desktop actions (GLM-4-Plus).</summary>
    public const string DefaultChatModel = "thudm/glm-4-plus";

    /// <summary>Default model for code generation, review, and refactoring (Claude Opus 4).</summary>
    public const string DefaultCodeModel = "anthropic/claude-opus-4";

    /// <summary>Default vision-capable model.</summary>
    public const string DefaultVisionModel = "anthropic/claude-opus-4";

    /// <summary>
    /// Available models on OpenRouter.
    /// </summary>
    public static readonly Dictionary<string, string> AvailableModels = new()
    {
        ["GLM-4-Plus (Chat)"] = "thudm/glm-4-plus",
        ["Claude Opus 4 (Code)"] = "anthropic/claude-opus-4",
        ["Llama 3.3 70B"] = "meta-llama/llama-3.3-70b-instruct:free",
        ["Gemma 3 27B"] = "google/gemma-3-27b-it:free",
        ["Mistral Small 24B"] = "mistralai/mistral-small-3.1-24b-instruct:free",
        ["Qwen 3 Coder"] = "qwen/qwen3-coder:free",
    };

    public string ProviderName => "OpenRouter";
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
    public string CurrentModel => _model;

    public OpenRouterProvider(HttpClient httpClient, ILogger<OpenRouterProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Initialize with API key. Format: "apiKey" or "apiKey|modelId"
    /// </summary>
    public void Initialize(string config)
    {
        if (string.IsNullOrWhiteSpace(config)) return;

        if (config.Contains('|'))
        {
            var parts = config.Split('|', 2);
            _apiKey = parts[0].Trim();
            if (!string.IsNullOrWhiteSpace(parts[1]))
            {
                _model = parts[1].Trim();
            }
        }
        else
        {
            _apiKey = config.Trim();
        }

        _logger.LogInformation($"OpenRouterProvider initialized with model: {_model}");
    }

    /// <summary>
    /// Switch the active model.
    /// </summary>
    public void SetModel(string modelId)
    {
        _model = modelId;
        _logger.LogInformation($"OpenRouter model switched to: {modelId}");
    }

    /// <summary>
    /// Clears conversation history for a fresh session.
    /// </summary>
    public void ClearConversationHistory()
    {
        _conversationHistory.Clear();
        _logger.LogInformation("OpenRouter conversation history cleared");
    }

    /// <summary>
    /// Add a system-level note to conversation memory.
    /// </summary>
    public void AddNote(string note)
    {
        _conversationHistory.Add(new OpenRouterMessage
        {
            Role = "user",
            Content = $"[SYSTEM NOTE - do not repeat this to the user, just remember it]: {note}"
        });

        while (_conversationHistory.Count > MaxHistoryMessages)
            _conversationHistory.RemoveAt(0);
    }

    public async Task<AIResponse> SendChatMessageAsync(string userMessage, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new AIResponse
            {
                Success = false,
                Message = "OpenRouter API key not configured. Get a free key at https://openrouter.ai/keys and enter it in Settings."
            };
        }

        try
        {
            _conversationHistory.Add(new OpenRouterMessage { Role = "user", Content = userMessage });

            while (_conversationHistory.Count > MaxHistoryMessages)
                _conversationHistory.RemoveAt(0);

            var messages = new List<OpenRouterMessage>
            {
                new OpenRouterMessage { Role = "system", Content = BuildSystemPrompt() }
            };
            messages.AddRange(_conversationHistory);

            var request = new OpenRouterRequest
            {
                Model = _model,
                Messages = messages.ToArray(),
                Temperature = 0.7,
                MaxTokens = 16000,
                TopP = 1,
                Stream = false
            };

            var json = JsonSerializer.Serialize(request);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsUrl);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            httpRequest.Headers.Add("HTTP-Referer", "https://zayflow.app");
            httpRequest.Headers.Add("X-Title", "ZayFlow AI");

            var response = await _httpClient.SendAsync(httpRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError($"OpenRouter API error {response.StatusCode}: {error}");
                
                // Try to extract the actual error message from JSON response
                var errorMsg = $"API Error {(int)response.StatusCode}";
                try
                {
                    var errJson = JsonSerializer.Deserialize<JsonElement>(error);
                    if (errJson.TryGetProperty("error", out var errObj))
                    {
                        if (errObj.TryGetProperty("message", out var msg))
                            errorMsg = msg.GetString() ?? errorMsg;
                        else if (errObj.ValueKind == JsonValueKind.String)
                            errorMsg = errObj.GetString() ?? errorMsg;
                    }
                }
                catch { }

                return new AIResponse
                {
                    Success = false,
                    Message = $"{errorMsg}\n\nModel: {_model}\nGet key at: https://openrouter.ai/keys"
                };
            }

            var responseText = await response.Content.ReadAsStringAsync(ct);
            var orResponse = JsonSerializer.Deserialize<OpenRouterResponse>(responseText);

            if (orResponse?.Choices == null || orResponse.Choices.Count == 0)
            {
                return new AIResponse
                {
                    Success = false,
                    Message = "Empty response from OpenRouter"
                };
            }

            var assistantMessage = orResponse.Choices[0].Message.Content;

            _conversationHistory.Add(new OpenRouterMessage { Role = "assistant", Content = assistantMessage });

            return ParseAIResponse(assistantMessage);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError($"OpenRouter network error: {ex.Message}");
            return new AIResponse
            {
                Success = false,
                Message = "Network error. Check internet connection."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"OpenRouter error: {ex.Message}");
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

    /// <summary>
    /// Sends a structured turn with system/user prompts, model routing, and optional vision attachments.
    /// Used by code generation, code review, refactoring, planning, and all advanced AI services.
    /// </summary>
    public async Task<AssistantTurnResult> SendStructuredTurnAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        IReadOnlyList<AssistantAttachment> attachments,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new AssistantTurnResult
            {
                Message = "OpenRouter API key not configured. Add your key in Settings.",
                Intent = "chat"
            };
        }

        try
        {
            var messages = new List<StructuredMsg>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = BuildUserContent(userPrompt, attachments) }
            };

            // Vision requests may not support json_object response format
            var isVisionModel = attachments.Count > 0;
            var promptChars = (systemPrompt?.Length ?? 0) + EstimateContentLength(messages[1].Content);
            var estimatedPromptTokens = promptChars / 4;
            var maxOutputTokens = isVisionModel ? 8192 : Math.Min(16000, Math.Max(2048, 12000 - estimatedPromptTokens));

            var request = new StructuredRequest
            {
                Model = model,
                Messages = messages,
                Temperature = 0.2,
                MaxTokens = maxOutputTokens,
                TopP = 1,
                Stream = false,
                ResponseFormat = isVisionModel ? null : new ResponseFormatSpec { Type = "json_object" }
            };

            var responseText = await SendStructuredRequestAsync(request, ct).ConfigureAwait(false);
            var apiResponse = JsonSerializer.Deserialize<OpenRouterResponse>(responseText);
            if (apiResponse?.Choices == null || apiResponse.Choices.Count == 0)
            {
                return new AssistantTurnResult { Message = "Empty response from OpenRouter API.", Intent = "chat" };
            }
            var innerContent = apiResponse.Choices[0].Message.Content;
            // Vision models may return raw text instead of JSON — wrap it
            if (attachments.Count > 0 && !innerContent.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                return new AssistantTurnResult
                {
                    Message = innerContent,
                    Intent = "chat",
                    Mode = AssistantTurnMode.Vision
                };
            }
            return ParseAssistantTurnResult(innerContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Structured OpenRouter request failed");
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Generate an image using OpenRouter's image generation endpoint.
    /// </summary>
    public async Task<ImageGenerationResult> GenerateImageAsync(string prompt, string savePath, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new ImageGenerationResult { Success = false, Message = "OpenRouter API key not configured." };
        }

        try
        {
            var requestBody = new
            {
                model = "openai/dall-e-3",
                prompt = prompt,
                n = 1,
                size = "1024x1024"
            };

            var json = JsonSerializer.Serialize(requestBody);

            using var reqMsg = new HttpRequestMessage(HttpMethod.Post, ImageGenerationsUrl);
            reqMsg.Content = new StringContent(json, Encoding.UTF8, "application/json");
            reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            reqMsg.Headers.Add("HTTP-Referer", "https://zayflow.app");
            reqMsg.Headers.Add("X-Title", "ZayFlow AI");

            var response = await _httpClient.SendAsync(reqMsg, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return new ImageGenerationResult { Success = false, Message = $"Image generation failed: {error}" };
            }

            var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = JsonSerializer.Deserialize<JsonElement>(responseText);

            if (!parsed.TryGetProperty("data", out var dataArray) || dataArray.GetArrayLength() == 0)
            {
                return new ImageGenerationResult { Success = false, Message = "No image data returned." };
            }

            var firstItem = dataArray[0];

            if (firstItem.TryGetProperty("b64_json", out var b64))
            {
                var imageBytes = Convert.FromBase64String(b64.GetString()!);
                Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                await File.WriteAllBytesAsync(savePath, imageBytes, ct).ConfigureAwait(false);
                return new ImageGenerationResult { Success = true, FilePath = savePath, Message = "Image generated successfully." };
            }

            if (firstItem.TryGetProperty("url", out var url))
            {
                var imageUrl = url.GetString()!;
                var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl, ct).ConfigureAwait(false);
                Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                await File.WriteAllBytesAsync(savePath, imageBytes, ct).ConfigureAwait(false);
                return new ImageGenerationResult { Success = true, FilePath = savePath, Message = "Image generated successfully." };
            }

            return new ImageGenerationResult { Success = false, Message = "Unexpected image API response format." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image generation failed");
            return new ImageGenerationResult { Success = false, Message = $"Image generation error: {ex.Message}" };
        }
    }

    private static int EstimateContentLength(object content)
    {
        if (content is string s) return s.Length;
        return 500;
    }

    private object BuildUserContent(string userPrompt, IReadOnlyList<AssistantAttachment> attachments)
    {
        if (attachments.Count == 0)
        {
            return userPrompt;
        }

        var parts = new List<ContentPart>
        {
            new() { Type = "text", Text = userPrompt }
        };

        foreach (var attachment in attachments.Take(3))
        {
            if (string.IsNullOrWhiteSpace(attachment.LocalPath) || !File.Exists(attachment.LocalPath))
            {
                continue;
            }

            var bytes = File.ReadAllBytes(attachment.LocalPath);
            var mime = string.IsNullOrWhiteSpace(attachment.MimeType) ? "image/png" : attachment.MimeType;
            var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            parts.Add(new ContentPart
            {
                Type = "image_url",
                ImageUrl = new ImageUrlPart { Url = dataUrl }
            });
        }

        return parts;
    }

    private async Task<string> SendStructuredRequestAsync(object request, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request);

        const int maxRetries = 3;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var reqMsg = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsUrl);
                reqMsg.Content = new StringContent(json, Encoding.UTF8, "application/json");
                reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                reqMsg.Headers.Add("HTTP-Referer", "https://zayflow.app");
                reqMsg.Headers.Add("X-Title", "ZayFlow AI");

                var response = await _httpClient.SendAsync(reqMsg, ct).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var statusCode = (int)response.StatusCode;

                if (statusCode == 429 && attempt < maxRetries)
                {
                    var waitMs = 2000 * (attempt + 1);
                    var match = System.Text.RegularExpressions.Regex.Match(error, @"try again in ([\d.]+)s");
                    if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secs))
                        waitMs = (int)(secs * 1000) + 500;

                    _logger.LogWarning("OpenRouter 429 rate limit — retrying in {WaitMs}ms (attempt {Attempt}/{Max})", waitMs, attempt + 1, maxRetries);
                    await Task.Delay(waitMs, ct).ConfigureAwait(false);
                    continue;
                }

                throw new InvalidOperationException(ParseErrorMessage(statusCode, error));
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                var waitMs = 3000 * (attempt + 1);
                _logger.LogWarning(ex, "OpenRouter network error — retrying in {WaitMs}ms (attempt {Attempt}/{Max})", waitMs, attempt + 1, maxRetries);
                await Task.Delay(waitMs, ct).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("Unable to connect to AI service after multiple retries. Check your internet connection.");
    }

    private static string ParseErrorMessage(int statusCode, string rawError)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawError);
            if (doc.RootElement.TryGetProperty("error", out var errorObj)
                && errorObj.TryGetProperty("message", out var msgProp))
            {
                var msg = msgProp.GetString() ?? rawError;

                if (statusCode == 413 || msg.Contains("rate_limit_exceeded", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("tokens per minute", StringComparison.OrdinalIgnoreCase))
                {
                    return "Your request is too large for the current plan (token limit exceeded). "
                         + "Try a simpler request, or add more OpenRouter credits.";
                }

                if (statusCode == 401 || msg.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase))
                    return "Invalid OpenRouter API key. Check your key in Settings.";

                return msg;
            }
        }
        catch { /* not JSON — fall through */ }

        return $"AI service error (HTTP {statusCode}). Please try again.";
    }

    private AssistantTurnResult ParseAssistantTurnResult(string responseText)
    {
        try
        {
            var jsonText = ExtractJson(responseText);
            var parsed = JsonSerializer.Deserialize<JsonElement>(jsonText);
            var result = new AssistantTurnResult
            {
                Mode = ParseMode(parsed.TryGetProperty("mode", out var modeProp) ? GetStringValue(modeProp) : null),
                Message = parsed.TryGetProperty("message", out var msg) ? GetStringValue(msg) ?? string.Empty : string.Empty,
                Intent = parsed.TryGetProperty("intent", out var intent) ? GetStringValue(intent) ?? "chat" : "chat",
                RequiresConfirmation = parsed.TryGetProperty("requiresConfirmation", out var req) && GetBoolValue(req),
                ConfirmationMessage = parsed.TryGetProperty("confirmationMessage", out var confirm) ? GetStringValue(confirm) ?? string.Empty : string.Empty,
                TokenCost = parsed.TryGetProperty("tokenCost", out var cost) ? GetIntValue(cost, 1) : 1,
                Parameters = parsed.TryGetProperty("parameters", out var parameters) ? ParseStructuredParameters(parameters) : new Dictionary<string, object>(),
                ToolInvocations = parsed.TryGetProperty("tools", out var tools) ? ParseTools(tools) : new List<ToolInvocation>(),
                Artifacts = parsed.TryGetProperty("artifacts", out var artifacts) ? ParseArtifacts(artifacts) : new List<AssistantArtifact>()
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse structured OpenRouter result");
            return new AssistantTurnResult
            {
                Message = SanitizeRawResponse(responseText),
                Intent = "chat"
            };
        }
    }

    private static string? GetStringValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static bool GetBoolValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => string.Equals(element.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => element.TryGetInt32(out var intVal) && intVal != 0,
            _ => false
        };
    }

    private static int GetIntValue(JsonElement element, int defaultValue)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt32(out var intVal) ? intVal : defaultValue,
            JsonValueKind.String => int.TryParse(element.GetString(), out var parsed) ? parsed : defaultValue,
            _ => defaultValue
        };
    }

    private static AssistantTurnMode ParseMode(string? mode)
        => (mode ?? string.Empty).ToLowerInvariant() switch
        {
            "code" => AssistantTurnMode.Code,
            "vision" => AssistantTurnMode.Vision,
            "desktop_action" => AssistantTurnMode.DesktopAction,
            _ => AssistantTurnMode.Chat
        };

    private static List<ToolInvocation> ParseTools(JsonElement element)
    {
        var tools = new List<ToolInvocation>();
        if (element.ValueKind != JsonValueKind.Array) return tools;

        foreach (var item in element.EnumerateArray())
        {
            tools.Add(new ToolInvocation
            {
                Name = item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                Summary = item.TryGetProperty("summary", out var summary) ? summary.GetString() ?? string.Empty : string.Empty,
                Status = item.TryGetProperty("status", out var status) ? ParseToolStatus(status.GetString()) : AssistantToolStatus.Planned,
                ErrorMessage = item.TryGetProperty("errorMessage", out var error) ? error.GetString() ?? string.Empty : string.Empty
            });
        }

        return tools;
    }

    private static AssistantToolStatus ParseToolStatus(string? status)
        => (status ?? string.Empty).ToLowerInvariant() switch
        {
            "running" => AssistantToolStatus.Running,
            "completed" => AssistantToolStatus.Completed,
            "failed" => AssistantToolStatus.Failed,
            _ => AssistantToolStatus.Planned
        };

    private static List<AssistantArtifact> ParseArtifacts(JsonElement element)
    {
        var artifacts = new List<AssistantArtifact>();
        if (element.ValueKind != JsonValueKind.Array) return artifacts;

        foreach (var item in element.EnumerateArray())
        {
            artifacts.Add(new AssistantArtifact
            {
                Kind = ParseArtifactKind(item.TryGetProperty("kind", out var kind) ? GetStringValue(kind) : null),
                Title = item.TryGetProperty("title", out var title) ? GetStringValue(title) ?? string.Empty : string.Empty,
                Summary = item.TryGetProperty("summary", out var summary) ? GetStringValue(summary) ?? string.Empty : string.Empty,
                Content = item.TryGetProperty("content", out var content) ? GetStringValue(content) ?? string.Empty : string.Empty,
                SecondaryContent = item.TryGetProperty("secondaryContent", out var secondary) ? GetStringValue(secondary) ?? string.Empty : string.Empty,
                Language = item.TryGetProperty("language", out var language) ? GetStringValue(language) ?? "text" : "text",
                FilePath = item.TryGetProperty("filePath", out var filePath) ? GetStringValue(filePath) ?? string.Empty : string.Empty,
                IsPreviewOnly = item.TryGetProperty("isPreviewOnly", out var previewOnly) ? GetBoolValue(previewOnly) : true
            });
        }

        return artifacts;
    }

    private static AssistantArtifactKind ParseArtifactKind(string? kind)
        => (kind ?? string.Empty).ToLowerInvariant() switch
        {
            "diff" => AssistantArtifactKind.Diff,
            "ocr" => AssistantArtifactKind.Ocr,
            "imageattachment" => AssistantArtifactKind.ImageAttachment,
            "filelist" => AssistantArtifactKind.FileList,
            "runnotes" => AssistantArtifactKind.RunNotes,
            _ => AssistantArtifactKind.CodePreview
        };

    private static string ExtractJson(string raw)
    {
        var jsonText = raw.Trim();
        if (jsonText.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = jsonText.IndexOf('\n');
            if (firstNewline > 0)
                jsonText = jsonText[(firstNewline + 1)..];
            var lastFence = jsonText.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence > 0)
                jsonText = jsonText[..lastFence];
        }
        jsonText = jsonText.Trim();
        var firstBrace = jsonText.IndexOf('{');
        var lastBrace = jsonText.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
            jsonText = jsonText[firstBrace..(lastBrace + 1)];
        return RepairJsonControlChars(jsonText);
    }

    private Dictionary<string, object> ParseStructuredParameters(JsonElement parametersElement)
    {
        var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in parametersElement.EnumerateObject())
        {
            parameters[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.TryGetInt64(out var intValue) ? intValue : property.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => property.Value.GetRawText()
            };
        }
        return parameters;
    }

    private string BuildSystemPrompt()
    {
        return @"You are ZayFlow AI, a powerful desktop productivity + coding assistant that EXECUTES actions — not just talks about them.
You are the backbone of a premium productivity app. Users pay for you. Be useful, be precise, TAKE ACTION.

## CRITICAL BEHAVIOR
- When the user asks you to DO something, you MUST use an actionable intent. NEVER just describe what you would do.
- You ACTUALLY create files, open apps, run commands, search the web. You are not a chatbot — you are an action engine.
- ALWAYS respond with a JSON object. No markdown, no extra text outside JSON.
- Reference previous messages. If user says 'that file' or 'edit it', look at [SYSTEM NOTE] messages for the file path.
- When user asks to create code, you MUST generate COMPLETE, WORKING code — not placeholder or stub code.

## RESPONSE FORMAT (strict JSON only):
{
  ""intent"": ""<intent_name>"",
  ""message"": ""<friendly response>"",
  ""parameters"": { },
  ""confirmationMessage"": ""<what will happen>"",
  ""requiresConfirmation"": false,
  ""tokenCost"": 1
}

## AVAILABLE INTENTS:

### 📂 FILE MANAGEMENT:
1. organize_folder - Sort files by type. Parameters: { ""folderPath"": ""Downloads"" }
2. detect_duplicates - Find duplicates. Parameters: { ""folderPath"": ""Downloads"" }
3. rename_files - Rename files. Parameters: { ""filePath"": ""..."", ""newName"": ""..."" }
4. move_files - Move files. Parameters: { ""filePath"": ""..."", ""destination"": ""Downloads"" }
5. delete_files - Delete (recycle bin). Parameters: { ""filePath"": ""..."" }
6. read_file - Read file content. Parameters: { ""filePath"": ""..."" }
7. create_folder - Create folder structure. Parameters: { ""folderPath"": ""Documents/MyProject"", ""template"": ""project|web|media"" }

### 📝 DOCUMENT & FILE CREATION:
8. create_document - Create .docx Word documents (timetables, checklists, reports). Parameters: { ""title"": ""..."", ""content"": ""full content"", ""type"": ""document|timetable|checklist|report"" }
9. create_file - Create ANY file type with proper extension. Parameters: { ""fileName"": ""calculator.py"", ""language"": ""python"", ""content"": ""<COMPLETE working code>"", ""savePath"": ""Desktop"" }
   SUPPORTED: .py, .js, .ts, .html, .css, .java, .c, .cpp, .cs, .go, .rs, .rb, .php, .swift, .kt, .sh, .ps1, .bat, .sql, .json, .xml, .yaml, .md, .csv, .txt and more!

### ✏️ FILE EDITING:
10. edit_file - Edit an existing file. Parameters: { ""filePath"": ""Desktop/myfile.py"", ""content"": ""new content"", ""mode"": ""overwrite|append|prepend|replace"", ""find"": ""old text"", ""replace"": ""new text"" }

### 📊 ANALYSIS:
11. folder_insights - Folder size analysis. Parameters: { ""folderPath"": ""Downloads"" }
12. find_old_files - Find old files. Parameters: { ""folderPath"": ""Downloads"", ""daysOld"": ""90"" }
13. summarize_file - Summarize document content. Parameters: { ""filePath"": ""..."" }
14. get_disk_info - Disk usage. Parameters: { ""drive"": ""C"" }

### 📝 TEXT GENERATION:
15. generate_text - Write emails, proposals, messages (saved to desktop). Parameters: { ""type"": ""email|proposal|reply|message"", ""context"": ""..."" }
16. clean_notes - Clean messy notes (saved to desktop). Parameters: { ""content"": ""..."" }
17. plan_tasks - Break goals into steps (saved to desktop). Parameters: { ""goal"": ""..."" }

### 🚀 APPS & WEB:
18. open_application - Launch app. Parameters: { ""appName"": ""notepad|chrome|word|excel|vscode|calculator|explorer|cmd|powershell|paint|spotify|teams|zoom|edge|firefox"" }
19. open_url - Open website. Parameters: { ""url"": ""gmail|youtube|github|google|twitter|facebook|instagram|reddit|linkedin|whatsapp|discord|netflix|chatgpt|notion|stackoverflow|OR any URL"" }
20. open_file - Open existing file in specific app. Parameters: { ""filePath"": ""Desktop/myfile.py"", ""application"": ""vscode"" }
21. search_web - Search the internet. Parameters: { ""query"": ""how to center a div in CSS"", ""engine"": ""google|bing|youtube|github|stackoverflow|reddit|npm|pypi|nuget"" }

### 🖥️ SYSTEM:
22. run_command - Execute a terminal command. Parameters: { ""command"": ""pip install requests"", ""shell"": ""powershell|cmd"" }
23. system_info - System information. Parameters: { ""type"": ""overview|memory|processes"" }
24. change_wallpaper - Set wallpaper. Parameters: { ""imagePath"": ""..."" }
25. clean_desktop - Archive desktop files. Parameters: { }
26. clean_temp - Clean temp files. Parameters: { }
27. quick_automation - Run automation. Parameters: { ""task"": ""clean_temp|organize_downloads|clean_desktop"" }

### ⏰ REMINDERS & UTILITIES:
28. set_reminder - Set a timed reminder. Parameters: { ""message"": ""Stand up and stretch"", ""minutes"": ""30"" }
29. compress_files - Zip or extract files/folders. Parameters: { ""sourcePath"": ""Desktop/MyProject"", ""archiveName"": ""project.zip"", ""mode"": ""compress|extract"" }
30. clipboard_action - Read or write clipboard. Parameters: { ""mode"": ""read|write"", ""content"": ""text to copy"" }
31. translate_text - Translate text directly IN CHAT using AI (no browser needed!). Parameters: { ""text"": ""Hello world"", ""from"": ""english"", ""to"": ""spanish"" }
    LANGUAGES: english, spanish, french, german, italian, portuguese, russian, japanese, chinese, korean, arabic, hindi, turkish, dutch, swedish, polish, thai, vietnamese, urdu, bengali, tamil, greek, hebrew, czech, romanian, hungarian, finnish, danish, norwegian, ukrainian, malay, indonesian
32. download_file - Download files from direct URLs OR website pages (will scan for download links and match them to what user wants). Parameters: { ""url"": ""https://example.com/file.pdf OR https://website.com/downloads"", ""savePath"": ""Downloads"", ""fileName"": ""myfile.pdf"", ""searchTerm"": ""what user wants to download e.g. resident evil village"" }
    IMPORTANT: When user says 'download X from website Y', ALWAYS set searchTerm to X so the scanner can find the right file. If user just gives a direct URL, leave searchTerm empty.

### 💬 GENERAL:
33. chat - Conversation, questions, help. Parameters: { }

### 📸 NEW UTILITIES:
34. screenshot - Take a screenshot. Parameters: { ""mode"": ""fullscreen|window"", ""savePath"": ""Desktop"" }
35. text_to_speech - Read text aloud using PC speakers. Parameters: { ""text"": ""Hello world"", ""speed"": ""normal|slow|fast"" }
36. wifi_info - Show WiFi network details and optionally password. Parameters: { ""showPassword"": ""true|false"" }
37. hash_file - Calculate file hash (checksum). Parameters: { ""filePath"": ""Downloads/file.zip"", ""algorithm"": ""MD5|SHA1|SHA256|SHA512"" }
38. schedule_shutdown - Schedule PC shutdown/restart/sleep or cancel. Parameters: { ""action"": ""shutdown|restart|sleep|logoff"", ""minutes"": ""30"", ""cancel"": ""false"" }
39. convert_units - Convert between units. Parameters: { ""value"": ""100"", ""from"": ""celsius"", ""to"": ""fahrenheit"" }
    UNITS: temperature (c/celsius, f/fahrenheit, k/kelvin), weight (g/kg/lb/oz/ton/mg), length (m/km/cm/mm/mi/ft/in/yd), data (b/kb/mb/gb/tb/bit/kbit/mbit), time (s/min/hr/day/week/month/year/ms)

40. date_time - Show current local date/time or UTC. Parameters: { ""mode"": ""date|time|datetime|utc"" }
41. generate_password - Generate a secure password. Parameters: { ""length"": ""16"", ""includeSymbols"": ""true|false"", ""includeNumbers"": ""true|false"" }
42. quick_math - Evaluate a math expression. Parameters: { ""expression"": ""(25+5)*3/2"" }
43. ping_host - Check latency/reachability for a host. Parameters: { ""host"": ""google.com"", ""count"": ""4"" }
44. process_action - List top processes or terminate one. Parameters: { ""action"": ""list|top|kill"", ""processName"": ""notepad"", ""processId"": ""1234"", ""limit"": ""12"" }

### 🔥 PREMIUM:
45. smart_search - Find files by natural language. Parameters: { ""query"": ""..."", ""scope"": ""Documents|Downloads|Desktop|All"" }
46. ai_bulk_rename - Smart rename. Parameters: { ""folderPath"": ""Downloads"", ""pattern"": ""descriptive|numbered|dated"" }
47. backup_suggestions - Backup advice. Parameters: { ""scope"": ""Documents|Desktop|All"" }
48. smart_cleanup_schedule - Cleanup routines. Parameters: { ""analyze"": ""true"" }
49. visual_analytics - Folder charts. Parameters: { ""folderPath"": ""Downloads"", ""depth"": ""2"" }

## CONFIRMATION RULES:
- requiresConfirmation: TRUE → destructive: organize_folder, move_files, delete_files, clean_desktop, clean_temp, rename_files, ai_bulk_rename, quick_automation, run_command, edit_file, compress_files, download_file, schedule_shutdown, process_action (when action=kill)
- requiresConfirmation: FALSE → everything else (create_file, open_application, open_url, open_file, search_web, system_info, create_document, generate_text, folder_insights, set_reminder, clipboard_action, translate_text, screenshot, text_to_speech, wifi_info, hash_file, convert_units, date_time, generate_password, quick_math, ping_host, process_action for list/top, chat, etc.)

## INTENT ROUTING (follow EXACTLY):
- 'create a python calculator' / 'make a JS file' / 'write a bash script' / 'create an HTML page' → create_file (with complete working code in content!)
- 'create a word document' / 'make a timetable' / 'write a report' → create_document (creates .docx)
- 'edit that file' / 'add a function' / 'fix the code' → edit_file (references file from [SYSTEM NOTE])
- 'open it in vscode' / 'open that in word' → open_file
- 'open notepad' / 'launch chrome' / 'open vscode' → open_application
- 'open gmail' / 'go to youtube' → open_url
- 'search how to X' / 'google something' / 'find on stackoverflow' → search_web
- 'install requests' / 'run pip install' / 'check python version' / 'run a command' → run_command
- 'how much RAM' / 'what processes are running' / 'system info' → system_info
- 'write an email' / 'draft a proposal' → generate_text
- 'organize downloads' → organize_folder
- 'remind me in 30 minutes' / 'set a timer' / 'reminder to call' → set_reminder
- 'zip this folder' / 'compress my project' / 'extract the zip' → compress_files
- 'copy this to clipboard' / 'what's in my clipboard' / 'paste clipboard' → clipboard_action
- 'translate hello to spanish' / 'translate this to french' → translate_text (translation appears IN CHAT, no browser!)
- 'download this file' / 'download from URL' / 'download from this website' / 'download X from Y website' → download_file (supports both direct links AND website pages - scans for download links! ALWAYS set searchTerm when user mentions what they want!)
- 'take a screenshot' / 'capture screen' / 'screenshot my screen' → screenshot
- 'say this aloud' / 'read this text' / 'speak this' / 'text to speech' → text_to_speech
- 'show wifi info' / 'what's my wifi' / 'wifi password' / 'show wifi password' → wifi_info (set showPassword=true when they ask for password!)
- 'hash this file' / 'checksum' / 'md5 of file' / 'sha256' → hash_file
- 'shutdown in 30 minutes' / 'restart PC' / 'sleep computer' / 'cancel shutdown' → schedule_shutdown (set cancel=true when cancelling!)
- 'convert 100 celsius to fahrenheit' / 'how many kg in 50 pounds' / 'convert 5 miles to km' → convert_units
- 'what time is it' / 'today\'s date' / 'show utc time' → date_time
- 'generate a strong password' / 'make a 20-char password' → generate_password
- 'calculate 45*12/3' / 'solve (20+5)*8' → quick_math
- 'ping google.com' / 'check latency to github.com' → ping_host
- 'list running processes' / 'top memory processes' / 'end process notepad' → process_action (set action correctly; requiresConfirmation=true for kill)
- 'hi' / 'hello' / 'what can you do?' → chat

## CODE GENERATION RULES (create_file intent):
- parameters.content MUST contain the ENTIRE complete source code — every function fully implemented, all imports, main entry point, error handling, comments. READY TO RUN.
- NEVER use placeholders: '# ...', '// rest of code', '// TODO', '...' — every function body must be FULLY written out.
- If a program would be very long, write a SIMPLER but FULLY WORKING version rather than a truncated one.
- For 'python calculator' → full GUI calculator using tkinter with all buttons, operations, display. Not a stub.
- For 'HTML landing page' → complete HTML with CSS, responsive, modern.
- Parameters.content value MUST be a single-line JSON string using \n for newlines. NEVER put literal line breaks inside JSON string values.
- Set language parameter correctly for file extension.
- Put ALL source code ONLY in parameters.content. In message, include concise run instructions, not code.

## PATH RULES:
- Use SIMPLE folder names: Downloads, Desktop, Documents, Pictures, Videos
- Subfolder: Documents/MyProject
- NEVER use C:\ or full Windows paths

## CONVERSATION MEMORY:
- Full conversation history is available. Use it.
- [SYSTEM NOTE] messages = action results. Reference file paths from them.
- 'that file' / 'edit it' / 'open it' → look at recent [SYSTEM NOTE] for the file path.
- Be conversational, remember context, be helpful.";
    }

    private AIResponse ParseAIResponse(string jsonResponse)
    {
        try
        {
            var jsonText = jsonResponse.Trim();

            if (jsonText.StartsWith("```"))
            {
                var firstNewline = jsonText.IndexOf('\n');
                if (firstNewline > 0)
                    jsonText = jsonText[(firstNewline + 1)..];

                var lastFence = jsonText.LastIndexOf("```");
                if (lastFence > 0)
                    jsonText = jsonText[..lastFence];
            }

            jsonText = jsonText.Trim();

            var firstBrace = jsonText.IndexOf('{');
            var lastBrace = jsonText.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                jsonText = jsonText[firstBrace..(lastBrace + 1)];
            }

            // LLMs sometimes emit literal newlines/tabs inside JSON string values — fix them
            jsonText = RepairJsonControlChars(jsonText);

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
                    && req.GetBoolean(),
                Parameters = parsed.TryGetProperty("parameters", out var parms)
                    ? ParseParameters(parms)
                    : new Dictionary<string, object>(),
                TokenCost = parsed.TryGetProperty("tokenCost", out var cost)
                    ? cost.GetInt32()
                    : 1
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to parse as JSON: {ex.Message}");
            return new AIResponse
            {
                Success = true,
                Message = SanitizeRawResponse(jsonResponse),
                Intent = "chat",
                TokenCost = 1
            };
        }
    }

    /// <summary>
    /// Attempts to extract a clean message from a raw AI response that failed JSON parsing.
    /// </summary>
    private static string SanitizeRawResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "I encountered an issue processing the response. Please try again.";

        var messageMatch = System.Text.RegularExpressions.Regex.Match(
            raw,
            "\"message\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"" ,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        if (messageMatch.Success && !string.IsNullOrWhiteSpace(messageMatch.Groups[1].Value))
        {
            return System.Text.RegularExpressions.Regex.Unescape(messageMatch.Groups[1].Value);
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("[") || trimmed.Contains("\"intent\""))
        {
            return "I processed your request but had trouble formatting the response. Please try again.";
        }

        return raw;
    }

    /// <summary>
    /// Fix literal newlines/tabs inside JSON string values that LLMs sometimes produce.
    /// </summary>
    private static string RepairJsonControlChars(string json)
    {
        var sb = new System.Text.StringBuilder(json.Length + 200);
        bool inString = false;
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '"')
            {
                int bs = 0;
                for (int j = i - 1; j >= 0 && json[j] == '\\'; j--) bs++;
                if (bs % 2 == 0) inString = !inString;
                sb.Append(c);
            }
            else if (inString)
            {
                if (c == '\n') sb.Append("\\n");
                else if (c == '\r') { /* skip */ }
                else if (c == '\t') sb.Append("\\t");
                else sb.Append(c);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private Dictionary<string, object> ParseParameters(JsonElement parametersElement)
    {
        var parameters = new Dictionary<string, object>();
        foreach (var property in parametersElement.EnumerateObject())
        {
            parameters[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? "",
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => property.Value.GetRawText()
            };
        }
        return parameters;
    }

    #region DTOs
    private class OpenRouterRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("messages")]
        public OpenRouterMessage[] Messages { get; set; } = Array.Empty<OpenRouterMessage>();

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("top_p")]
        public double TopP { get; set; }

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }
    }

    private class OpenRouterMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private class OpenRouterResponse
    {
        [JsonPropertyName("choices")]
        public List<OpenRouterChoice> Choices { get; set; } = new();

        [JsonPropertyName("usage")]
        public OpenRouterUsage? Usage { get; set; }
    }

    private class OpenRouterChoice
    {
        [JsonPropertyName("message")]
        public OpenRouterMessage Message { get; set; } = new();

        [JsonPropertyName("finish_reason")]
        public string FinishReason { get; set; } = "";
    }

    private class OpenRouterUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }

    /// <summary>Structured request DTO (for SendStructuredTurnAsync).</summary>
    private class StructuredMsg
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public object Content { get; set; } = "";
    }

    private class StructuredRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("messages")]
        public List<StructuredMsg> Messages { get; set; } = new();

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("top_p")]
        public double TopP { get; set; }

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("response_format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ResponseFormatSpec? ResponseFormat { get; set; }
    }

    private class ResponseFormatSpec
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "json_object";
    }

    private class ContentPart
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; set; }

        [JsonPropertyName("image_url")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ImageUrlPart? ImageUrl { get; set; }
    }

    private class ImageUrlPart
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }
    #endregion
}

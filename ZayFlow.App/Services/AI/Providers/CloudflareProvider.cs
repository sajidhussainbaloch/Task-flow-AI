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
/// Cloudflare Workers AI provider with OpenAI-compatible chat support and OCR-friendly vision fallback.
/// </summary>
public class CloudflareProvider : IAIProvider
{
    private const int MaxHistoryMessages = 12;
    private readonly HttpClient _httpClient;
    private readonly ILogger<CloudflareProvider> _logger;
    private readonly List<CloudflareMessage> _conversationHistory = new();
    private string? _apiToken;
    private string? _accountId;
    private string _model = DefaultChatModel;

    public const string DefaultChatModel = "@cf/meta/llama-3.1-8b-instruct";
    public const string BalancedChatModel = "@cf/meta/llama-3.3-70b-instruct-fp8-fast";
    public const string DefaultCodeModel = "@cf/qwen/qwen2.5-coder-32b-instruct";
    public const string DefaultVisionModel = "@cf/google/gemma-3-12b-it";
    public const string DefaultImageModel = "@cf/black-forest-labs/flux-1-schnell";

    public static readonly Dictionary<string, string> AvailableModels = new()
    {
        ["Llama 3.1 8B (Fast Chat)"] = DefaultChatModel,
        ["Llama 3.3 70B Fast (Balanced Chat)"] = BalancedChatModel,
        ["Qwen 2.5 Coder 32B (Code)"] = DefaultCodeModel,
        ["Gemma 3 12B (Vision)"] = DefaultVisionModel,
        ["FLUX 1 Schnell (Image)"] = DefaultImageModel
    };

    public string ProviderName => "Cloudflare";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiToken) && !string.IsNullOrWhiteSpace(_accountId);
    public string CurrentModel => _model;

    public CloudflareProvider(HttpClient httpClient, ILogger<CloudflareProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Initialize with "token|accountId|chatModel" or just "token".
    /// </summary>
    public void Initialize(string config)
    {
        if (string.IsNullOrWhiteSpace(config))
        {
            return;
        }

        var parts = config.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length >= 1)
        {
            _apiToken = parts[0];
        }

        if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
        {
            _accountId = parts[1];
        }

        if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
        {
            _model = parts[2];
        }

        _logger.LogInformation("CloudflareProvider initialized with model: {Model}", _model);
    }

    public void SetModel(string modelId)
    {
        _model = modelId;
        _logger.LogInformation("Cloudflare model switched to: {Model}", modelId);
    }

    public void ClearConversationHistory()
    {
        _conversationHistory.Clear();
        _logger.LogInformation("Cloudflare conversation history cleared");
    }

    public void AddNote(string note)
    {
        _conversationHistory.Add(new CloudflareMessage
        {
            Role = "user",
            Content = $"[SYSTEM NOTE - do not repeat this to the user, just remember it]: {note}"
        });

        TrimHistory();
    }

    public async Task<AIResponse> SendChatMessageAsync(string userMessage, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new AIResponse
            {
                Success = false,
                Message = "Cloudflare token or account ID is missing. Add both in Settings."
            };
        }

        try
        {
            _conversationHistory.Add(new CloudflareMessage { Role = "user", Content = userMessage });
            TrimHistory();

            var messages = new List<CloudflareMessage>
            {
                new() { Role = "system", Content = BuildSystemPrompt() }
            };
            messages.AddRange(_conversationHistory);

            var request = new ChatCompletionRequest
            {
                Model = _model,
                Messages = messages.Cast<object>().ToArray(),
                Temperature = 0.3,
                MaxTokens = 1536,
                TopP = 1
            };

            var responseText = await SendRequestAsync(request, ct).ConfigureAwait(false);
            var apiResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseText);
            var assistantMessage = ExtractAssistantContent(apiResponse);
            if (string.IsNullOrWhiteSpace(assistantMessage))
            {
                return new AIResponse
                {
                    Success = false,
                    Message = "Empty response from Cloudflare Workers AI."
                };
            }

            _conversationHistory.Add(new CloudflareMessage { Role = "assistant", Content = assistantMessage });
            TrimHistory();
            return ParseAIResponse(assistantMessage);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Cloudflare network error");
            return new AIResponse
            {
                Success = false,
                Message = "Network error. Check your internet connection."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloudflare error");
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

        var response = await SendChatMessageAsync(prompt, ct).ConfigureAwait(false);
        return response.Message;
    }

    public async Task<string> GenerateTextAsync(string prompt, CancellationToken ct = default)
    {
        var response = await SendChatMessageAsync(prompt, ct).ConfigureAwait(false);
        return response.Message;
    }

    public async Task<AssistantTurnResult> SendStructuredTurnAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        IReadOnlyList<AssistantAttachment> attachments,
        CancellationToken ct = default,
        Action<string>? onToken = null)
    {
        if (!IsConfigured)
        {
            return new AssistantTurnResult
            {
                Message = "Cloudflare token or account ID is not configured. Add both in Settings.",
                Intent = "chat"
            };
        }

        var isVisionRequest = attachments.Count > 0;
        try
        {
            var responseText = await SendStructuredRequestAsync(systemPrompt, userPrompt, model, attachments, ct, onToken).ConfigureAwait(false);
            var apiResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseText);
            var assistantMessage = ExtractAssistantContent(apiResponse);
            if (string.IsNullOrWhiteSpace(assistantMessage))
            {
                return new AssistantTurnResult
                {
                    Message = "Empty response from Cloudflare Workers AI.",
                    Intent = "chat"
                };
            }

            if (isVisionRequest && !assistantMessage.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                return new AssistantTurnResult
                {
                    Message = assistantMessage,
                    Intent = "chat",
                    Mode = AssistantTurnMode.Vision
                };
            }

            return ParseAssistantTurnResult(assistantMessage);
        }
        catch (Exception ex) when (isVisionRequest)
        {
            _logger.LogWarning(ex, "Cloudflare vision request failed; retrying with OCR/text-only fallback");

            var fallbackText = BuildVisionFallbackPrompt(userPrompt, attachments);
            var fallbackModel = string.Equals(model, DefaultVisionModel, StringComparison.OrdinalIgnoreCase)
                ? DefaultCodeModel
                : model;

            try
            {
                var responseText = await SendStructuredRequestAsync(systemPrompt, fallbackText, fallbackModel, Array.Empty<AssistantAttachment>(), ct).ConfigureAwait(false);
                var apiResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseText);
                var assistantMessage = ExtractAssistantContent(apiResponse);
                if (string.IsNullOrWhiteSpace(assistantMessage))
                {
                    return new AssistantTurnResult
                    {
                        Message = "Cloudflare returned no content for the OCR fallback request.",
                        Intent = "chat"
                    };
                }

                return ParseAssistantTurnResult(assistantMessage);
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Cloudflare OCR/text fallback failed");
                throw new InvalidOperationException(fallbackEx.Message, fallbackEx);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Structured Cloudflare request failed");
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    public async Task<ImageGenerationResult> GenerateImageAsync(string prompt, string savePath, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new ImageGenerationResult { Success = false, Message = "Cloudflare token or account ID is missing. Add both in Settings." };
        }

        try
        {
            var responseText = await SendRunModelRequestAsync(DefaultImageModel, new
            {
                prompt,
                steps = 4
            }, ct).ConfigureAwait(false);

            using var document = JsonDocument.Parse(responseText);
            if (!TryExtractEnvelopeResult(document.RootElement, out var resultElement)
                || !resultElement.TryGetProperty("image", out var imageProp)
                || string.IsNullOrWhiteSpace(imageProp.GetString()))
            {
                return new ImageGenerationResult { Success = false, Message = "Cloudflare returned no image data." };
            }

            var imageBytes = Convert.FromBase64String(imageProp.GetString()!);
            var directory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(savePath, imageBytes, ct).ConfigureAwait(false);
            return new ImageGenerationResult
            {
                Success = true,
                FilePath = savePath,
                Message = "Image generated successfully."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloudflare image generation failed");
            return new ImageGenerationResult { Success = false, Message = $"Image generation failed: {ex.Message}" };
        }
    }

    private async Task<string> SendStructuredRequestAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        IReadOnlyList<AssistantAttachment> attachments,
        CancellationToken ct,
        Action<string>? onToken = null)
    {
        var messages = new List<StructuredMessage>
        {
            new() { Role = "system", Content = systemPrompt },
            new() { Role = "user", Content = BuildUserContent(userPrompt, attachments) }
        };

        var promptChars = (systemPrompt?.Length ?? 0) + EstimateContentLength(messages[1].Content);
        var estimatedPromptTokens = promptChars / 4;
        var maxOutputTokens = ResolveStructuredMaxTokens(model, attachments.Count, estimatedPromptTokens);

        var request = new ChatCompletionRequest
        {
            Model = model,
            Messages = messages.Cast<object>().ToArray(),
            Temperature = 0.2,
            MaxTokens = maxOutputTokens,
            TopP = 1
        };

        // Use SSE streaming when a token callback is provided
        if (onToken != null)
            return await SendStreamingRequestAsync(request, onToken, ct).ConfigureAwait(false);

        return await SendRequestAsync(request, ct).ConfigureAwait(false);
    }

    private async Task<string> SendRequestAsync(object request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiToken))
        {
            throw new InvalidOperationException("Cloudflare token is missing.");
        }

        if (string.IsNullOrWhiteSpace(_accountId))
        {
            throw new InvalidOperationException("Cloudflare account ID is missing.");
        }

        var url = $"https://api.cloudflare.com/client/v4/accounts/{_accountId}/ai/v1/chat/completions";
        var json = JsonSerializer.Serialize(request);

        const int maxRetries = 3;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var reqMsg = new HttpRequestMessage(HttpMethod.Post, url);
                reqMsg.Content = new StringContent(json, Encoding.UTF8, "application/json");
                reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);

                var response = await _httpClient.SendAsync(reqMsg, ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                }

                var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var statusCode = (int)response.StatusCode;

                if (statusCode == 429 && attempt < maxRetries)
                {
                    var waitMs = 2000 * (attempt + 1);
                    _logger.LogWarning("Cloudflare 429 rate limit - retrying in {WaitMs}ms (attempt {Attempt}/{Max})", waitMs, attempt + 1, maxRetries);
                    await Task.Delay(waitMs, ct).ConfigureAwait(false);
                    continue;
                }

                throw new InvalidOperationException(ParseErrorMessage(statusCode, error));
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                var waitMs = 3000 * (attempt + 1);
                _logger.LogWarning(ex, "Cloudflare network error - retrying in {WaitMs}ms (attempt {Attempt}/{Max})", waitMs, attempt + 1, maxRetries);
                await Task.Delay(waitMs, ct).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("Unable to connect to Cloudflare Workers AI after multiple retries.");
    }

    /// <summary>
    /// Sends a streaming SSE request to Cloudflare. Collects all tokens and returns the full text.
    /// Invokes <paramref name="onToken"/> for each received chunk for real-time display.
    /// Falls back to non-streaming if SSE parsing fails.
    /// </summary>
    private async Task<string> SendStreamingRequestAsync(object request, Action<string>? onToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiToken) || string.IsNullOrWhiteSpace(_accountId))
            return await SendRequestAsync(request, ct).ConfigureAwait(false);

        var url = $"https://api.cloudflare.com/client/v4/accounts/{_accountId}/ai/v1/chat/completions";

        // Force stream=true on the request via re-serialization
        var jsonDoc = JsonSerializer.SerializeToDocument(request);
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonDoc);
        if (dict != null)
            dict["stream"] = JsonSerializer.SerializeToElement(true);
        var json = JsonSerializer.Serialize(dict);

        using var reqMsg = new HttpRequestMessage(HttpMethod.Post, url);
        reqMsg.Content = new StringContent(json, Encoding.UTF8, "application/json");
        reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(reqMsg, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch
        {
            // Network issue — fall back to non-streaming
            return await SendRequestAsync(request, ct).ConfigureAwait(false);
        }

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            response.Dispose();
            // If rate-limited, let the non-streaming path handle retries
            if (statusCode == 429)
                return await SendRequestAsync(request, ct).ConfigureAwait(false);
            throw new InvalidOperationException(ParseErrorMessage(statusCode, error));
        }

        var fullContent = new StringBuilder();
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null) break;

                if (!line.StartsWith("data: ", StringComparison.Ordinal))
                    continue;

                var payload = line.Substring(6).Trim();
                if (payload == "[DONE]")
                    break;

                try
                {
                    using var chunkDoc = JsonDocument.Parse(payload);
                    var root = chunkDoc.RootElement;
                    if (root.TryGetProperty("choices", out var choices) &&
                        choices.GetArrayLength() > 0)
                    {
                        var delta = choices[0];
                        if (delta.TryGetProperty("delta", out var deltaObj) &&
                            deltaObj.TryGetProperty("content", out var contentProp))
                        {
                            var token = ExtractContentString(contentProp);
                            if (!string.IsNullOrEmpty(token))
                            {
                                fullContent.Append(token);
                                onToken?.Invoke(token);
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Malformed SSE chunk — skip
                }
            }
        }
        finally
        {
            response.Dispose();
        }

        if (fullContent.Length == 0)
        {
            // No tokens received — fall back to non-streaming
            _logger.LogWarning("SSE stream returned no tokens; falling back to non-streaming");
            return await SendRequestAsync(request, ct).ConfigureAwait(false);
        }

        // Wrap the streamed content into a ChatCompletion-shaped JSON so callers can parse it normally
        var wrappedResponse = new
        {
            choices = new[]
            {
                new { message = new { role = "assistant", content = fullContent.ToString() } }
            }
        };
        return JsonSerializer.Serialize(wrappedResponse);
    }

    private async Task<string> SendRunModelRequestAsync(string model, object request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiToken))
        {
            throw new InvalidOperationException("Cloudflare token is missing.");
        }

        if (string.IsNullOrWhiteSpace(_accountId))
        {
            throw new InvalidOperationException("Cloudflare account ID is missing.");
        }

        var url = $"https://api.cloudflare.com/client/v4/accounts/{_accountId}/ai/run/{model}";
        var json = JsonSerializer.Serialize(request);

        using var reqMsg = new HttpRequestMessage(HttpMethod.Post, url);
        reqMsg.Content = new StringContent(json, Encoding.UTF8, "application/json");
        reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);

        var response = await _httpClient.SendAsync(reqMsg, ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new InvalidOperationException(ParseErrorMessage((int)response.StatusCode, error));
    }

    private static string ParseErrorMessage(int statusCode, string rawError)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawError);

            if (doc.RootElement.TryGetProperty("error", out var errorObj))
            {
                if (errorObj.ValueKind == JsonValueKind.Object && errorObj.TryGetProperty("message", out var messageProp))
                {
                    var msg = messageProp.GetString() ?? rawError;
                    return ParseKnownError(statusCode, msg);
                }

                if (errorObj.ValueKind == JsonValueKind.String)
                {
                    return ParseKnownError(statusCode, errorObj.GetString() ?? rawError);
                }
            }

            if (doc.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0)
            {
                var first = errors[0];
                if (first.TryGetProperty("message", out var msgProp))
                {
                    return ParseKnownError(statusCode, msgProp.GetString() ?? rawError);
                }
            }
        }
        catch
        {
            // Fall through to generic message.
        }

        return statusCode switch
        {
            401 => "Invalid Cloudflare token. Check your token in Settings.",
            403 => "Cloudflare rejected this account or permission set. Check the account ID and Workers AI token scopes.",
            _ => $"Cloudflare Workers AI error (HTTP {statusCode})."
        };
    }

    private static string ParseKnownError(int statusCode, string message)
    {
        if (statusCode == 401 || message.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
        {
            return "Invalid Cloudflare token. Check your token in Settings.";
        }

        if (statusCode == 403 || message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return "Cloudflare rejected this account or permission set. Check the account ID and Workers AI token scopes.";
        }

        if (statusCode == 413 || message.Contains("too large", StringComparison.OrdinalIgnoreCase))
        {
            return "Your request is too large for the selected Cloudflare model. Try a smaller request.";
        }

        return message;
    }

    private static int EstimateContentLength(object content)
    {
        if (content is string text)
        {
            return text.Length;
        }

        return 600;
    }

    private static int ResolveStructuredMaxTokens(string model, int attachmentCount, int estimatedPromptTokens)
    {
        if (attachmentCount > 0)
        {
            return 2048;
        }

        var isCodeModel = model.Contains("coder", StringComparison.OrdinalIgnoreCase)
            || model.Contains("code", StringComparison.OrdinalIgnoreCase);
        var baseLimit = isCodeModel ? 8192 : 4096;
        if (estimatedPromptTokens > 7000)
        {
            return Math.Min(baseLimit, 4096);
        }

        return baseLimit;
    }

    private static bool TryExtractEnvelopeResult(JsonElement root, out JsonElement result)
    {
        if (root.TryGetProperty("result", out result))
        {
            return true;
        }

        result = root;
        return result.ValueKind == JsonValueKind.Object;
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

    private static string BuildVisionFallbackPrompt(string userPrompt, IReadOnlyList<AssistantAttachment> attachments)
    {
        var builder = new StringBuilder();
        builder.AppendLine(userPrompt);
        builder.AppendLine();
        builder.AppendLine("Cloudflare image upload fallback:");
        builder.AppendLine("Use the attachment OCR text and previews below instead of raw image understanding.");

        var index = 1;
        foreach (var attachment in attachments)
        {
            builder.AppendLine();
            builder.AppendLine($"Attachment {index}: {attachment.Title}");
            if (!string.IsNullOrWhiteSpace(attachment.PreviewText))
            {
                builder.AppendLine($"Preview: {attachment.PreviewText}");
            }

            if (!string.IsNullOrWhiteSpace(attachment.OcrText))
            {
                builder.AppendLine("OCR:");
                builder.AppendLine(attachment.OcrText);
            }

            index++;
        }

        return builder.ToString();
    }

    private void TrimHistory()
    {
        while (_conversationHistory.Count > MaxHistoryMessages)
        {
            var noteIndex = _conversationHistory.FindIndex(message =>
                message.Role == "user" && message.Content.StartsWith("[SYSTEM NOTE", StringComparison.Ordinal));

            if (noteIndex >= 0 && noteIndex < _conversationHistory.Count - 2)
            {
                _conversationHistory.RemoveAt(noteIndex);
            }
            else
            {
                _conversationHistory.RemoveAt(0);
            }
        }
    }

    private string BuildSystemPrompt()
    {
        return @"You are ZayFlow AI, a premium desktop productivity and coding assistant that executes actions. You are not a chatbot; you are an action engine.

## RULES
1. Always respond with a JSON object. No markdown outside JSON. No extra text.
2. When the user asks to do something, use an actionable intent. Never just describe.
3. Reference [SYSTEM NOTE] messages for file paths when the user says ""that file"" or ""edit it"".
4. For create_file: generate complete, working, production-quality code with imports, error handling, and comments.
5. Use simple paths like Downloads, Desktop, Documents, Pictures. Never full C:\ paths.
6. Never operate on Windows, System32, Program Files, or other system directories.

## RESPONSE FORMAT
{""intent"":""<name>"",""message"":""<friendly markdown response>"",""parameters"":{},""confirmationMessage"":""<what happens>"",""requiresConfirmation"":false,""tokenCost"":1}

## STYLE
- Use rich markdown in the message field.
- Be concise but thorough.
- Suggest the next helpful step when it fits.
- Full conversation history is available.";
    }

    private AssistantTurnResult ParseAssistantTurnResult(string responseText)
    {
        try
        {
            var jsonText = ExtractJson(responseText);
            var parsed = JsonSerializer.Deserialize<JsonElement>(jsonText);
            return new AssistantTurnResult
            {
                Mode = ParseMode(parsed.TryGetProperty("mode", out var modeProp) ? GetStringValue(modeProp) : null),
                Message = parsed.TryGetProperty("message", out var msg) ? GetStringValue(msg) ?? string.Empty : string.Empty,
                Intent = parsed.TryGetProperty("intent", out var intent) ? GetStringValue(intent) ?? "chat" : "chat",
                RequiresConfirmation = parsed.TryGetProperty("requiresConfirmation", out var req) && GetBoolValue(req),
                ConfirmationMessage = parsed.TryGetProperty("confirmationMessage", out var confirm) ? GetStringValue(confirm) ?? string.Empty : string.Empty,
                TokenCost = parsed.TryGetProperty("tokenCost", out var cost) ? GetIntValue(cost, 1) : 1,
                Parameters = parsed.TryGetProperty("parameters", out var parameters) ? ParseParameters(parameters) : new Dictionary<string, object>(),
                ToolInvocations = parsed.TryGetProperty("tools", out var tools) ? ParseTools(tools) : new List<ToolInvocation>(),
                Artifacts = parsed.TryGetProperty("artifacts", out var artifacts) ? ParseArtifacts(artifacts) : new List<AssistantArtifact>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse structured Cloudflare result");
            return new AssistantTurnResult
            {
                Message = SanitizeRawResponse(responseText),
                Intent = "chat"
            };
        }
    }

    private AIResponse ParseAIResponse(string jsonResponse)
    {
        try
        {
            var jsonText = ExtractJson(jsonResponse);
            var parsed = JsonSerializer.Deserialize<JsonElement>(jsonText);
            return new AIResponse
            {
                Success = true,
                Message = parsed.TryGetProperty("message", out var msgProp) ? GetStringValue(msgProp) ?? string.Empty : string.Empty,
                Intent = parsed.TryGetProperty("intent", out var intentProp) ? GetStringValue(intentProp) ?? "chat" : "chat",
                ConfirmationMessage = parsed.TryGetProperty("confirmationMessage", out var confirm) ? GetStringValue(confirm) ?? string.Empty : string.Empty,
                RequiresConfirmation = parsed.TryGetProperty("requiresConfirmation", out var req) && GetBoolValue(req),
                Parameters = parsed.TryGetProperty("parameters", out var parms) ? ParseParameters(parms) : new Dictionary<string, object>(),
                TokenCost = parsed.TryGetProperty("tokenCost", out var cost) ? GetIntValue(cost, 1) : 1
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Cloudflare JSON response");
            return new AIResponse
            {
                Success = true,
                Message = SanitizeRawResponse(jsonResponse),
                Intent = "chat",
                TokenCost = 1
            };
        }
    }

    private static string ExtractAssistantContent(ChatCompletionResponse? apiResponse)
    {
        if (apiResponse?.Choices == null || apiResponse.Choices.Count == 0)
        {
            return string.Empty;
        }

        var message = apiResponse.Choices[0].Message;
        var content = ExtractContentString(message.Content);
        if (!string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        return message.ReasoningContent ?? string.Empty;
    }

    /// <summary>
    /// Handles Cloudflare returning content as either a plain string or an array of content parts.
    /// </summary>
    private static string ExtractContentString(JsonElement? element)
    {
        if (element == null || !element.HasValue)
            return string.Empty;

        var val = element.Value;

        // Plain string: "content": "hello"
        if (val.ValueKind == JsonValueKind.String)
            return val.GetString() ?? string.Empty;

        // Array of content parts: "content": [{"type":"text","text":"hello"}]
        if (val.ValueKind == JsonValueKind.Array)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var part in val.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.String)
                {
                    sb.Append(part.GetString());
                }
                else if (part.ValueKind == JsonValueKind.Object &&
                         part.TryGetProperty("text", out var textProp))
                {
                    sb.Append(textProp.GetString());
                }
            }
            return sb.ToString();
        }

        // Fallback: return raw JSON text
        return val.GetRawText();
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
            "code_review" => AssistantTurnMode.CodeReview,
            "refactor" => AssistantTurnMode.Refactor,
            "research" => AssistantTurnMode.Research,
            "document" => AssistantTurnMode.Document,
            _ => AssistantTurnMode.Chat
        };

    private static List<ToolInvocation> ParseTools(JsonElement element)
    {
        var tools = new List<ToolInvocation>();
        if (element.ValueKind != JsonValueKind.Array)
        {
            return tools;
        }

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
        if (element.ValueKind != JsonValueKind.Array)
        {
            return artifacts;
        }

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

    private Dictionary<string, object> ParseParameters(JsonElement parametersElement)
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

    private static string ExtractJson(string raw)
    {
        var jsonText = raw.Trim();
        if (jsonText.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = jsonText.IndexOf('\n');
            if (firstNewline > 0)
            {
                jsonText = jsonText[(firstNewline + 1)..];
            }

            var lastFence = jsonText.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence > 0)
            {
                jsonText = jsonText[..lastFence];
            }
        }

        jsonText = jsonText.Trim();
        var firstBrace = jsonText.IndexOf('{');
        var lastBrace = jsonText.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            jsonText = jsonText[firstBrace..(lastBrace + 1)];
        }

        return RepairJsonControlChars(jsonText);
    }

    private static string RepairJsonControlChars(string json)
    {
        var sb = new StringBuilder(json.Length + 200);
        var inString = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (c == '"')
            {
                var backslashes = 0;
                for (var j = i - 1; j >= 0 && json[j] == '\\'; j--)
                {
                    backslashes++;
                }

                if (backslashes % 2 == 0)
                {
                    inString = !inString;
                }

                sb.Append(c);
            }
            else if (inString)
            {
                if (c == '\n') sb.Append("\\n");
                else if (c == '\r') { }
                else if (c == '\t') sb.Append("\\t");
                else sb.Append(c);
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static string SanitizeRawResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "I encountered an issue processing the response. Please try again.";
        }

        var messageMatch = System.Text.RegularExpressions.Regex.Match(
            raw,
            "\"message\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        if (messageMatch.Success && !string.IsNullOrWhiteSpace(messageMatch.Groups[1].Value))
        {
            try
            {
                return System.Text.RegularExpressions.Regex.Unescape(messageMatch.Groups[1].Value);
            }
            catch
            {
                // Unescape can fail on sequences like \U from Windows paths — return the raw match
                return messageMatch.Groups[1].Value.Replace("\\n", "\n").Replace("\\t", "\t");
            }
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal)
            || trimmed.StartsWith("[", StringComparison.Ordinal)
            || trimmed.Contains("\"intent\"", StringComparison.Ordinal))
        {
            return "I processed your request but had trouble formatting the response. Please try again.";
        }

        return raw;
    }

    private interface IChatMessage
    {
        string Role { get; }
        object Content { get; }
    }

    private sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public object[] Messages { get; set; } = Array.Empty<object>();

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("top_p")]
        public double TopP { get; set; }

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = false;

        [JsonPropertyName("response_format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ResponseFormatSpec? ResponseFormat { get; set; }
    }

    private sealed class ResponseFormatSpec
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "json_schema";
    }

    private sealed class CloudflareMessage : IChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        object IChatMessage.Content => Content;
    }

    private sealed class StructuredMessage : IChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public object Content { get; set; } = string.Empty;
    }

    private sealed class ContentPart
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

    private sealed class ImageUrlPart
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice> Choices { get; set; } = new();
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public ChoiceMessage Message { get; set; } = new();
    }

    private sealed class ChoiceMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public JsonElement? Content { get; set; }

        [JsonPropertyName("reasoning_content")]
        public string? ReasoningContent { get; set; }
    }
}

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
/// Groq API provider with legacy intent routing support plus structured multimodal turn support.
/// </summary>
public class GroqProvider : IAIProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GroqProvider> _logger;
    private string? _apiKey;
    private readonly List<GroqMessage> _conversationHistory = new();
    private const int MaxHistoryMessages = 20;

    public string ProviderName => "Groq (Free)";
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public GroqProvider(HttpClient httpClient, ILogger<GroqProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public void Initialize(string apiKey)
    {
        _apiKey = apiKey;
        _logger.LogInformation("GroqProvider initialized");
    }

    public void ClearConversationHistory()
    {
        _conversationHistory.Clear();
        _logger.LogInformation("Groq conversation history cleared");
    }

    public void AddNote(string note)
    {
        _conversationHistory.Add(new GroqMessage
        {
            Role = "user",
            Content = $"[SYSTEM NOTE - do not repeat this to the user, just remember it]: {note}"
        });

        TrimHistory();
    }

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
                Message = "Groq API key not configured. Add your key in Settings.",
                Intent = "chat"
            };
        }

        try
        {
            var messages = new List<GroqStructuredMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = BuildUserContent(userPrompt, attachments) }
            };

            // Vision models (llama-4-scout, etc.) often don't support json_object response format
            var isVisionModel = attachments.Count > 0;
            // Estimate token count (~4 chars per token) and cap MaxTokens to avoid 413
            var promptChars = (systemPrompt?.Length ?? 0) + EstimateContentLength(messages[1].Content);
            var estimatedPromptTokens = promptChars / 4;
            var maxOutputTokens = isVisionModel ? 8192 : Math.Min(16000, Math.Max(2048, 12000 - estimatedPromptTokens));

            var request = new GroqStructuredRequest
            {
                Model = model,
                Messages = messages,
                Temperature = 0.2,
                MaxTokens = maxOutputTokens,
                TopP = 1,
                Stream = false,
                ResponseFormat = isVisionModel ? null : new GroqResponseFormat { Type = "json_object" }
            };

            var responseText = await SendRequestAsync(request, ct).ConfigureAwait(false);
            var groqResponse = JsonSerializer.Deserialize<GroqResponse>(responseText);
            if (groqResponse?.Choices == null || groqResponse.Choices.Count == 0)
            {
                return new AssistantTurnResult { Message = "Empty response from Groq API.", Intent = "chat" };
            }
            var innerContent = groqResponse.Choices[0].Message.Content;
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
            _logger.LogError(ex, "Structured Groq request failed");
            // Re-throw so callers (code gen engine, plan mode, etc.) can handle properly
            // instead of silently treating the error as chat content
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    private static int EstimateContentLength(object content)
    {
        if (content is string s) return s.Length;
        // For vision messages with image parts, estimate text portion only
        return 500;
    }

    public async Task<AIResponse> SendChatMessageAsync(string userMessage, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new AIResponse
            {
                Success = false,
                Message = "Groq API key not configured. Add your key to the backend configuration file at: %LOCALAPPDATA%\\ZayFlow\\backend-config.json (field: \"GroqApiKey\"). Get a free key at: https://console.groq.com"
            };
        }

        try
        {
            _conversationHistory.Add(new GroqMessage { Role = "user", Content = userMessage });
            TrimHistory();

            var messages = new List<GroqMessage>
            {
                new() { Role = "system", Content = BuildSystemPrompt() }
            };
            messages.AddRange(_conversationHistory);

            var request = new GroqRequest
            {
                Model = "llama-3.3-70b-versatile",
                Messages = messages.ToArray(),
                Temperature = 0.7,
                MaxTokens = 16000,
                TopP = 1,
                Stream = false
            };

            var responseText = await SendRequestAsync(request, ct).ConfigureAwait(false);
            var groqResponse = JsonSerializer.Deserialize<GroqResponse>(responseText);
            if (groqResponse?.Choices == null || groqResponse.Choices.Count == 0)
            {
                return new AIResponse
                {
                    Success = false,
                    Message = "Empty response from Groq"
                };
            }

            var assistantMessage = groqResponse.Choices[0].Message.Content;
            _conversationHistory.Add(new GroqMessage { Role = "assistant", Content = assistantMessage });
            return ParseAIResponse(assistantMessage);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Groq network error");
            return new AIResponse
            {
                Success = false,
                Message = "Network error. Check internet connection."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Groq error");
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

    public async Task<ImageGenerationResult> GenerateImageAsync(string prompt, string savePath, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new ImageGenerationResult { Success = false, Message = "Groq API key not configured." };
        }

        try
        {
            var requestBody = new
            {
                model = "playai/playai-image-generation",
                prompt = prompt,
                n = 1,
                size = "1024x1024"
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.PostAsync("https://api.groq.com/openai/v1/images/generations", content, ct).ConfigureAwait(false);
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

            // Check for base64 data
            if (firstItem.TryGetProperty("b64_json", out var b64))
            {
                var imageBytes = Convert.FromBase64String(b64.GetString()!);
                Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                await File.WriteAllBytesAsync(savePath, imageBytes, ct).ConfigureAwait(false);
                return new ImageGenerationResult { Success = true, FilePath = savePath, Message = "Image generated successfully." };
            }

            // Check for URL
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

    private async Task<string> SendRequestAsync(object request, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request);

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        const int maxRetries = 3;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("https://api.groq.com/openai/v1/chat/completions", content, ct).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var statusCode = (int)response.StatusCode;

                // Retry on 429 rate limit with exponential backoff
                if (statusCode == 429 && attempt < maxRetries)
                {
                    var waitMs = 2000 * (attempt + 1);
                    var match = System.Text.RegularExpressions.Regex.Match(error, @"try again in ([\d.]+)s");
                    if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secs))
                        waitMs = (int)(secs * 1000) + 500;

                    _logger.LogWarning("Groq 429 rate limit — retrying in {WaitMs}ms (attempt {Attempt}/{Max})", waitMs, attempt + 1, maxRetries);
                    await Task.Delay(waitMs, ct).ConfigureAwait(false);
                    continue;
                }

                // Parse friendly error message from Groq JSON error response
                throw new InvalidOperationException(ParseGroqErrorMessage(statusCode, error));
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                // Network / DNS failures — retry with backoff
                var waitMs = 3000 * (attempt + 1);
                _logger.LogWarning(ex, "Groq network error — retrying in {WaitMs}ms (attempt {Attempt}/{Max})", waitMs, attempt + 1, maxRetries);
                await Task.Delay(waitMs, ct).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("Unable to connect to AI service after multiple retries. Check your internet connection.");
    }

    /// <summary>Parses the Groq JSON error body into a clean, user-friendly message.</summary>
    private static string ParseGroqErrorMessage(int statusCode, string rawError)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawError);
            if (doc.RootElement.TryGetProperty("error", out var errorObj)
                && errorObj.TryGetProperty("message", out var msgProp))
            {
                var msg = msgProp.GetString() ?? rawError;

                // 413 / rate_limit_exceeded on tokens — give a specific helpful message
                if (statusCode == 413 || msg.Contains("rate_limit_exceeded", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("tokens per minute", StringComparison.OrdinalIgnoreCase))
                {
                    return "Your request is too large for the current Groq plan (token limit exceeded). "
                         + "Try a simpler request, or upgrade to Groq Dev Tier for higher limits.";
                }

                if (statusCode == 401 || msg.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase))
                    return "Invalid Groq API key. Check your key in Settings.";

                return msg;
            }
        }
        catch { /* not JSON — fall through */ }

        return $"AI service error (HTTP {statusCode}). Please try again.";
    }

    private object BuildUserContent(string userPrompt, IReadOnlyList<AssistantAttachment> attachments)
    {
        if (attachments.Count == 0)
        {
            return userPrompt;
        }

        var parts = new List<GroqContentPart>
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
            parts.Add(new GroqContentPart
            {
                Type = "image_url",
                ImageUrl = new GroqImageUrl { Url = dataUrl }
            });
        }

        return parts;
    }

    private void TrimHistory()
    {
        while (_conversationHistory.Count > MaxHistoryMessages)
        {
            var noteIdx = _conversationHistory.FindIndex(m =>
                m.Role == "user" && m.Content.StartsWith("[SYSTEM NOTE", StringComparison.Ordinal));
            if (noteIdx >= 0 && noteIdx < _conversationHistory.Count - 2)
            {
                _conversationHistory.RemoveAt(noteIdx);
            }
            else
            {
                _conversationHistory.RemoveAt(0);
            }
        }
    }

    private string BuildSystemPrompt()
    {
        return @"You are ZayFlow AI, a premium desktop productivity and coding assistant that EXECUTES actions. You are not a chatbot; you are an action engine.

## RULES
1. ALWAYS respond with a JSON object. No markdown outside JSON. No extra text.
2. When user asks to DO something, use an actionable intent. Never just describe.
3. Reference [SYSTEM NOTE] messages for file paths when user says ""that file""/""edit it"".
4. For create_file: generate COMPLETE, WORKING, PRODUCTION-QUALITY code with imports, error handling, comments. Ready to compile/run.
5. Simple paths: Downloads, Desktop, Documents, Pictures. Never C:\\ full paths.
6. NEVER operate on Windows, System32, Program Files, or system directories.

## RESPONSE FORMAT (strict JSON):
{""intent"":""<name>"",""message"":""<friendly response with markdown formatting>"",""parameters"":{},""confirmationMessage"":""<what happens>"",""requiresConfirmation"":false,""tokenCost"":1}

## INTENTS (91 total, use exact names):
FILE: organize_folder(folderPath,mode:type|date|category) | detect_duplicates(folderPath) | rename_files(filePath,newName) | move_files(filePath,destination) | delete_files(filePath) | read_file(filePath) | create_folder(folderPath,template:project|web|media|school) | open_file(filePath,application)
CREATE: create_document(title,content,type:document|timetable|checklist|report) | create_file(fileName,language,content,savePath)
EDIT: edit_file(filePath,content,mode:overwrite|append|prepend|replace,find,replace)
ANALYSIS: folder_insights(folderPath) | find_old_files(folderPath,daysOld) | summarize_file(filePath) | get_disk_info(drive) | visual_analytics(folderPath,depth)
TEXT: generate_text(type:email|proposal|reply,context) | clean_notes(content) | plan_tasks(goal)
APPS: open_application(appName) | open_url(url) | search_web(query,engine) | download_file(url,savePath,fileName,searchTerm)
SYSTEM: run_command(command,shell) | system_info(type) | change_wallpaper(imagePath) | clean_desktop | clean_temp | quick_automation(task) | process_action(action:list|top|kill,processName)
UTILS: set_reminder(message,minutes) | compress_files(sourcePath,archiveName,mode) | clipboard_action(mode,content) | translate_text(text,from,to) | screenshot(mode,savePath) | text_to_speech(text,speed) | wifi_info(showPassword) | hash_file(filePath,algorithm) | schedule_shutdown(action,minutes,cancel) | convert_units(value,from,to) | date_time(mode) | generate_password(length) | quick_math(expression) | ping_host(host,count)
SEARCH: smart_search(query,scope) | ai_bulk_rename(folderPath,pattern) | backup_suggestions(scope) | smart_cleanup_schedule(analyze)
BATCH: batch_operations(operation,sourcePath,pattern,destination) | quick_note(action:add|list|search|delete,content,query) | focus_mode(duration,action) | daily_briefing | generate_report(type,path) | file_templates(template,name,savePath) | workspace_snapshot(action,name) | productivity_tips | preview_changes(action,path) | explain_action(intent) | suggest_workflow(goal)
FILES: sync_folders(source,target,mode) | file_diff(file1,file2) | encrypt_decrypt(filePath,action,password) | secure_delete(filePath) | bulk_metadata(folderPath,pattern) | regex_search(folderPath,pattern,filePattern)
MEDIA: data_convert(filePath,targetFormat) | text_transform(text,operation) | image_tools(imagePath,action,width) | pdf_tools(filePath,action) | extract_text(filePath)
NETWORK: network_diagnostics | port_scan(host) | dns_manage(action,domain) | hosts_file(action)
POWER: startup_manager(action) | service_manager(action,filter) | env_variables(action,name) | performance_report | power_plan(action) | storage_analyzer(path,action)
DEV: git_quick(action,path) | api_test(url,method,body) | code_format(filePath,action) | qr_code(text,savePath)
AUTO: watch_folder(folderPath,action) | scheduled_task(action) | auto_backup(sourcePath,backupPath)
WORKFLOW: batch_workflow(steps) | save_template(action,name,steps)
HUB: zayflow_hub(category)
CHAT: chat (general conversation)

## CONFIRMATION REQUIRED (set requiresConfirmation:true):
organize_folder, move_files, delete_files, clean_desktop, clean_temp, rename_files, ai_bulk_rename, quick_automation, run_command, edit_file, compress_files, download_file, schedule_shutdown, process_action(kill), secure_delete, encrypt_decrypt, sync_folders, auto_backup, batch_operations(move/rename)

## CODE GENERATION (create_file intent):
- parameters.content MUST contain the ENTIRE source code - every function fully implemented, all imports, main entry point, error handling. READY TO RUN.
- NEVER use placeholders like '# ...', '// rest of code', '// TODO', or '...' - every single function body must be complete.
- If a program would be too long, write a SIMPLER but FULLY WORKING version instead of a truncated one.
- parameters.language = correct language (python, javascript, csharp, html, etc.)
- parameters.fileName = full filename with extension
- message field = what it does, how to run it (commands), prerequisites. NO code in message.
- For GUI: use proper frameworks (tkinter for Python, WinForms for C#, etc.)
- The content value MUST be a single-line JSON string with \n for newlines. NEVER put literal line breaks inside JSON string values.

## CRITICAL INTENT RULES:
- The intent field must ALWAYS be a specific intent name (e.g., ""qr_code"", ""wifi_info"", ""screenshot""). NEVER return a category header (FILE, CREATE, EDIT, UTILS, DEV, SYSTEM, NETWORK, MEDIA, POWER, etc.) as the intent value.
- ""generate qr code"" / ""make qr code"" → qr_code with text parameter (NOT ""DEV"")
- ""wifi password"" / ""wifi info"" → wifi_info with showPassword:true (NOT ""UTILS"")
- ""take a screenshot"" → screenshot (NOT ""UTILS"")

## STYLE
- Use rich markdown in message field: **bold**, *italic*, `code`, bullet lists, headers
- Be concise but thorough. Sound like a senior developer.
- Proactively suggest next steps: 'Want me to open it in VS Code?'
- Full conversation history available. Reference it naturally";
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
                Parameters = parsed.TryGetProperty("parameters", out var parameters) ? ParseParameters(parameters) : new Dictionary<string, object>(),
                ToolInvocations = parsed.TryGetProperty("tools", out var tools) ? ParseTools(tools) : new List<ToolInvocation>(),
                Artifacts = parsed.TryGetProperty("artifacts", out var artifacts) ? ParseArtifacts(artifacts) : new List<AssistantArtifact>()
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse structured Groq result");
            return new AssistantTurnResult
            {
                Message = SanitizeRawResponse(responseText),
                Intent = "chat"
            };
        }
    }

    /// <summary>Safely extract a string from a JsonElement regardless of its value kind.</summary>
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

    /// <summary>Safely extract a bool from a JsonElement — handles string "true"/"false" and numbers.</summary>
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

    /// <summary>Safely extract an int from a JsonElement — handles string numbers and booleans.</summary>
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

    private AIResponse ParseAIResponse(string jsonResponse)
    {
        try
        {
            var jsonText = ExtractJson(jsonResponse);
            var parsed = JsonSerializer.Deserialize<JsonElement>(jsonText);

            return new AIResponse
            {
                Success = true,
                Message = parsed.TryGetProperty("message", out var msgProp) ? GetStringValue(msgProp) ?? "" : "",
                Intent = parsed.TryGetProperty("intent", out var intentProp) ? GetStringValue(intentProp) ?? "chat" : "chat",
                ConfirmationMessage = parsed.TryGetProperty("confirmationMessage", out var confirm)
                    ? GetStringValue(confirm) ?? ""
                    : "",
                RequiresConfirmation = parsed.TryGetProperty("requiresConfirmation", out var req)
                    && GetBoolValue(req),
                Parameters = parsed.TryGetProperty("parameters", out var parms)
                    ? ParseParameters(parms)
                    : new Dictionary<string, object>(),
                TokenCost = parsed.TryGetProperty("tokenCost", out var cost)
                    ? GetIntValue(cost, 1)
                    : 1
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Groq JSON response");
            return new AIResponse
            {
                Success = true,
                Message = SanitizeRawResponse(jsonResponse),
                Intent = "chat",
                TokenCost = 1
            };
        }
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
                var bs = 0;
                for (var j = i - 1; j >= 0 && json[j] == '\\'; j--)
                {
                    bs++;
                }

                if (bs % 2 == 0)
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

    /// <summary>
    /// Attempts to extract a clean message from a raw AI response that failed JSON parsing.
    /// Tries to pull the "message" field value, otherwise strips JSON-like syntax for display.
    /// </summary>
    private static string SanitizeRawResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "I encountered an issue processing the response. Please try again.";

        // Try a best-effort regex extraction of the "message" field
        var messageMatch = System.Text.RegularExpressions.Regex.Match(
            raw,
            "\"message\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        if (messageMatch.Success && !string.IsNullOrWhiteSpace(messageMatch.Groups[1].Value))
        {
            return System.Text.RegularExpressions.Regex.Unescape(messageMatch.Groups[1].Value);
        }

        // If response looks like JSON structure, give a friendly fallback
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("[") || trimmed.Contains("\"intent\""))
        {
            return "I processed your request but had trouble formatting the response. Please try again.";
        }

        // Otherwise return the raw text (it's probably plain text)
        return raw;
    }

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

    #region DTOs
    private class GroqRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public GroqMessage[] Messages { get; set; } = Array.Empty<GroqMessage>();

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("top_p")]
        public double TopP { get; set; }

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }
    }

    private class GroqStructuredRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<GroqStructuredMessage> Messages { get; set; } = new();

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
        public GroqResponseFormat? ResponseFormat { get; set; }
    }

    private class GroqResponseFormat
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "json_object";
    }

    private class GroqStructuredMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public object Content { get; set; } = string.Empty;
    }

    private class GroqContentPart
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; set; }

        [JsonPropertyName("image_url")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GroqImageUrl? ImageUrl { get; set; }
    }

    private class GroqImageUrl
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }

    private class GroqMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private class GroqResponse
    {
        [JsonPropertyName("choices")]
        public List<GroqChoice> Choices { get; set; } = new();

        [JsonPropertyName("usage")]
        public GroqUsage? Usage { get; set; }
    }

    private class GroqChoice
    {
        [JsonPropertyName("message")]
        public GroqChoiceMessage Message { get; set; } = new();

        [JsonPropertyName("finish_reason")]
        public string FinishReason { get; set; } = string.Empty;
    }

    private class GroqChoiceMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private class GroqUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
    #endregion
}

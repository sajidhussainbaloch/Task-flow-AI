using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Contracts;

namespace ZayFlow.App.Services.AI.Providers;

/// <summary>
/// Groq API provider - Free, ultra-fast inference with conversation memory.
/// </summary>
public class GroqProvider : IAIProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GroqProvider> _logger;
    private string? _apiKey;
    private readonly List<GroqMessage> _conversationHistory = new();
    private const int MaxHistoryMessages = 20; // Keep last 20 messages for context

    public string ProviderName => "Groq (Free)";
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public GroqProvider(HttpClient httpClient, ILogger<GroqProvider> _logger)
    {
        _httpClient = httpClient;
        this._logger = _logger;
    }

    public void Initialize(string apiKey)
    {
        _apiKey = apiKey;
        _logger.LogInformation("GroqProvider initialized");
    }

    /// <summary>
    /// Clears conversation history for a fresh session.
    /// </summary>
    public void ClearConversationHistory()
    {
        _conversationHistory.Clear();
        _logger.LogInformation("Groq conversation history cleared");
    }

    /// <summary>
    /// Add a system-level note to conversation memory (e.g., action results).
    /// Uses the "user" role with a [SYSTEM] prefix so the model understands it's context, not a user message.
    /// </summary>
    public void AddNote(string note)
    {
        _conversationHistory.Add(new GroqMessage
        {
            Role = "user",
            Content = $"[SYSTEM NOTE - do not repeat this to the user, just remember it]: {note}"
        });

        // Trim if needed
        TrimHistory();
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
            // Add user message to conversation history
            _conversationHistory.Add(new GroqMessage { Role = "user", Content = userMessage });

            // Smart trim: remove system notes first, then oldest messages
            TrimHistory();

            // Build the full message list: system prompt + conversation history
            var messages = new List<GroqMessage>
            {
                new GroqMessage { Role = "system", Content = BuildSystemPrompt() }
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

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.PostAsync(
                "https://api.groq.com/openai/v1/chat/completions",
                content,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError($"Groq API error {response.StatusCode}: {error}");
                return new AIResponse
                {
                    Success = false,
                    Message = $"API Error {response.StatusCode}. Check your API key at https://console.groq.com"
                };
            }

            var responseText = await response.Content.ReadAsStringAsync(ct);
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

            // Add assistant response to conversation history for memory
            _conversationHistory.Add(new GroqMessage { Role = "assistant", Content = assistantMessage });

            return ParseAIResponse(assistantMessage);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError($"Groq network error: {ex.Message}");
            return new AIResponse
            {
                Success = false,
                Message = "Network error. Check internet connection."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Groq error: {ex.Message}");
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
    /// Smart trim: prioritize removing old system notes before removing actual conversation turns.
    /// </summary>
    private void TrimHistory()
    {
        // First pass: remove system notes (they are less important than user/assistant turns)
        while (_conversationHistory.Count > MaxHistoryMessages)
        {
            var noteIdx = _conversationHistory.FindIndex(m =>
                m.Role == "user" && m.Content.StartsWith("[SYSTEM NOTE"));
            if (noteIdx >= 0 && noteIdx < _conversationHistory.Count - 2) // Don't remove very recent
            {
                _conversationHistory.RemoveAt(noteIdx);
            }
            else
            {
                // Fall back to FIFO removal of oldest messages
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
5. Simple paths: Downloads, Desktop, Documents, Pictures. Never C:\ full paths.
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
- parameters.content MUST contain the ENTIRE source code — every function fully implemented, all imports, main entry point, error handling. READY TO RUN.
- NEVER use placeholders like '# ...', '// rest of code', '// TODO', or '...' — every single function body must be complete.
- If a program would be too long, write a SIMPLER but FULLY WORKING version instead of a truncated one.
- parameters.language = correct language (python, javascript, csharp, html, etc.)
- parameters.fileName = full filename with extension
- message field = what it does, how to run it (commands), prerequisites. NO code in message.
- For GUI: use proper frameworks (tkinter for Python, WinForms for C#, etc.)
- The content value MUST be a single-line JSON string with \n for newlines. NEVER put literal line breaks inside JSON string values.

## STYLE
- Use rich markdown in message field: **bold**, *italic*, `code`, bullet lists, headers
- Be concise but thorough. Sound like a senior developer.
- Proactively suggest next steps: 'Want me to open it in VS Code?'
- Full conversation history available. Reference it naturally";
    }
    private AIResponse ParseAIResponse(string jsonResponse)
    {
        try
        {
            // Extract JSON — find the outermost { ... } to avoid mangling code in parameters
            var jsonText = jsonResponse.Trim();

            // If the response starts with a code fence, strip only the outer fence
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

            // Find the outermost JSON object boundaries
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
                Message = jsonResponse,
                Intent = "chat",
                TokenCost = 1
            };
        }
    }

    /// <summary>
    /// Fix literal newlines/tabs inside JSON string values that LLMs sometimes produce.
    /// Walks char-by-char, tracks whether we're inside a quoted string, escapes control chars.
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
                // Count preceding backslashes to handle escaped quotes correctly
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
            // Handle different JSON value types — code content may be long strings with escapes
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
    private class GroqRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

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

    private class GroqMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
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
        public GroqMessage Message { get; set; } = new();

        [JsonPropertyName("finish_reason")]
        public string FinishReason { get; set; } = "";
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
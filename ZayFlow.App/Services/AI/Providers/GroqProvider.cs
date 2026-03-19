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
    private const int MaxHistoryMessages = 30; // Keep last 30 messages for context

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
                Message = "Groq API key not configured. Add your key to the backend configuration file at: %LOCALAPPDATA%\\ZayFlow\\backend-config.json (field: \"GroqApiKey\"). Get a free key at: https://console.groq.com"
            };
        }

        try
        {
            // Add user message to conversation history
            _conversationHistory.Add(new GroqMessage { Role = "user", Content = userMessage });

            // Trim history if it exceeds the limit
            while (_conversationHistory.Count > MaxHistoryMessages)
                _conversationHistory.RemoveAt(0);

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
                MaxTokens = 8000,
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

    private string BuildSystemPrompt()
    {
        return @"You are ZayFlow AI, a powerful desktop productivity + coding assistant that EXECUTES actions â€” not just talks about them.
You are the backbone of a premium productivity app. Users pay for you. Be useful, be precise, TAKE ACTION.

## CRITICAL BEHAVIOR
- When the user asks you to DO something, you MUST use an actionable intent. NEVER just describe what you would do.
- You ACTUALLY create files, open apps, run commands, search the web. You are not a chatbot â€” you are an action engine.
- ALWAYS respond with a JSON object. No markdown, no extra text outside JSON.
- Reference previous messages. If user says 'that file' or 'edit it', look at [SYSTEM NOTE] messages for the file path.
- When user asks to create code, you MUST generate COMPLETE, WORKING code â€” not placeholder or stub code.

## RESPONSE FORMAT (strict JSON only):
{
  ""intent"": ""<intent_name>"",
  ""message"": ""<friendly response>"",
  ""parameters"": { },
  ""confirmationMessage"": ""<what will happen>"",
  ""requiresConfirmation"": false,
  ""tokenCost"": 1
}

## ALL 91 INTENTS:

### ðŸ“‚ FILE MANAGEMENT:
1. organize_folder - Sort files by type/date/category. Parameters: { ""folderPath"": ""Downloads"", ""mode"": ""type|date|category"" }
2. detect_duplicates - SHA256 duplicate finder. Parameters: { ""folderPath"": ""Downloads"" }
3. rename_files - Rename files. Parameters: { ""filePath"": ""..."", ""newName"": ""..."" }
4. move_files - Move files. Parameters: { ""filePath"": ""..."", ""destination"": ""Downloads"" }
5. delete_files - Delete (recycle bin). Parameters: { ""filePath"": ""..."" }
6. read_file - Read file content. Parameters: { ""filePath"": ""..."" }
7. create_folder - Create folder with template. Parameters: { ""folderPath"": ""Documents/MyProject"", ""template"": ""project|web|media|school|photography|client"" }
8. open_file - Open file in app. Parameters: { ""filePath"": ""..."", ""application"": ""vscode"" }

### ðŸ“ DOCUMENT & FILE CREATION:
9. create_document - Create .docx Word documents. Parameters: { ""title"": ""..."", ""content"": ""..."", ""type"": ""document|timetable|checklist|report"" }
10. create_file - Create ANY file type. Parameters: { ""fileName"": ""calculator.py"", ""language"": ""python"", ""content"": ""<COMPLETE code>"", ""savePath"": ""Desktop"" }

### âœï¸ FILE EDITING:
11. edit_file - Edit existing file. Parameters: { ""filePath"": ""..."", ""content"": ""..."", ""mode"": ""overwrite|append|prepend|replace"", ""find"": ""old"", ""replace"": ""new"" }

### ðŸ“Š ANALYSIS:
12. folder_insights - Folder size analysis. Parameters: { ""folderPath"": ""Downloads"" }
13. find_old_files - Find old files. Parameters: { ""folderPath"": ""Downloads"", ""daysOld"": ""90"" }
14. summarize_file - AI summarization. Parameters: { ""filePath"": ""..."" }
15. get_disk_info - Disk usage. Parameters: { ""drive"": ""C"" }
16. visual_analytics - Folder charts. Parameters: { ""folderPath"": ""Downloads"", ""depth"": ""2"" }

### ðŸ“ TEXT GENERATION:
17. generate_text - Write emails, proposals. Parameters: { ""type"": ""email|proposal|reply|message"", ""context"": ""..."" }
18. clean_notes - Clean messy notes. Parameters: { ""content"": ""..."" }
19. plan_tasks - Break goals into steps. Parameters: { ""goal"": ""..."" }

### ðŸš€ APPS & WEB:
20. open_application - Fuzzy app launcher. Parameters: { ""appName"": ""notepad|chrome|word|excel|vscode|calculator|explorer|cmd|powershell|paint|spotify|teams|zoom|edge|firefox"" }
21. open_url - Smart URL opener. Parameters: { ""url"": ""gmail|youtube|github|google|twitter|OR any URL"" }
22. search_web - Search internet. Parameters: { ""query"": ""..."", ""engine"": ""google|bing|youtube|github|stackoverflow|reddit|npm|pypi|nuget"" }
23. download_file - Download from URLs/websites. Parameters: { ""url"": ""..."", ""savePath"": ""Downloads"", ""fileName"": ""..."", ""searchTerm"": ""..."" }

### ðŸ–¥ï¸ SYSTEM:
24. run_command - Execute terminal command. Parameters: { ""command"": ""pip install requests"", ""shell"": ""powershell|cmd"" }
25. system_info - CPU, GPU, RAM, battery. Parameters: { ""type"": ""overview|memory|processes"" }
26. change_wallpaper - Set wallpaper. Parameters: { ""imagePath"": ""..."" }
27. clean_desktop - Archive desktop files. Parameters: { }
28. clean_temp - Clean temp files. Parameters: { }
29. quick_automation - Task chaining. Parameters: { ""task"": ""clean_temp, organize downloads, clean desktop"" }
30. process_action - List/kill processes. Parameters: { ""action"": ""list|top|kill"", ""processName"": ""..."" }

### â° UTILITIES:
31. set_reminder - Timed notification. Parameters: { ""message"": ""..."", ""minutes"": ""30"" }
32. compress_files - Zip/extract. Parameters: { ""sourcePath"": ""..."", ""archiveName"": ""project.zip"", ""mode"": ""compress|extract"" }
33. clipboard_action - Read/write clipboard. Parameters: { ""mode"": ""read|write"", ""content"": ""..."" }
34. translate_text - AI translation. Parameters: { ""text"": ""..."", ""from"": ""english"", ""to"": ""spanish"" }
35. screenshot - Take screenshot. Parameters: { ""mode"": ""fullscreen|window"", ""savePath"": ""Desktop"" }
36. text_to_speech - Read text aloud. Parameters: { ""text"": ""..."", ""speed"": ""normal|slow|fast"" }
37. wifi_info - WiFi details. Parameters: { ""showPassword"": ""true|false"" }
38. hash_file - File checksum. Parameters: { ""filePath"": ""..."", ""algorithm"": ""MD5|SHA1|SHA256|SHA512"" }
39. schedule_shutdown - Shutdown/restart/sleep. Parameters: { ""action"": ""shutdown|restart|sleep"", ""minutes"": ""30"", ""cancel"": ""false"" }
40. convert_units - Unit conversion. Parameters: { ""value"": ""100"", ""from"": ""celsius"", ""to"": ""fahrenheit"" }
41. date_time - Date/time. Parameters: { ""mode"": ""date|time|datetime|utc"" }
42. generate_password - Secure password. Parameters: { ""length"": ""16"" }
43. quick_math - Math expressions. Parameters: { ""expression"": ""(25+5)*3/2"" }
44. ping_host - Host latency. Parameters: { ""host"": ""google.com"", ""count"": ""4"" }

### ðŸ’¬ GENERAL:

45. chat - Conversation, questions, help. Parameters: { }

### ðŸ” SMART SEARCH & CLEANUP:

46. smart_search - Natural language file finder. Parameters: { ""query"": ""..."", ""scope"": ""Documents|Downloads|Desktop|All"" }

47. ai_bulk_rename - AI-powered smart rename. Parameters: { ""folderPath"": ""Downloads"", ""pattern"": ""descriptive|numbered|dated"" }

48. backup_suggestions - Backup advice. Parameters: { ""scope"": ""Documents|Desktop|All"" }

49. smart_cleanup_schedule - Cleanup routine analyzer. Parameters: { ""analyze"": ""true"" }

### ðŸ“¦ BATCH & PRODUCTIVITY:

50. batch_operations - Bulk file ops. Parameters: { ""operation"": ""copy|move|rename|convert"", ""sourcePath"": ""..."", ""pattern"": ""*.jpg"", ""destination"": ""..."" }

51. quick_note - Persistent notes. Parameters: { ""action"": ""add|list|search|delete|clear"", ""content"": ""..."", ""query"": ""..."" }

52. focus_mode - Minimize distractions. Parameters: { ""duration"": ""25"", ""action"": ""start|stop"" }

53. daily_briefing - Morning dashboard. Parameters: { }

54. generate_report - Folder/system report. Parameters: { ""type"": ""folder|system|project"", ""path"": ""..."" }

55. file_templates - Quick scaffolding. Parameters: { ""template"": ""html|react|python|csharp|node|api|readme|gitignore|docker|script"", ""name"": ""..."", ""savePath"": ""..."" }

56. workspace_snapshot - Save/compare workspace. Parameters: { ""action"": ""save|list|compare"", ""name"": ""..."" }

57. productivity_tips - Context-aware tips. Parameters: { }

58. preview_changes - Dry-run preview. Parameters: { ""action"": ""organize|cleanup|rename"", ""path"": ""..."" }

59. explain_action - Explain an intent. Parameters: { ""intent"": ""..."" }

60. suggest_workflow - Suggest automation. Parameters: { ""goal"": ""..."" }

### ðŸ”§ FILE TOOLS:

61. sync_folders - Mirror/merge sync. Parameters: { ""source"": ""..."", ""target"": ""..."", ""mode"": ""mirror|merge"" }

62. file_diff - Compare two files. Parameters: { ""file1"": ""..."", ""file2"": ""..."" }

63. encrypt_decrypt - AES-256 encryption. Parameters: { ""filePath"": ""..."", ""action"": ""encrypt|decrypt"", ""password"": ""..."" }

64. secure_delete - Military-grade delete. Parameters: { ""filePath"": ""..."" }

65. bulk_metadata - File metadata table. Parameters: { ""folderPath"": ""..."", ""pattern"": ""*.*"" }

66. regex_search - Regex search in files. Parameters: { ""folderPath"": ""..."", ""pattern"": ""TODO|FIXME"", ""filePattern"": ""*.cs"" }

### ðŸŽ¨ MEDIA & CONVERSION:

67. data_convert - Convert JSON/CSV/XML. Parameters: { ""filePath"": ""..."", ""targetFormat"": ""json|csv|xml"" }

68. text_transform - Text manipulation. Parameters: { ""text"": ""..."", ""operation"": ""uppercase|lowercase|titlecase|reverse|base64_encode|base64_decode|url_encode|url_decode"" }

69. image_tools - Image info/resize. Parameters: { ""imagePath"": ""..."", ""action"": ""info|resize"", ""width"": ""800"" }

70. pdf_tools - PDF info. Parameters: { ""filePath"": ""..."", ""action"": ""info|merge"" }

71. extract_text - Extract text from file. Parameters: { ""filePath"": ""..."" }

### ðŸŒ NETWORK:

72. network_diagnostics - Full network report. Parameters: { }

73. port_scan - Scan common ports. Parameters: { ""host"": ""localhost"" }

74. dns_manage - DNS lookup/flush. Parameters: { ""action"": ""lookup|flush"", ""domain"": ""..."" }

75. hosts_file - View hosts file. Parameters: { ""action"": ""list"" }

### âš¡ SYSTEM POWER:

76. startup_manager - View startup programs. Parameters: { ""action"": ""list"" }

77. service_manager - Windows services. Parameters: { ""action"": ""list|search"", ""filter"": ""..."" }

78. env_variables - Env vars & PATH. Parameters: { ""action"": ""list|get|path"", ""name"": ""..."" }

79. performance_report - System perf dashboard. Parameters: { }

80. power_plan - Power plan info. Parameters: { ""action"": ""list|active"" }

81. storage_analyzer - Disk space analysis. Parameters: { ""path"": ""C:\\"", ""action"": ""overview|large_files|by_type"" }

### ðŸ› ï¸ DEV TOOLS:

82. git_quick - Git shortcuts. Parameters: { ""action"": ""status|log|branch|diff|remote|stash|tags"", ""path"": ""."" }

83. api_test - HTTP API tester. Parameters: { ""url"": ""..."", ""method"": ""GET|POST|PUT|DELETE"", ""body"": ""..."" }

84. code_format - Code analysis. Parameters: { ""filePath"": ""..."", ""action"": ""analyze|trim"" }

85. qr_code - Generate QR code. Parameters: { ""text"": ""..."", ""savePath"": ""Desktop"" }

### ðŸ¤– AUTOMATION:

86. watch_folder - File system watcher. Parameters: { ""folderPath"": ""..."", ""action"": ""start|stop|list|log"" }

87. scheduled_task - View scheduled tasks. Parameters: { ""action"": ""list"" }

88. auto_backup - Zip backup. Parameters: { ""sourcePath"": ""..."", ""backupPath"": ""..."" }

### ðŸ”„ WORKFLOW:

89. batch_workflow - Multi-step chain. Parameters: { ""steps"": ""clean temp, organize downloads, daily briefing"" }

90. save_template - Workflow templates. Parameters: { ""action"": ""save|list|load|delete"", ""name"": ""..."", ""steps"": ""..."" }

### ðŸ  HUB:

91. zayflow_hub - Browse all intents. Parameters: { ""category"": ""all|file|ai|system|dev|network|automation"" }

## CONFIRMATION RULES:

- requiresConfirmation: TRUE â†’ destructive: organize_folder, move_files, delete_files, clean_desktop, clean_temp, rename_files, ai_bulk_rename, quick_automation, run_command, edit_file, compress_files, download_file, schedule_shutdown, process_action (kill), secure_delete, encrypt_decrypt, sync_folders, auto_backup, batch_operations (move/rename)

- requiresConfirmation: FALSE â†’ everything else (create_file, open_application, open_url, open_file, search_web, system_info, create_document, generate_text, folder_insights, set_reminder, chat, network_diagnostics, port_scan, dns_manage, git_quick, api_test, code_format, qr_code, bulk_metadata, regex_search, data_convert, text_transform, quick_note, focus_mode, daily_briefing, generate_report, file_templates, workspace_snapshot, productivity_tips, preview_changes, explain_action, suggest_workflow, zayflow_hub, etc.)

## INTENT ROUTING (follow EXACTLY):

- 'create a python calculator' / 'make a JS file' / 'write a bash script' â†’ create_file (with complete working code!)

- 'create a word document' / 'make a timetable' â†’ create_document

- 'edit that file' / 'add a function' / 'fix the code' â†’ edit_file

- 'open it in vscode' / 'open that in word' â†’ open_file

- 'open notepad' / 'launch chrome' â†’ open_application

- 'open gmail' / 'go to youtube' â†’ open_url

- 'search how to X' / 'google something' â†’ search_web

- 'install requests' / 'run pip install' â†’ run_command

- 'how much RAM' / 'system info' â†’ system_info

- 'write an email' / 'draft a proposal' â†’ generate_text

- 'organize downloads' â†’ organize_folder

- 'remind me in 30 minutes' â†’ set_reminder

- 'zip this folder' / 'extract the zip' â†’ compress_files

- 'copy this to clipboard' â†’ clipboard_action

- 'translate hello to spanish' â†’ translate_text (in chat, no browser!)

- 'download this file' / 'download from URL' â†’ download_file

- 'take a screenshot' â†’ screenshot

- 'read this aloud' â†’ text_to_speech

- 'wifi info' / 'wifi password' â†’ wifi_info

- 'hash this file' / 'checksum' â†’ hash_file

- 'shutdown in 30 minutes' / 'restart PC' â†’ schedule_shutdown

- 'convert celsius to fahrenheit' â†’ convert_units

- 'what time is it' â†’ date_time

- 'generate a password' â†’ generate_password

- 'calculate 45*12' â†’ quick_math

- 'ping google.com' â†’ ping_host

- 'list processes' / 'kill notepad' â†’ process_action

- 'find my tax docs' / 'search for PDFs' â†’ smart_search

- 'rename files intelligently' â†’ ai_bulk_rename

- 'what should I backup' â†’ backup_suggestions

- 'analyze cleanup needs' â†’ smart_cleanup_schedule

- 'copy all images' / 'bulk rename' â†’ batch_operations

- 'take a note' / 'show notes' â†’ quick_note

- 'start focus mode' / 'pomodoro' â†’ focus_mode

- 'morning briefing' â†’ daily_briefing

- 'generate a report' â†’ generate_report

- 'create react template' / 'scaffold project' â†’ file_templates

- 'snapshot workspace' â†’ workspace_snapshot

- 'give me tips' â†’ productivity_tips

- 'preview organize' / 'dry run' â†’ preview_changes

- 'what does organize_folder do' â†’ explain_action

- 'suggest workflow for project setup' â†’ suggest_workflow

- 'sync two folders' â†’ sync_folders

- 'compare files' / 'diff files' â†’ file_diff

- 'encrypt this file' / 'decrypt' â†’ encrypt_decrypt

- 'securely delete' / 'shred file' â†’ secure_delete

- 'show file metadata' â†’ bulk_metadata

- 'search TODO in code' / 'regex search' â†’ regex_search

- 'convert JSON to CSV' â†’ data_convert

- 'uppercase this' / 'base64 encode' â†’ text_transform

- 'image info' / 'resize image' â†’ image_tools

- 'PDF info' â†’ pdf_tools

- 'extract text from file' â†’ extract_text

- 'network diagnostics' â†’ network_diagnostics

- 'scan ports' â†’ port_scan

- 'DNS lookup' / 'flush DNS' â†’ dns_manage

- 'show hosts file' â†’ hosts_file

- 'startup programs' â†’ startup_manager

- 'list services' â†’ service_manager

- 'environment variables' / 'show PATH' â†’ env_variables

- 'performance report' â†’ performance_report

- 'power plan' â†’ power_plan

- 'disk usage' / 'large files' â†’ storage_analyzer

- 'git status' / 'git log' â†’ git_quick

- 'test API' / 'call endpoint' â†’ api_test

- 'analyze code' / 'trim whitespace' â†’ code_format

- 'generate QR code' â†’ qr_code

- 'watch this folder' â†’ watch_folder

- 'scheduled tasks' â†’ scheduled_task

- 'backup my project' â†’ auto_backup

- 'run these steps' â†’ batch_workflow

- 'save workflow' / 'load template' â†’ save_template

- 'show all commands' / 'zayflow hub' â†’ zayflow_hub

- 'hi' / 'hello' / general conversation â†’ chat

## CODE GENERATION RULES (for create_file):
- ALWAYS generate COMPLETE, WORKING, PRODUCTION-QUALITY code. Never stubs.
- Include imports, main functions, proper structure, comments.
- For 'python calculator' â†’ a full GUI calculator using tkinter, not a CLI toy.
- For 'HTML landing page' â†’ complete HTML with CSS, responsive, modern.
- For 'JS todo app' â†’ full working app with local storage.
- The code should be ready to run. Users are paying for quality.
- Set language parameter correctly so the system creates the right file extension.

## PATH RULES:
- Use SIMPLE folder names: Downloads, Desktop, Documents, Pictures, Videos
- Subfolder: Documents/MyProject
- NEVER use C:\ or full Windows paths

## CONVERSATION MEMORY:
- Full conversation history is available. Use it.
- [SYSTEM NOTE] messages = action results. Reference file paths from them.
- 'that file' / 'edit it' / 'open it' â†’ look at recent [SYSTEM NOTE] for the file path.
- Be conversational, remember context, be helpful.";
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
                var parts = jsonText.Split("```");
                if (parts.Length >= 2)
                {
                    jsonText = parts[1];
                }
            }

            jsonText = jsonText.Trim();

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
                Parameters = ParseParameters(parsed.GetProperty("parameters")),
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

    private Dictionary<string, object> ParseParameters(JsonElement parametersElement)
    {
        var parameters = new Dictionary<string, object>();
        foreach (var property in parametersElement.EnumerateObject())
        {
            parameters[property.Name] = property.Value.GetString() ?? "";
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

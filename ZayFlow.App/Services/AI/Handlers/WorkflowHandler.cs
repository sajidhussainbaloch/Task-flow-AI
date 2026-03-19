using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Contracts;

namespace ZayFlow.App.Services.AI.Handlers;

/// <summary>
/// Tier 2 (Premium): Workflow tools — batch workflow chaining, save/load workflow templates.
/// </summary>
public class WorkflowHandler
{
    private readonly ILogger<WorkflowHandler> _logger;
    private static readonly string _templatesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZayFlow", "WorkflowTemplates");

    public WorkflowHandler(ILogger<WorkflowHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>batch_workflow — Chain multiple intents for sequential execution.</summary>
    public Task<ActionResult> ExecuteBatchWorkflowAsync(string workflow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(workflow))
            return Task.FromResult(new ActionResult { Success = false, Message = "Provide a workflow description. Example: 'organize downloads, clean temp, generate report'" });

        // Parse the workflow steps
        var steps = workflow.Split(new[] { ',', '|', ';', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (steps.Length == 0)
            return Task.FromResult(new ActionResult { Success = false, Message = "No workflow steps found in your description." });

        var sb = new StringBuilder();
        sb.AppendLine($"📋 Batch Workflow Plan ({steps.Length} steps):\n");

        for (int i = 0; i < steps.Length; i++)
        {
            sb.AppendLine($"  {i + 1}. 🔄 {steps[i]}");
        }

        sb.AppendLine($"\n💡 To execute this workflow, send each step as a separate message.");
        sb.AppendLine("   ZayFlow will process them one at a time for safety.");
        sb.AppendLine($"\n🔖 Tip: Use 'save_template' to save this workflow for reuse.");

        return Task.FromResult(new ActionResult { Success = true, Message = sb.ToString() });
    }

    /// <summary>save_template — Save a workflow as a reusable template.</summary>
    public async Task<ActionResult> ExecuteSaveTemplateAsync(string name, string workflow, string action, CancellationToken ct)
    {
        Directory.CreateDirectory(_templatesDir);
        var sb = new StringBuilder();

        switch (action.ToLowerInvariant())
        {
            case "save" or "" when !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(workflow):
                var steps = workflow.Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var template = new WorkflowTemplate
                {
                    Name = name,
                    Steps = steps.ToList(),
                    CreatedAt = DateTime.Now
                };

                var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
                var filePath = Path.Combine(_templatesDir, $"{safeName}.json");
                var json = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json, ct);

                sb.AppendLine($"✅ Workflow template saved: '{name}'");
                sb.AppendLine($"\nSteps ({steps.Length}):");
                for (int i = 0; i < steps.Length; i++)
                    sb.AppendLine($"  {i + 1}. {steps[i]}");
                break;

            case "list":
                var files = Directory.GetFiles(_templatesDir, "*.json");
                if (files.Length == 0)
                {
                    sb.AppendLine("No saved workflow templates.");
                }
                else
                {
                    sb.AppendLine($"📋 Saved Workflow Templates ({files.Length}):\n");
                    foreach (var f in files)
                    {
                        try
                        {
                            var content = await File.ReadAllTextAsync(f, ct);
                            var tmpl = JsonSerializer.Deserialize<WorkflowTemplate>(content);
                            sb.AppendLine($"  📌 {tmpl?.Name ?? Path.GetFileNameWithoutExtension(f)} ({tmpl?.Steps.Count ?? 0} steps)");
                            if (tmpl?.Steps != null)
                                foreach (var step in tmpl.Steps)
                                    sb.AppendLine($"     ➤ {step}");
                        }
                        catch
                        {
                            sb.AppendLine($"  📌 {Path.GetFileNameWithoutExtension(f)} (error reading)");
                        }
                    }
                }
                break;

            case "load" when !string.IsNullOrWhiteSpace(name):
                var loadPath = Directory.GetFiles(_templatesDir, "*.json")
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(name.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase));

                if (loadPath == null)
                    return new ActionResult { Success = false, Message = $"Template '{name}' not found. Use action 'list' to see available templates." };

                var loadContent = await File.ReadAllTextAsync(loadPath, ct);
                var loaded = JsonSerializer.Deserialize<WorkflowTemplate>(loadContent);
                if (loaded == null)
                    return new ActionResult { Success = false, Message = "Failed to parse template." };

                sb.AppendLine($"📋 Workflow: {loaded.Name}\n");
                sb.AppendLine($"Created: {loaded.CreatedAt:f}\n");
                for (int i = 0; i < loaded.Steps.Count; i++)
                    sb.AppendLine($"  {i + 1}. {loaded.Steps[i]}");
                sb.AppendLine("\n💡 Send each step as a message to execute.");
                break;

            case "delete" when !string.IsNullOrWhiteSpace(name):
                var delPath = Directory.GetFiles(_templatesDir, "*.json")
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(name.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase));

                if (delPath != null)
                {
                    File.Delete(delPath);
                    sb.AppendLine($"🗑️ Deleted template: '{name}'");
                }
                else
                    sb.AppendLine($"Template '{name}' not found.");
                break;

            default:
                return new ActionResult { Success = false, Message = "Actions: 'save' (name + workflow), 'list', 'load' (name), 'delete' (name)." };
        }

        return new ActionResult { Success = true, Message = sb.ToString() };
    }

    private class WorkflowTemplate
    {
        public string Name { get; set; } = "";
        public List<string> Steps { get; set; } = new();
        public DateTime CreatedAt { get; set; }
    }
}

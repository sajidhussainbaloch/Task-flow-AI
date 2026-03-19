using System.Collections.ObjectModel;
using System.Windows.Input;
using ZayFlow.App.Commands;
using ZayFlow.App.Services;
using ZayFlow.App.Services.AI.Contracts;

namespace ZayFlow.App.ViewModels;

public sealed class ProductivityTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
}

public sealed class ProductivityToolsViewModel : ViewModelBase
{
    private readonly IAIService _aiService;
    private readonly NotificationService _notificationService;

    private ProductivityTemplate? _selectedTemplate;
    private string _input = string.Empty;
    private string _result = string.Empty;
    private bool _isGenerating;

    public ProductivityToolsViewModel(IAIService aiService, NotificationService notificationService)
    {
        _aiService = aiService;
        _notificationService = notificationService;

        Templates = new ObservableCollection<ProductivityTemplate>
        {
            new() { Name = "Resume Generator", Prompt = "Create a modern resume from this information:" },
            new() { Name = "Proposal Writer", Prompt = "Write a persuasive proposal using this context:" },
            new() { Name = "Meeting Notes Generator", Prompt = "Generate structured meeting notes from:" },
            new() { Name = "Email Draft Generator", Prompt = "Draft a professional email based on:" },
            new() { Name = "Study Notes Creator", Prompt = "Create concise study notes from:" },
            new() { Name = "Project Planning Assistant", Prompt = "Create a project plan with milestones from:" }
        };

        GenerateCommand = new RelayCommand(async _ => await GenerateAsync(), _ => SelectedTemplate != null && !string.IsNullOrWhiteSpace(Input) && !IsGenerating);

        SelectedTemplate = Templates.FirstOrDefault();
    }

    public ObservableCollection<ProductivityTemplate> Templates { get; }
    public ICommand GenerateCommand { get; }

    public ProductivityTemplate? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (SetProperty(ref _selectedTemplate, value))
            {
                (GenerateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string Input
    {
        get => _input;
        set
        {
            if (SetProperty(ref _input, value))
            {
                (GenerateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string Result
    {
        get => _result;
        set => SetProperty(ref _result, value);
    }

    public bool IsGenerating
    {
        get => _isGenerating;
        set
        {
            if (SetProperty(ref _isGenerating, value))
            {
                (GenerateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    private async Task GenerateAsync()
    {
        if (SelectedTemplate == null)
        {
            return;
        }

        IsGenerating = true;
        try
        {
            var prompt = $"{SelectedTemplate.Prompt}\n\n{Input}";
            Result = await _aiService.GenerateTextAsync(prompt);
        }
        catch (Exception ex)
        {
            _notificationService.ShowError("AI service unavailable", ex.Message);
        }
        finally
        {
            IsGenerating = false;
        }
    }
}

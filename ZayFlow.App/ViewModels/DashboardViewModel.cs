using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using ZayFlow.App.Commands;
using ZayFlow.App.Services;

namespace ZayFlow.App.ViewModels;

public class QuickAction
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Color { get; set; } = "#6366F1";
}

public class RecentAction
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string StatusColor { get; set; } = "#34D399";
}

public class DashboardViewModel : ViewModelBase
{
    private readonly NotificationService _notificationService;
    private readonly MainViewModel _mainViewModel;
    private string _greeting = string.Empty;
    private int _tokenUsed = 23;
    private int _tokenLimit = 100;
    private int _actionsToday = 7;
    private int _filesProcessed = 42;
    private double _timeSavedMinutes = 18.5;

    public DashboardViewModel(NotificationService notificationService, MainViewModel mainViewModel)
    {
        _notificationService = notificationService;
        _mainViewModel = mainViewModel;

        UpdateGreeting();

        _tokenUsed = _mainViewModel.TokenUsed;
        _tokenLimit = _mainViewModel.TokenDailyLimit;

        QuickActions = new ObservableCollection<QuickAction>
        {
            new() { Title = "Organize Downloads", Description = "Sort files by type", Icon = "\uE896", Color = "#6366F1" },
            new() { Title = "Clean Temp Files", Description = "Free up disk space", Icon = "\uE74D", Color = "#F59E0B" },
            new() { Title = "Batch Rename", Description = "Rename files in bulk", Icon = "\uE70F", Color = "#10B981" },
            new() { Title = "AI Suggest", Description = "Get smart suggestions", Icon = "\uE734", Color = "#8B5CF6" }
        };

        RecentActions = new ObservableCollection<RecentAction>
        {
            new() { Title = "Organized Downloads", Subtitle = "Moved 23 files into 5 folders", Time = "2 min ago", Icon = "\uE896", StatusColor = "#34D399" },
            new() { Title = "Cleaned Temp Files", Subtitle = "Freed 1.2 GB of space", Time = "15 min ago", Icon = "\uE74D", StatusColor = "#34D399" },
            new() { Title = "Batch Renamed", Subtitle = "Renamed 12 photos", Time = "1 hour ago", Icon = "\uE70F", StatusColor = "#34D399" },
            new() { Title = "AI Suggestion Applied", Subtitle = "Created project structure", Time = "3 hours ago", Icon = "\uE734", StatusColor = "#60A5FA" },
            new() { Title = "File Backup", Subtitle = "Backed up 156 documents", Time = "Yesterday", Icon = "\uE74E", StatusColor = "#34D399" }
        };

        QuickActionCommand = new RelayCommand(OnQuickAction);
    }

    public ObservableCollection<QuickAction> QuickActions { get; }
    public ObservableCollection<RecentAction> RecentActions { get; }
    public ICommand QuickActionCommand { get; }

    public string Greeting
    {
        get => _greeting;
        set => SetProperty(ref _greeting, value);
    }

    public int TokenUsed
    {
        get => _tokenUsed;
        set { if (SetProperty(ref _tokenUsed, value)) OnPropertyChanged(nameof(TokenPercent)); }
    }

    public int TokenLimit
    {
        get => _tokenLimit;
        set { if (SetProperty(ref _tokenLimit, value)) OnPropertyChanged(nameof(TokenPercent)); }
    }

    public double TokenPercent => TokenLimit > 0 ? (double)TokenUsed / TokenLimit * 100 : 0;

    public int ActionsToday
    {
        get => _actionsToday;
        set => SetProperty(ref _actionsToday, value);
    }

    public int FilesProcessed
    {
        get => _filesProcessed;
        set => SetProperty(ref _filesProcessed, value);
    }

    public double TimeSavedMinutes
    {
        get => _timeSavedMinutes;
        set => SetProperty(ref _timeSavedMinutes, value);
    }

    private void OnQuickAction(object? parameter)
    {
        var action = parameter?.ToString() ?? "";
        _notificationService.ShowInfo("Quick Action", $"'{action}' will be available soon.");
    }

    private void UpdateGreeting()
    {
        var hour = DateTime.Now.Hour;
        Greeting = hour switch
        {
            < 12 => "Good Morning",
            < 17 => "Good Afternoon",
            < 21 => "Good Evening",
            _ => "Good Night"
        };
    }
}

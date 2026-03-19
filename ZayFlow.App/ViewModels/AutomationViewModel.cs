using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using ZayFlow.App.Commands;
using ZayFlow.App.Models;
using ZayFlow.App.Services;

namespace ZayFlow.App.ViewModels;

public class AutomationViewModel : ViewModelBase
{
    private readonly MainViewModel _mainViewModel;
    private readonly NotificationService _notificationService;
    private string _commandText = string.Empty;
    private bool _isProcessing;
    private string _ruleName = "";
    private string _ruleFileType = "image";
    private string _ruleDestination = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    private string _selectedSchedule = "Daily";

    public AutomationViewModel(MainViewModel mainViewModel, NotificationService notificationService)
    {
        _mainViewModel = mainViewModel;
        _notificationService = notificationService;

        PreviewCommand = new RelayCommand(_ => OnPreview(), _ => !IsProcessing);
        ExecuteCommand = new RelayCommand(_ => OnExecute(), _ => !IsProcessing);
        UndoCommand = new RelayCommand(_ => OnUndo(), _ => !IsProcessing);
        ProcessQueueCommand = new RelayCommand(_ => OnProcessQueue(), _ => !IsProcessing);
        AddRuleCommand = new RelayCommand(_ => AddRule());

        Schedules = new ObservableCollection<string> { "Daily", "Weekly", "Monthly" };
        Rules = new ObservableCollection<AutomationRule>();
    }

    public ICommand PreviewCommand { get; }
    public ICommand ExecuteCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand ProcessQueueCommand { get; }
    public ICommand AddRuleCommand { get; }

    public ObservableCollection<string> Schedules { get; }
    public ObservableCollection<AutomationRule> Rules { get; }

    public ObservableCollection<PreviewItemViewModel> PreviewItems => _mainViewModel.PreviewItems;

    public string CommandText
    {
        get => _commandText;
        set
        {
            if (SetProperty(ref _commandText, value))
                _mainViewModel.CommandText = value;
        }
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set => SetProperty(ref _isProcessing, value);
    }

    public string RuleName
    {
        get => _ruleName;
        set => SetProperty(ref _ruleName, value);
    }

    public string RuleFileType
    {
        get => _ruleFileType;
        set => SetProperty(ref _ruleFileType, value);
    }

    public string RuleDestination
    {
        get => _ruleDestination;
        set => SetProperty(ref _ruleDestination, value);
    }

    public string SelectedSchedule
    {
        get => _selectedSchedule;
        set => SetProperty(ref _selectedSchedule, value);
    }

    public int TokenUsed => _mainViewModel.TokenUsed;
    public int TokenLimit => _mainViewModel.TokenDailyLimit;
    public int QueueCount => _mainViewModel.QueueCount;

    private void OnPreview()
    {
        if (string.IsNullOrWhiteSpace(CommandText))
        {
            _notificationService.ShowWarning("Empty Command", "Please enter a command to preview.");
            return;
        }
        IsProcessing = true;
        try
        {
            _mainViewModel.CommandText = CommandText;
            _mainViewModel.PreviewCommand.Execute(null);
            _notificationService.ShowSuccess("Preview Generated", "Action preview is ready.");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError("Preview Failed", ex.Message);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void OnExecute()
    {
        if (string.IsNullOrWhiteSpace(CommandText))
        {
            _notificationService.ShowWarning("Empty Command", "Please enter a command to execute.");
            return;
        }
        IsProcessing = true;
        try
        {
            _mainViewModel.CommandText = CommandText;
            _mainViewModel.ExecuteCommand.Execute(null);
            _notificationService.ShowSuccess("Executed", "Action completed successfully.");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError("Execution Failed", ex.Message);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void OnUndo()
    {
        IsProcessing = true;
        try
        {
            _mainViewModel.UndoCommand.Execute(null);
            _notificationService.ShowInfo("Undo", "Last action has been undone.");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError("Undo Failed", ex.Message);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void OnProcessQueue()
    {
        IsProcessing = true;
        try
        {
            _mainViewModel.ProcessQueueCommand.Execute(null);
            _notificationService.ShowSuccess("Queue Processed", "All queued actions executed.");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError("Queue Failed", ex.Message);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void AddRule()
    {
        if (string.IsNullOrWhiteSpace(RuleName) || string.IsNullOrWhiteSpace(RuleFileType) || string.IsNullOrWhiteSpace(RuleDestination))
        {
            _notificationService.ShowWarning("Incomplete rule", "Please complete name, file type, and destination.");
            return;
        }

        Rules.Insert(0, new AutomationRule
        {
            Name = RuleName,
            FileType = RuleFileType,
            DestinationFolder = RuleDestination,
            Schedule = SelectedSchedule,
            Enabled = true
        });

        _notificationService.ShowSuccess("Rule added", $"{RuleName} scheduled {SelectedSchedule.ToLowerInvariant()}.");
    }
}

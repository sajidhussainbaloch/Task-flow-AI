using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Commands;
using ZayFlow.Backend.Contracts;
using ZayFlow.Backend.DTOs;

namespace ZayFlow.App.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly ILocalBackendService _backendService;
    private readonly ILogger<MainViewModel> _logger;

    private string _commandText = string.Empty;
    private bool _isProcessing;
    private bool _isDarkMode = true;
    private bool _isOnline;
    private int _tokenUsed;
    private int _tokenRemaining;
    private int _tokenDailyLimit = 100;
    private int _queueCount;
    private string _lastPreviewSummary = "No preview yet";
    private PlanDTO? _lastPlan;

    public MainViewModel(ILocalBackendService backendService, ILogger<MainViewModel> logger)
    {
        _backendService = backendService ?? throw new ArgumentNullException(nameof(backendService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        PreviewItems = new ObservableCollection<PreviewItemViewModel>();

        PreviewCommand = new RelayCommand(async _ => await OnPreviewAsync(), _ => CanPreviewOrExecute());
        ExecuteCommand = new RelayCommand(async _ => await OnExecuteAsync(), _ => CanPreviewOrExecute());
        UndoCommand = new RelayCommand(async _ => await OnUndoAsync(), _ => !IsProcessing);
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
        ToggleOnlineCommand = new RelayCommand(async _ => await ToggleOnlineAsync(), _ => !IsProcessing);
        ProcessQueueCommand = new RelayCommand(async _ => await ProcessQueueAsync(), _ => !IsProcessing && IsOnline && QueueCount > 0);

        _ = InitializeAsync();
    }

    public string CommandText
    {
        get => _commandText;
        set
        {
            if (SetProperty(ref _commandText, value))
            {
                RaiseAllCanExecute();
            }
        }
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        private set
        {
            if (SetProperty(ref _isProcessing, value))
            {
                RaiseAllCanExecute();
            }
        }
    }

    public bool IsDarkMode
    {
        get => _isDarkMode;
        private set => SetProperty(ref _isDarkMode, value);
    }

    public bool IsOnline
    {
        get => _isOnline;
        private set
        {
            if (SetProperty(ref _isOnline, value))
            {
                OnPropertyChanged(nameof(OnlineStatusLabel));
                OnPropertyChanged(nameof(StatusText));
                RaiseAllCanExecute();
            }
        }
    }

    public int TokenUsed
    {
        get => _tokenUsed;
        private set
        {
            if (SetProperty(ref _tokenUsed, value))
            {
                OnPropertyChanged(nameof(TokenUsagePercent));
            }
        }
    }

    public int TokenRemaining
    {
        get => _tokenRemaining;
        private set
        {
            if (SetProperty(ref _tokenRemaining, value))
            {
                OnPropertyChanged(nameof(TokenText));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public int TokenDailyLimit
    {
        get => _tokenDailyLimit;
        private set
        {
            if (SetProperty(ref _tokenDailyLimit, value))
            {
                OnPropertyChanged(nameof(TokenText));
                OnPropertyChanged(nameof(TokenUsagePercent));
            }
        }
    }

    public int QueueCount
    {
        get => _queueCount;
        private set
        {
            if (SetProperty(ref _queueCount, value))
            {
                OnPropertyChanged(nameof(QueueStatusText));
                OnPropertyChanged(nameof(StatusText));
                RaiseAllCanExecute();
            }
        }
    }

    public double TokenUsagePercent
    {
        get
        {
            if (TokenDailyLimit <= 0)
            {
                return 0;
            }

            return Math.Clamp((TokenUsed / (double)TokenDailyLimit) * 100d, 0d, 100d);
        }
    }

    public string TokenText => $"Tokens: {TokenRemaining}/{TokenDailyLimit}";

    public string QueueStatusText => QueueCount == 0 ? "Queue: empty" : $"Queue: {QueueCount} pending";

    public string OnlineStatusLabel => IsOnline ? "Online" : "Offline";

    public string StatusText => $"{(IsOnline ? "🟢 Online" : "🔴 Offline")} | {TokenText} | {QueueStatusText}";

    public string LastPreviewSummary
    {
        get => _lastPreviewSummary;
        private set => SetProperty(ref _lastPreviewSummary, value);
    }

    public ObservableCollection<PreviewItemViewModel> PreviewItems { get; }

    public ICommand PreviewCommand { get; }

    public ICommand ExecuteCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand ToggleThemeCommand { get; }

    public ICommand ToggleOnlineCommand { get; }

    public ICommand ProcessQueueCommand { get; }

    private async Task InitializeAsync()
    {
        try
        {
            await RefreshBackendStateAsync().ConfigureAwait(false);
            AddInfo("Backend ready. Operating in local mode.");
        }
        catch (Exception ex)
        {
            AddError($"Initialization failed: {ex.Message}");
            _logger.LogError(ex, "MainViewModel initialization failed");
        }
    }

    private bool CanPreviewOrExecute()
    {
        return !IsProcessing && !string.IsNullOrWhiteSpace(CommandText);
    }

    private async Task OnPreviewAsync()
    {
        try
        {
            IsProcessing = true;

            _lastPlan = await _backendService.GeneratePlanAsync(CommandText).ConfigureAwait(false);
            var preview = await _backendService.PreviewPlanAsync(_lastPlan).ConfigureAwait(false);

            PreviewItems.Clear();
            AddInfo($"Plan: {preview.Description}");
            AddRisk($"Overall risk: {preview.RiskLevel}", preview.RiskLevel);
            AddInfo($"Actions: {preview.Items.Count}");

            foreach (var item in preview.Items.Take(100))
            {
                var detail = $"{item.ActionType}: {item.SourcePath} -> {item.DestinationPath}";
                if (item.IsRisky)
                {
                    AddRisk(detail, "High", item.RiskReason);
                }
                else
                {
                    AddSafe(detail);
                }
            }

            if (preview.Items.Count > 100)
            {
                AddInfo($"... {preview.Items.Count - 100} more items");
            }

            LastPreviewSummary = $"{preview.Items.Count} actions, risk {preview.RiskLevel}";
        }
        catch (Exception ex)
        {
            PreviewItems.Clear();
            AddError($"Preview failed: {ex.Message}");
            LastPreviewSummary = "Preview failed";
            _logger.LogError(ex, "Preview failed");
        }
        finally
        {
            await RefreshBackendStateAsync().ConfigureAwait(false);
            IsProcessing = false;
        }
    }

    private async Task OnExecuteAsync()
    {
        try
        {
            IsProcessing = true;

            _lastPlan ??= await _backendService.GeneratePlanAsync(CommandText).ConfigureAwait(false);
            var result = await _backendService.ExecutePlanAsync(_lastPlan).ConfigureAwait(false);

            PreviewItems.Clear();

            if (result.WasQueued)
            {
                AddInfo(result.Message);
            }
            else
            {
                AddSafe($"Execution completed. Success: {result.SuccessCount}");
                if (result.FailureCount > 0)
                {
                    AddRisk($"Failures: {result.FailureCount}", "High");
                }
            }

            foreach (var failed in result.FailedItems.Take(50))
            {
                AddError($"{failed.ActionType}: {failed.Error}");
            }

            if (!string.IsNullOrWhiteSpace(result.Message) && !result.WasQueued)
            {
                AddInfo(result.Message);
            }
        }
        catch (Exception ex)
        {
            PreviewItems.Clear();
            AddError($"Execution failed: {ex.Message}");
            _logger.LogError(ex, "Execution failed");
        }
        finally
        {
            await RefreshBackendStateAsync().ConfigureAwait(false);
            IsProcessing = false;
        }
    }

    private async Task OnUndoAsync()
    {
        try
        {
            IsProcessing = true;

            var result = await _backendService.UndoLastAsync().ConfigureAwait(false);

            PreviewItems.Clear();
            AddSafe($"Undo completed. Success: {result.SuccessCount}");

            if (result.FailureCount > 0)
            {
                AddRisk($"Undo failures: {result.FailureCount}", "High");
            }

            foreach (var failed in result.FailedItems.Take(50))
            {
                AddError($"Undo failed for {failed.ActionType}: {failed.Error}");
            }
        }
        catch (Exception ex)
        {
            PreviewItems.Clear();
            AddError($"Undo failed: {ex.Message}");
            _logger.LogError(ex, "Undo failed");
        }
        finally
        {
            await RefreshBackendStateAsync().ConfigureAwait(false);
            IsProcessing = false;
        }
    }

    private void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
    }

    private async Task ToggleOnlineAsync()
    {
        try
        {
            IsProcessing = true;
            await _backendService.SetOnlineStatusAsync(!IsOnline).ConfigureAwait(false);
            await RefreshBackendStateAsync().ConfigureAwait(false);
            AddInfo(IsOnline ? "Switched to online mode" : "Switched to offline mode");
        }
        catch (Exception ex)
        {
            AddError($"Status switch failed: {ex.Message}");
            _logger.LogError(ex, "Toggle online failed");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            IsProcessing = true;

            var result = await _backendService.ProcessQueuedActionsAsync().ConfigureAwait(false);
            AddInfo(result.Message);

            if (result.FailureCount > 0)
            {
                AddRisk($"Queue failures: {result.FailureCount}", "Medium");
            }
        }
        catch (Exception ex)
        {
            AddError($"Queue processing failed: {ex.Message}");
            _logger.LogError(ex, "Queue processing failed");
        }
        finally
        {
            await RefreshBackendStateAsync().ConfigureAwait(false);
            IsProcessing = false;
        }
    }

    private async Task RefreshBackendStateAsync()
    {
        var token = await _backendService.GetTokenStatusAsync().ConfigureAwait(false);
        var online = await _backendService.GetOnlineStatusAsync().ConfigureAwait(false);
        var queued = await _backendService.GetQueuedCountAsync().ConfigureAwait(false);

        TokenUsed = token.Used;
        TokenRemaining = token.Remaining;
        TokenDailyLimit = token.DailyLimit;
        IsOnline = online;
        QueueCount = queued;
    }

    private void RaiseAllCanExecute()
    {
        (PreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ExecuteCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (UndoCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ToggleOnlineCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ProcessQueueCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void AddInfo(string text)
    {
        PreviewItems.Add(new PreviewItemViewModel(text, "Info"));
    }

    private void AddSafe(string text)
    {
        PreviewItems.Add(new PreviewItemViewModel(text, "Low"));
    }

    private void AddRisk(string text, string riskLevel, string? note = null)
    {
        var detail = string.IsNullOrWhiteSpace(note) ? text : $"{text} ({note})";
        PreviewItems.Add(new PreviewItemViewModel(detail, riskLevel));
    }

    private void AddError(string text)
    {
        PreviewItems.Add(new PreviewItemViewModel(text, "High"));
    }
}

public sealed class PreviewItemViewModel
{
    public PreviewItemViewModel(string text, string riskLevel)
    {
        Text = text;
        RiskLevel = riskLevel;
    }

    public string Text { get; }

    public string RiskLevel { get; }
}

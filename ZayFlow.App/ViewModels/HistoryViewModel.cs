using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ZayFlow.App.Commands;
using ZayFlow.App.Services;

namespace ZayFlow.App.ViewModels;

public class HistoryEntry
{
    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string DateText => Date.ToString("MMM dd, yyyy HH:mm");
    public string ActionType { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public int TokenCost { get; set; }
    public string Status { get; set; } = "Completed";
    public string StatusColor => Status == "Completed" ? "#34D399" : Status == "Failed" ? "#F87171" : "#FBBF24";
    public string Preview { get; set; } = string.Empty;
    public string Icon => "💬";
}

public class HistoryViewModel : ViewModelBase
{
    private readonly IAppPreferencesService _preferencesService;
    private string _searchText = string.Empty;
    private ObservableCollection<HistoryEntry> _filteredEntries = new();
    private HistoryEntry? _selectedSession = null;

    public HistoryViewModel(IAppPreferencesService preferencesService)
    {
        _preferencesService = preferencesService;
        AllEntries = new ObservableCollection<HistoryEntry>();
        _filteredEntries = new ObservableCollection<HistoryEntry>();

        FilterOptions = new ObservableCollection<string> { "All Sessions" };

        UndoCommand = new RelayCommand(_ => { });
        FilterCommand = new RelayCommand(OnFilter);
        RefreshCommand = new RelayCommand(_ => LoadFromPreferences());
        LoadSessionCommand = new RelayCommand(LoadSession);

        LoadFromPreferences();
    }

    public ObservableCollection<HistoryEntry> AllEntries { get; }

    public ObservableCollection<HistoryEntry> FilteredEntries
    {
        get => _filteredEntries;
        set => SetProperty(ref _filteredEntries, value);
    }

    public ObservableCollection<string> FilterOptions { get; }

    public HistoryEntry? SelectedSession
    {
        get => _selectedSession;
        set => SetProperty(ref _selectedSession, value);
    }

    public ICommand UndoCommand { get; }
    public ICommand FilterCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand LoadSessionCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
    }

    public int TotalActions => AllEntries.Count;
    public int TotalTokensUsed => 0;
    public bool IsSearchEmpty => string.IsNullOrEmpty(SearchText);
    public bool IsHistoryEmpty => FilteredEntries.Count == 0;

    private void ApplyFilter()
    {
        var filtered = AllEntries.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(e =>
                e.ActionType.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                e.Folder.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        FilteredEntries = new ObservableCollection<HistoryEntry>(filtered);
        OnPropertyChanged(nameof(TotalActions));
        OnPropertyChanged(nameof(TotalTokensUsed));
        OnPropertyChanged(nameof(IsSearchEmpty));
        OnPropertyChanged(nameof(IsHistoryEmpty));
    }

    private void OnFilter(object? parameter)
    {
        ApplyFilter();
    }

    private void LoadSession(object? parameter)
    {
        var entry = parameter as HistoryEntry ?? SelectedSession;
        if (entry == null) return;
        SelectedSession = entry;
        
        // Signal that a session was selected to load
        SessionSelected?.Invoke(this, entry);
    }

    public event EventHandler<HistoryEntry>? SessionSelected;

    private void LoadFromPreferences()
    {
        var sessions = _preferencesService.Get().ChatSessions
            .OrderByDescending(s => s.LastUpdated)
            .Select(s => new HistoryEntry
            {
                Id = s.Id,
                Date = s.LastUpdated,
                ActionType = string.IsNullOrWhiteSpace(s.Title) ? "Chat Session" : s.Title,
                Folder = $"{s.Messages.Count} messages",
                Preview = s.Preview,
                Status = "Completed",
                TokenCost = 0
            })
            .ToList();

        AllEntries.Clear();
        foreach (var session in sessions)
        {
            AllEntries.Add(session);
        }

        ApplyFilter();
    }
}

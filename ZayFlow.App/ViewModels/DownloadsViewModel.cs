using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ZayFlow.App.Commands;
using ZayFlow.App.Services;

namespace ZayFlow.App.ViewModels;

public class DownloadsViewModel : ViewModelBase
{
    private readonly DownloadManager _downloadManager;
    private string _filterMode = "All";

    public DownloadsViewModel(DownloadManager downloadManager)
    {
        _downloadManager = downloadManager ?? throw new ArgumentNullException(nameof(downloadManager));

        PauseCommand = new RelayCommand(o => { if (o is DownloadItem d) _downloadManager.PauseDownload(d); });
        ResumeCommand = new RelayCommand(o => { if (o is DownloadItem d) _downloadManager.ResumeDownload(d); });
        CancelCommand = new RelayCommand(o => { if (o is DownloadItem d) _downloadManager.CancelDownload(d); });
        RemoveCommand = new RelayCommand(o => { if (o is DownloadItem d) _downloadManager.RemoveDownload(d); });
        OpenFolderCommand = new RelayCommand(o => { if (o is DownloadItem d) _downloadManager.OpenFolder(d); });
        OpenFileCommand = new RelayCommand(o => { if (o is DownloadItem d) _downloadManager.OpenFile(d); });
        ClearFinishedCommand = new RelayCommand(_ => _downloadManager.ClearFinished());
        CancelAllCommand = new RelayCommand(_ => CancelAllActive());

        FilterAllCommand = new RelayCommand(_ => FilterMode = "All");
        FilterActiveCommand = new RelayCommand(_ => FilterMode = "Active");
        FilterCompletedCommand = new RelayCommand(_ => FilterMode = "Completed");
    }

    public ObservableCollection<DownloadItem> Downloads => _downloadManager.Downloads;

    public string FilterMode
    {
        get => _filterMode;
        set { SetProperty(ref _filterMode, value); OnPropertyChanged(nameof(FilteredDownloads)); }
    }

    public ObservableCollection<DownloadItem> FilteredDownloads
    {
        get
        {
            // For simplicity, return all — filtering applied via converter if needed
            // The XAML will bind directly to Downloads for live updates
            return Downloads;
        }
    }

    public int ActiveCount => Downloads.Count(d => d.IsActive);
    public int CompletedCount => Downloads.Count(d => d.Status == DownloadStatus.Completed);
    public int TotalCount => Downloads.Count;

    public bool HasDownloads => Downloads.Count > 0;
    public bool HasNoDownloads => Downloads.Count == 0;

    // Commands
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand ClearFinishedCommand { get; }
    public ICommand CancelAllCommand { get; }
    public ICommand FilterAllCommand { get; }
    public ICommand FilterActiveCommand { get; }
    public ICommand FilterCompletedCommand { get; }

    private void CancelAllActive()
    {
        foreach (var d in Downloads.Where(d => d.IsActive).ToList())
            _downloadManager.CancelDownload(d);
    }

    /// <summary>
    /// Refresh computed counts — call from UI when collection changes.
    /// </summary>
    public void RefreshCounts()
    {
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasDownloads));
        OnPropertyChanged(nameof(HasNoDownloads));
    }
}

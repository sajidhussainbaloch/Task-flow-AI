using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using ZayFlow.App.Commands;
using ZayFlow.App.Services;

namespace ZayFlow.App.ViewModels;

public sealed class FolderCountItem
{
    public string Folder { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class InsightsViewModel : ViewModelBase
{
    private readonly IInsightsService _insightsService;
    private readonly NotificationService _notificationService;

    private string _scanFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private bool _isLoading;
    private int _oldFilesCount;
    private int _duplicateCount;

    public InsightsViewModel(IInsightsService insightsService, NotificationService notificationService)
    {
        _insightsService = insightsService;
        _notificationService = notificationService;

        MostUsedFolders = new ObservableCollection<FolderCountItem>();
        LargestFiles = new ObservableCollection<string>();

        BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
        RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => !IsLoading);

        _ = RefreshAsync();
    }

    public ObservableCollection<FolderCountItem> MostUsedFolders { get; }
    public ObservableCollection<string> LargestFiles { get; }

    public ICommand BrowseFolderCommand { get; }
    public ICommand RefreshCommand { get; }

    public string ScanFolder
    {
        get => _scanFolder;
        set => SetProperty(ref _scanFolder, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public int OldFilesCount
    {
        get => _oldFilesCount;
        set => SetProperty(ref _oldFilesCount, value);
    }

    public int DuplicateCount
    {
        get => _duplicateCount;
        set => SetProperty(ref _duplicateCount, value);
    }

    private void BrowseFolder()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select any file in the folder",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() == true)
        {
            var folder = Path.GetDirectoryName(dialog.FileName);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                ScanFolder = folder;
            }
        }
    }

    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var data = await _insightsService.GetInsightsAsync(ScanFolder);
            MostUsedFolders.Clear();
            foreach (var item in data.MostUsedFolders)
            {
                MostUsedFolders.Add(new FolderCountItem { Folder = item.Folder, Count = item.Count });
            }

            LargestFiles.Clear();
            foreach (var item in data.LargestFiles)
            {
                LargestFiles.Add(item);
            }

            OldFilesCount = data.OldFilesCount;
            DuplicateCount = data.DuplicateGroups;
        }
        catch (Exception ex)
        {
            _notificationService.ShowError("Insights failed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }
}

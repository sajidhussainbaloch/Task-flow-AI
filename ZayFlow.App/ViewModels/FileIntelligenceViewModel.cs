using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ZayFlow.App.Commands;
using ZayFlow.App.Models;
using ZayFlow.App.Services;

namespace ZayFlow.App.ViewModels;

public sealed class FileIntelligenceViewModel : ViewModelBase
{
    private readonly IFileIntelligenceService _fileIntelligenceService;
    private readonly NotificationService _notificationService;
    private readonly IAppPreferencesService _preferencesService;

    private string _selectedFilePath = string.Empty;
    private bool _isAnalyzing;
    private bool _requiresUploadConfirmation = true;
    private FileAnalysisResult _result = new();

    public FileIntelligenceViewModel(IFileIntelligenceService fileIntelligenceService, NotificationService notificationService, IAppPreferencesService preferencesService)
    {
        _fileIntelligenceService = fileIntelligenceService;
        _notificationService = notificationService;
        _preferencesService = preferencesService;

        SupportedTypes = new ObservableCollection<string> { "TXT", "PDF", "DOCX", "CSV" };
        BrowseFileCommand = new RelayCommand(_ => BrowseFile());
        AnalyzeFileCommand = new RelayCommand(async _ => await AnalyzeAsync(), _ => !IsAnalyzing && !string.IsNullOrWhiteSpace(SelectedFilePath));
        ClearCommand = new RelayCommand(_ => Clear());
    }

    public ObservableCollection<string> SupportedTypes { get; }
    public ICommand BrowseFileCommand { get; }
    public ICommand AnalyzeFileCommand { get; }
    public ICommand ClearCommand { get; }

    public string SelectedFilePath
    {
        get => _selectedFilePath;
        set
        {
            if (SetProperty(ref _selectedFilePath, value))
            {
                (AnalyzeFileCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        set
        {
            if (SetProperty(ref _isAnalyzing, value))
            {
                (AnalyzeFileCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool RequiresUploadConfirmation
    {
        get => _requiresUploadConfirmation;
        set => SetProperty(ref _requiresUploadConfirmation, value);
    }

    public FileAnalysisResult Result
    {
        get => _result;
        set => SetProperty(ref _result, value);
    }

    private void BrowseFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Supported Files|*.txt;*.pdf;*.docx;*.csv|Text|*.txt|PDF|*.pdf|Word|*.docx|CSV|*.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedFilePath = dialog.FileName;
        }
    }

    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFilePath))
        {
            return;
        }

        var preferences = _preferencesService.Get();
        if (preferences.DisableFileUpload)
        {
            _notificationService.ShowWarning("Upload disabled", "File upload is disabled in Privacy Settings.");
            return;
        }

        RequiresUploadConfirmation = preferences.RequireFileUploadConfirmation;

        if (RequiresUploadConfirmation)
        {
            var confirmation = MessageBox.Show(
                "This file will be sent to the AI for analysis.",
                "Privacy Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }
        }

        IsAnalyzing = true;
        try
        {
            var (success, error, content) = await _fileIntelligenceService.ReadSupportedFileAsync(SelectedFilePath);
            if (!success)
            {
                _notificationService.ShowError("File analysis failed", error);
                return;
            }

            var analysis = await _fileIntelligenceService.AnalyzeFileAsync(Path.GetFileName(SelectedFilePath), content);
            Result = analysis;
            _notificationService.ShowSuccess("Analysis complete", "File analyzed successfully.");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError("AI service unavailable", ex.Message);
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private void Clear()
    {
        SelectedFilePath = string.Empty;
        Result = new FileAnalysisResult();
    }
}

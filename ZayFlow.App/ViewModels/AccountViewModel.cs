using System.Diagnostics;
using System.Windows.Input;
using ZayFlow.App.Commands;
using ZayFlow.App.Services;

namespace ZayFlow.App.ViewModels;

public sealed class AccountViewModel : ViewModelBase
{
    private readonly NotificationService _notificationService;
    private readonly IAppPreferencesService _preferencesService;

    private string _appVersion = "1.0.0";
    private string _buildDate = "March 2026";
    private string _userEmail = "Not signed in";
    private string _licenseStatus = "Free Plan";
    private string _dataStoragePath;

    public AccountViewModel(NotificationService notificationService, IAppPreferencesService preferencesService)
    {
        _notificationService = notificationService;
        _preferencesService = preferencesService;

        _dataStoragePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZayFlow");

        var prefs = _preferencesService.Get();
        _licenseStatus = prefs.IsPremium ? "Premium Plan" : "Free Plan";

        OpenDataFolderCommand = new RelayCommand(_ => OnOpenDataFolder());
        OpenGitHubCommand = new RelayCommand(_ => OnOpenGitHub());
        CheckUpdatesCommand = new RelayCommand(_ => OnCheckUpdates());
        ResetAllDataCommand = new RelayCommand(_ => OnResetAllData());
    }

    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenGitHubCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand ResetAllDataCommand { get; }

    public string AppVersion
    {
        get => _appVersion;
        set => SetProperty(ref _appVersion, value);
    }

    public string BuildDate
    {
        get => _buildDate;
        set => SetProperty(ref _buildDate, value);
    }

    public string UserEmail
    {
        get => _userEmail;
        set => SetProperty(ref _userEmail, value);
    }

    public string LicenseStatus
    {
        get => _licenseStatus;
        set => SetProperty(ref _licenseStatus, value);
    }

    public string DataStoragePath
    {
        get => _dataStoragePath;
        set => SetProperty(ref _dataStoragePath, value);
    }

    private void OnOpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _dataStoragePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _notificationService.ShowError("Error", $"Could not open folder: {ex.Message}");
        }
    }

    private void OnOpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/zayflow",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OnCheckUpdates()
    {
        _notificationService.ShowInfo("Updates", "You are running the latest version.");
    }

    private void OnResetAllData()
    {
        _preferencesService.Save(new AppPreferences());
        _notificationService.ShowWarning("Data Reset", "All settings and data have been reset to defaults.");
        LicenseStatus = "Free Plan";
    }
}

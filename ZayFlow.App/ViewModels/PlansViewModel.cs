using System.Windows.Input;
using ZayFlow.App.Commands;
using ZayFlow.App.Services;

namespace ZayFlow.App.ViewModels;

public class PlanFeature
{
    public string Text { get; set; } = string.Empty;
    public bool IsIncluded { get; set; } = true;
}

public class PlansViewModel : ViewModelBase
{
    private readonly NotificationService _notificationService;
    private string _currentPlan = "Free";

    public PlansViewModel(NotificationService notificationService)
    {
        _notificationService = notificationService;
        UpgradeCommand = new RelayCommand(_ => OnUpgrade());
    }

    public ICommand UpgradeCommand { get; }

    public string CurrentPlan
    {
        get => _currentPlan;
        set => SetProperty(ref _currentPlan, value);
    }

    public bool IsFree => CurrentPlan == "Free";

    private void OnUpgrade()
    {
        _notificationService.ShowInfo("Upgrade", "Pro plan coming soon! Stay tuned for premium features.");
    }
}

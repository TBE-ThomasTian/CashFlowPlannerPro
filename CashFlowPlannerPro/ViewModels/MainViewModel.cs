using CommunityToolkit.Mvvm.ComponentModel;

namespace CashFlowPlannerPro.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string currentUser = string.Empty;

    [ObservableProperty]
    private string databaseName = string.Empty;
}

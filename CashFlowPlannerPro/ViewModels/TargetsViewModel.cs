using System;
using System.Collections.ObjectModel;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CashFlowPlannerPro.ViewModels;

public partial class TargetsViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<Target> targets = new();
    [ObservableProperty] private Target? selectedTarget;

    public void Load()
    {
        Targets = new ObservableCollection<Target>(Database.Instance.GetTargets_List());
    }

    [RelayCommand]
    private void Add()
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Targets, "target.add")) return;
        var t = new Target {
            Year = DateTime.Today.Year,
            Month = DateTime.Today.Month,
            Amount = 0
        };
        Database.Instance.AddTarget(t);
        Targets.Add(t);
        SelectedTarget = t;
    }

    [RelayCommand]
    private void Delete()
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Targets, "target.delete")) return;
        if (SelectedTarget == null) return;
        Database.Instance.DeleteTarget(SelectedTarget.Id);
        Targets.Remove(SelectedTarget);
    }

    public void Save(Target t)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Targets, "target.update")) return;
        if (t.Id > 0) Database.Instance.UpdateTarget(t);
    }
}

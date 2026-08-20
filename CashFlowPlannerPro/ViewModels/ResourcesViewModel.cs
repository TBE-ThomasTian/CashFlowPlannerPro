using System.Collections.ObjectModel;
using System.Windows;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CashFlowPlannerPro.ViewModels;

public partial class ResourcesViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Resource> resources = [];

    [ObservableProperty]
    private ObservableCollection<Project> projects = [];

    [ObservableProperty]
    private List<ResourceAllocation> allocations = [];

    [ObservableProperty]
    private ObservableCollection<HardwareResource> hardwareResources = [];

    [ObservableProperty]
    private List<HardwareAllocation> hardwareAllocations = [];

    [ObservableProperty]
    private DateTime currentDate = StartOfWeek(DateTime.Today);

    [ObservableProperty]
    private string viewMode = "Week";

    [ObservableProperty]
    private int daysToShow = 7;

    [ObservableProperty]
    private Resource? selectedResource;

    [ObservableProperty]
    private Project? selectedProject;

    public event Action? CalendarChanged;

    public void Load()
    {
        Resources = new ObservableCollection<Resource>(Database.Instance.GetResources());
        Projects = new ObservableCollection<Project>(Database.Instance.GetProjects());
        HardwareResources = new ObservableCollection<HardwareResource>(Database.Instance.GetHardwareResources());
        var (start, end) = GetDateRange();
        // One buffered day on each side is enough to mark a bar as continuing
        // beyond the visible period without loading an entire multi-month plan.
        Allocations = Database.Instance.GetAllocations(start.AddDays(-1), end.AddDays(1));
        HardwareAllocations = Database.Instance.GetHardwareAllocations(start.AddDays(-1), end.AddDays(1));
        CalendarChanged?.Invoke();
    }

    [RelayCommand]
    private void NavigatePrevious()
    {
        CurrentDate = CurrentDate.AddDays(-DaysToShow);
        Load();
    }

    [RelayCommand]
    private void NavigateNext()
    {
        CurrentDate = CurrentDate.AddDays(DaysToShow);
        Load();
    }

    [RelayCommand]
    private void NavigateToday()
    {
        CurrentDate = StartOfWeek(DateTime.Today);
        Load();
    }

    [RelayCommand]
    private void AddResource()
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "resource.add")) return;
        var name = PromptInput("Neuer Mitarbeiter", "Name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "resource.add.confirmed")) return;
        var res = new Resource { Name = name };
        Database.Instance.AddResource(res);
        Load();
    }

    [RelayCommand]
    private void AddProject()
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "project.add")) return;
        var name = PromptInput("Neues Projekt", "Projektname:");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "project.add.confirmed")) return;
        var p = new Project { Name = name };
        Database.Instance.AddProject(p);
        Load();
    }

    public void SetViewMode(string mode)
    {
        ViewMode = mode;
        DaysToShow = mode switch {
            "Day" => 1,
            "Month" => 30,
            _ => 7
        };
        if (mode == "Week") CurrentDate = StartOfWeek(CurrentDate);
        Load();
    }

    public void DeleteAllocation(long id)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "resource_allocation.delete")) return;
        Database.Instance.DeleteAllocation(id);
        Load();
    }

    public void DeleteProject(long id)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "project.delete")) return;
        Database.Instance.DeleteProject(id);
        Load();
    }

    public void MoveAllocation(long fromResourceId, long toResourceId, long projectId, DateTime startDate, DateTime endDate)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "resource_allocation.move")) return;
        Database.Instance.MoveAllocations(
            fromResourceId, toResourceId, projectId, startDate, endDate);
        Load();
    }

    public void UpdateProjectColor(long id, string color)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "project.color.update")) return;
        var project = Projects.FirstOrDefault(p => p.Id == id);
        if (project == null) return;
        project.Color = color;
        Database.Instance.UpdateProject(project);
        Load();
    }

    public void AddAllocation(long resourceId, long projectId, DateTime date)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "resource_allocation.add")) return;
        var a = new ResourceAllocation {
            ResourceId = resourceId,
            ProjectId = projectId,
            Date = date.ToString("yyyy-MM-dd"),
            Hours = 8.0
        };
        Database.Instance.AddAllocation(a);
        Load();
    }

    public ResourceAllocation? GetAllocation(long resourceId, DateTime date)
    {
        var ds = date.ToString("yyyy-MM-dd");
        return Allocations.FirstOrDefault(a => a.ResourceId == resourceId && a.Date == ds);
    }

    public List<ResourceAllocation> GetAllocations(long resourceId, DateTime date)
    {
        var ds = date.ToString("yyyy-MM-dd");
        return Allocations.Where(a => a.ResourceId == resourceId && a.Date == ds).ToList();
    }

    public (DateTime Start, DateTime End, double StartHours, double EndHours)? GetAllocationRange(
        long resourceId, long projectId, DateTime anchorDate)
    {
        return Database.Instance.GetAllocationRange(resourceId, projectId, anchorDate);
    }

    public void AddAllocationsRange(long resourceId, long projectId, DateTime from, DateTime to, double hours = 8.0)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "resource_allocation.range_add")) return;
        Database.Instance.AddAllocationsRange(resourceId, projectId, from, to, hours);
        Load();
    }

    public void DeleteAllocationsRange(long resourceId, long projectId, DateTime from, DateTime to)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "resource_allocation.range_delete")) return;
        Database.Instance.DeleteAllocationsRange(resourceId, projectId, from, to);
        Load();
    }

    // Hardware resource methods
    [RelayCommand]
    private void AddHardware()
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "hardware.add")) return;
        var name = PromptInput("Neue Hardware", "Name (z.B. HPC Cluster, AWS EC2):");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "hardware.add.confirmed")) return;
        var hw = new HardwareResource { Name = name, Type = "Server" };
        Database.Instance.AddHardwareResource(hw);
        Load();
    }

    public void DeleteHardware(long id)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "hardware.delete")) return;
        Database.Instance.DeleteHardwareResource(id);
        Load();
    }

    public void UpdateHardwareColor(long id, string color)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "hardware.color.update")) return;
        var hw = HardwareResources.FirstOrDefault(h => h.Id == id);
        if (hw == null) return;
        hw.Color = color;
        Database.Instance.UpdateHardwareResource(hw);
        Load();
    }

    public void AddHardwareAllocation(long resourceId, long hardwareId, long projectId, DateTime date)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "hardware_allocation.add")) return;
        var a = new HardwareAllocation {
            ResourceId = resourceId, HardwareId = hardwareId,
            ProjectId = projectId, Date = date.ToString("yyyy-MM-dd"), Hours = 8.0
        };
        Database.Instance.AddHardwareAllocation(a);
        Load();
    }

    public void DeleteHardwareAllocation(long id)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "hardware_allocation.delete")) return;
        Database.Instance.DeleteHardwareAllocation(id);
        Load();
    }

    public void AddHardwareAllocationsRange(long resourceId, long hardwareId, long projectId, DateTime from, DateTime to, double hours = 8.0)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "hardware_allocation.range_add")) return;
        Database.Instance.AddHardwareAllocationsRange(resourceId, hardwareId, projectId, from, to, hours);
        Load();
    }

    public void DeleteHardwareAllocationsRange(long resourceId, long hardwareId, long projectId, DateTime from, DateTime to)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "hardware_allocation.range_delete")) return;
        Database.Instance.DeleteHardwareAllocationsRange(resourceId, hardwareId, projectId, from, to);
        Load();
    }

    public List<HardwareAllocation> GetHardwareAllocationsForResource(long resourceId, DateTime date)
    {
        var ds = date.ToString("yyyy-MM-dd");
        return HardwareAllocations.Where(a => a.ResourceId == resourceId && a.Date == ds).ToList();
    }

    public (DateTime Start, DateTime End, double StartHours, double EndHours)? GetHardwareAllocationRange(
        long resourceId, long hardwareId, long projectId, DateTime anchorDate)
    {
        return Database.Instance.GetHardwareAllocationRange(resourceId, hardwareId, projectId, anchorDate);
    }

    public (DateTime start, DateTime end) GetDateRange()
    {
        return (CurrentDate, CurrentDate.AddDays(DaysToShow - 1));
    }

    public string DateRangeText
    {
        get {
            var (start, end) = GetDateRange();
            return DaysToShow == 1
                ? start.ToString("dd.MM.yyyy")
                : $"{start:dd.MM} - {end:dd.MM.yyyy}";
        }
    }

    partial void OnCurrentDateChanged(DateTime value) => OnPropertyChanged(nameof(DateRangeText));

    private static DateTime StartOfWeek(DateTime date)
    {
        int diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-diff).Date;
    }

    private static string? PromptInput(string title, string label)
    {
        var dlg = new Window {
            Title = title, Width = 350, Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0x21, 0x3e))
        };
        var sp = new System.Windows.Controls.StackPanel { Margin = new Thickness(15) };
        var lbl = new System.Windows.Controls.TextBlock {
            Text = label, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 5)
        };
        var tb = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 0, 0, 10) };
        var btn = new System.Windows.Controls.Button {
            Content = "OK", Width = 80, HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true
        };
        btn.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };
        sp.Children.Add(lbl);
        sp.Children.Add(tb);
        sp.Children.Add(btn);
        dlg.Content = sp;
        if (Application.Current?.MainWindow != null) dlg.Owner = Application.Current.MainWindow;
        return dlg.ShowDialog() == true ? tb.Text : null;
    }
}

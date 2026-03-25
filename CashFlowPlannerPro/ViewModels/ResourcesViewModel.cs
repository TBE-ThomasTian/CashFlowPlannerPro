using System.Collections.ObjectModel;
using System.Windows;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
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
        Allocations = Database.Instance.GetAllocations(start, end);
        HardwareAllocations = Database.Instance.GetHardwareAllocations(start, end);
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
        var name = PromptInput("Neuer Mitarbeiter", "Name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        var res = new Resource { Name = name };
        Database.Instance.AddResource(res);
        Load();
    }

    [RelayCommand]
    private void AddProject()
    {
        var name = PromptInput("Neues Projekt", "Projektname:");
        if (string.IsNullOrWhiteSpace(name)) return;
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
        Database.Instance.DeleteAllocation(id);
        Load();
    }

    public void DeleteProject(long id)
    {
        Database.Instance.DeleteProject(id);
        Load();
    }

    public void MoveAllocation(long fromResourceId, long toResourceId, long projectId, DateTime startDate, DateTime endDate)
    {
        // Delete from old resource
        for (var d = startDate; d <= endDate; d = d.AddDays(1))
        {
            var ds = d.ToString("yyyy-MM-dd");
            var alloc = Allocations.FirstOrDefault(a => a.ResourceId == fromResourceId && a.ProjectId == projectId && a.Date == ds);
            if (alloc != null) Database.Instance.DeleteAllocation(alloc.Id);
        }
        // Add to new resource
        for (var d = startDate; d <= endDate; d = d.AddDays(1))
        {
            var ds = d.ToString("yyyy-MM-dd");
            if (!Allocations.Any(a => a.ResourceId == toResourceId && a.ProjectId == projectId && a.Date == ds))
            {
                Database.Instance.AddAllocation(new ResourceAllocation {
                    ResourceId = toResourceId, ProjectId = projectId,
                    Date = ds, Hours = 8.0
                });
            }
        }
        Load();
    }

    public void UpdateProjectColor(long id, string color)
    {
        var project = Projects.FirstOrDefault(p => p.Id == id);
        if (project == null) return;
        project.Color = color;
        Database.Instance.UpdateProject(project);
        Load();
    }

    public void AddAllocation(long resourceId, long projectId, DateTime date)
    {
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

    public void AddAllocationsRange(long resourceId, long projectId, DateTime from, DateTime to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var ds = d.ToString("yyyy-MM-dd");
            if (!Allocations.Any(a => a.ResourceId == resourceId && a.ProjectId == projectId && a.Date == ds))
            {
                Database.Instance.AddAllocation(new ResourceAllocation {
                    ResourceId = resourceId, ProjectId = projectId,
                    Date = ds, Hours = 8.0
                });
            }
        }
        Load();
    }

    public void DeleteAllocationsRange(long resourceId, long projectId, DateTime from, DateTime to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var ds = d.ToString("yyyy-MM-dd");
            var alloc = Allocations.FirstOrDefault(a => a.ResourceId == resourceId && a.ProjectId == projectId && a.Date == ds);
            if (alloc != null) Database.Instance.DeleteAllocation(alloc.Id);
        }
        Load();
    }

    // Hardware resource methods
    [RelayCommand]
    private void AddHardware()
    {
        var name = PromptInput("Neue Hardware", "Name (z.B. HPC Cluster, AWS EC2):");
        if (string.IsNullOrWhiteSpace(name)) return;
        var hw = new HardwareResource { Name = name, Type = "Server" };
        Database.Instance.AddHardwareResource(hw);
        Load();
    }

    public void DeleteHardware(long id)
    {
        Database.Instance.DeleteHardwareResource(id);
        Load();
    }

    public void UpdateHardwareColor(long id, string color)
    {
        var hw = HardwareResources.FirstOrDefault(h => h.Id == id);
        if (hw == null) return;
        hw.Color = color;
        Database.Instance.UpdateHardwareResource(hw);
        Load();
    }

    public void AddHardwareAllocation(long resourceId, long hardwareId, long projectId, DateTime date)
    {
        var a = new HardwareAllocation {
            ResourceId = resourceId, HardwareId = hardwareId,
            ProjectId = projectId, Date = date.ToString("yyyy-MM-dd"), Hours = 8.0
        };
        Database.Instance.AddHardwareAllocation(a);
        Load();
    }

    public void DeleteHardwareAllocation(long id)
    {
        Database.Instance.DeleteHardwareAllocation(id);
        Load();
    }

    public void AddHardwareAllocationsRange(long resourceId, long hardwareId, long projectId, DateTime from, DateTime to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var ds = d.ToString("yyyy-MM-dd");
            if (!HardwareAllocations.Any(a => a.ResourceId == resourceId && a.HardwareId == hardwareId
                && a.ProjectId == projectId && a.Date == ds))
            {
                Database.Instance.AddHardwareAllocation(new HardwareAllocation {
                    ResourceId = resourceId, HardwareId = hardwareId,
                    ProjectId = projectId, Date = ds, Hours = 8.0
                });
            }
        }
        Load();
    }

    public void DeleteHardwareAllocationsRange(long resourceId, long hardwareId, long projectId, DateTime from, DateTime to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var ds = d.ToString("yyyy-MM-dd");
            var alloc = HardwareAllocations.FirstOrDefault(a => a.ResourceId == resourceId
                && a.HardwareId == hardwareId && a.ProjectId == projectId && a.Date == ds);
            if (alloc != null) Database.Instance.DeleteHardwareAllocation(alloc.Id);
        }
        Load();
    }

    public List<HardwareAllocation> GetHardwareAllocationsForResource(long resourceId, DateTime date)
    {
        var ds = date.ToString("yyyy-MM-dd");
        return HardwareAllocations.Where(a => a.ResourceId == resourceId && a.Date == ds).ToList();
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

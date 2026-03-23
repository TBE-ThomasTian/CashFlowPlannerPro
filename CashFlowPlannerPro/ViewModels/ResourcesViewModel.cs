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
        var (start, end) = GetDateRange();
        Allocations = Database.Instance.GetAllocations(start, end);
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

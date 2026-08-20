using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class TimeTrackingView : UserControl
{
    private List<Project> _projects = [];
    private TimeEntry? _runningEntry;
    private bool _isSubscribed;

    public TimeTrackingView()
    {
        InitializeComponent();
        StartButton.ToolTip = TooltipService.Get("Btn_StartTimer");
        StopButton.ToolTip = TooltipService.Get("Btn_StopTimer");
        ApplyLocalization();
        Loaded += (_, _) =>
        {
            if (_isSubscribed) return;
            LocalizationManager.LanguageChanged += OnLanguageChanged;
            _isSubscribed = true;
        };
        IsVisibleChanged += (_, e) => {
            if (e.NewValue is true)
                Refresh();
        };
        Unloaded += (_, _) =>
        {
            if (!_isSubscribed) return;
            LocalizationManager.LanguageChanged -= OnLanguageChanged;
            _isSubscribed = false;
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLocalization();
        BuildActivityCombo();
        UpdateSubtitle();
        UpdateRunningState();
    }

    private void Refresh()
    {
        try
        {
            _projects = Database.Instance.GetProjects().OrderBy(p => p.Name).ToList();
            BuildProjectCombo();
            BuildActivityCombo();

            _runningEntry = Database.Instance.GetRunningTimeEntry(App.CurrentUserId);
            EntriesGrid.ItemsSource = Database.Instance.GetTimeEntries(App.CurrentUserId, 50);
            ProjectSummaryGrid.ItemsSource = Database.Instance.GetProjectTimeSummary(App.CurrentUserId);
            ActivitySummaryGrid.ItemsSource = Database.Instance.GetActivityTimeSummary(App.CurrentUserId);
            UpdateSubtitle();
            UpdateRunningState();
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException("time_tracking.load_failed", ex);
            ModernMessageBox.ShowError($"Die Zeiterfassung konnte nicht geladen werden. Referenz: {reference}", LocalizationManager.Get("TimeTrackingTitleShort"));
        }
    }

    private void ApplyLocalization()
    {
        TitleText.Text = LocalizationManager.Get("TimeTrackingTitle");
        ProjectLabel.Text = LocalizationManager.Get("TimeTrackingProject");
        ActivityLabel.Text = LocalizationManager.Get("TimeTrackingActivity");
        DescriptionLabel.Text = LocalizationManager.Get("TimeTrackingDescription");
        StartButton.Content = LocalizationManager.Get("TimeTrackingStart");
        StopButton.Content = LocalizationManager.Get("TimeTrackingStop");
        RecentEntriesTitle.Text = LocalizationManager.Get("TimeTrackingRecentEntries");
        HoursByProjectTitle.Text = LocalizationManager.Get("TimeTrackingHoursByProject");
        HoursByActivityTitle.Text = LocalizationManager.Get("TimeTrackingHoursByActivity");

        EntryDateColumn.Header = LocalizationManager.Get("TimeTrackingDate");
        EntryProjectColumn.Header = LocalizationManager.Get("TimeTrackingProject");
        EntryActivityColumn.Header = LocalizationManager.Get("TimeTrackingActivity");
        EntryFromColumn.Header = LocalizationManager.Get("TimeTrackingFrom");
        EntryToColumn.Header = LocalizationManager.Get("TimeTrackingTo");
        EntryHoursColumn.Header = LocalizationManager.Get("TimeTrackingHours");
        ProjectSummaryLabelColumn.Header = LocalizationManager.Get("TimeTrackingProject");
        ProjectSummaryHoursColumn.Header = LocalizationManager.Get("TimeTrackingHours");
        ActivitySummaryLabelColumn.Header = LocalizationManager.Get("TimeTrackingActivity");
        ActivitySummaryHoursColumn.Header = LocalizationManager.Get("TimeTrackingHours");
    }

    private void UpdateSubtitle()
    {
        var displayName = Database.Instance.GetFullName(App.CurrentUsername);
        SubtitleText.Text = string.IsNullOrWhiteSpace(displayName)
            ? string.Format(LocalizationManager.Get("TimeTrackingSubtitle"), App.CurrentUsername)
            : string.Format(LocalizationManager.Get("TimeTrackingSubtitleWithName"), displayName, App.CurrentUsername);
    }

    private void BuildProjectCombo()
    {
        var selectedProjectId = (ProjectCombo.SelectedItem as ComboBoxItem)?.Tag as long?;
        ProjectCombo.Items.Clear();
        foreach (var project in _projects)
        {
            var item = new ComboBoxItem { Content = project.Name, Tag = project.Id };
            if (selectedProjectId == project.Id)
                item.IsSelected = true;
            ProjectCombo.Items.Add(item);
        }

        if (ProjectCombo.SelectedItem == null && ProjectCombo.Items.Count > 0)
            ((ComboBoxItem)ProjectCombo.Items[0]).IsSelected = true;
    }

    private void BuildActivityCombo()
    {
        var selectedActivity = (ActivityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        ActivityCombo.Items.Clear();
        foreach (var activity in GetActivityTypes())
        {
            var item = new ComboBoxItem { Content = activity };
            if (selectedActivity == activity)
                item.IsSelected = true;
            ActivityCombo.Items.Add(item);
        }

        if (ActivityCombo.SelectedItem == null && ActivityCombo.Items.Count > 0)
            ((ComboBoxItem)ActivityCombo.Items[0]).IsSelected = true;
    }

    private void UpdateRunningState()
    {
        var canEdit = App.CanEdit(PageKeys.TimeTracking);
        ProjectCombo.IsEnabled = canEdit && _runningEntry == null;
        ActivityCombo.IsEnabled = canEdit && _runningEntry == null;
        DescriptionTextBox.IsEnabled = canEdit && _runningEntry == null;
        if (_runningEntry == null)
        {
            RunningEntryText.Text = LocalizationManager.Get("TimeTrackingNoRunning");
            StartButton.IsEnabled = canEdit;
            StopButton.IsEnabled = false;
            return;
        }

        string started = FormatDateTime(_runningEntry.StartTime);
        string description = string.IsNullOrWhiteSpace(_runningEntry.Description) ? "" : $" · {_runningEntry.Description}";
        RunningEntryText.Text = string.Format(
            LocalizationManager.Get("TimeTrackingRunningSince"),
            started,
            _runningEntry.ProjectName,
            _runningEntry.ActivityType,
            description);
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = canEdit;
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.TimeTracking, "time_entry.start"))
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("TimeTrackingAccessDenied"),
                LocalizationManager.Get("AccessDeniedTitle"));
            return;
        }

        if (ProjectCombo.SelectedItem is not ComboBoxItem projectItem || projectItem.Tag is not long projectId)
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("TimeTrackingSelectProject"),
                LocalizationManager.Get("TimeTrackingTitleShort"));
            return;
        }

        var activity = (ActivityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(activity))
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("TimeTrackingSelectActivity"),
                LocalizationManager.Get("TimeTrackingTitleShort"));
            return;
        }

        Database.Instance.StartTimeEntry(App.CurrentUserId, projectId, activity, DescriptionTextBox.Text.Trim());
        DescriptionTextBox.Clear();
        Refresh();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.TimeTracking, "time_entry.stop"))
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("TimeTrackingAccessDenied"),
                LocalizationManager.Get("AccessDeniedTitle"));
            return;
        }

        if (_runningEntry == null) return;
        Database.Instance.StopTimeEntry(_runningEntry.Id);
        Refresh();
    }

    private static string FormatDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "-";
        if (Database.TryParseStoredTimeUtc(value, out var utc))
            return utc.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture);
        return value;
    }

    private static IReadOnlyList<string> GetActivityTypes() =>
    [
        LocalizationManager.Get("ActivityPlanning"),
        LocalizationManager.Get("ActivityEngineering"),
        LocalizationManager.Get("ActivityCad"),
        LocalizationManager.Get("ActivityMeeting"),
        LocalizationManager.Get("ActivityDocumentation"),
        LocalizationManager.Get("ActivityCoordination"),
        LocalizationManager.Get("ActivityOnSite"),
        LocalizationManager.Get("ActivitySupport")
    ];
}

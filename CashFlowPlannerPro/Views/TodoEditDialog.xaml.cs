using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class TodoEditDialog : Window
{
    private readonly UserTodo _todo;
    public UserTodo? Result { get; private set; }

    public TodoEditDialog(UserTodo todo, List<Project> projects)
    {
        InitializeComponent();
        _todo = todo;
        if (Application.Current?.MainWindow != null) Owner = Application.Current.MainWindow;

        // Populate fields
        TbTitle.Text = todo.Title;
        TbDescription.Text = todo.Description ?? "";

        // Status
        foreach (ComboBoxItem item in CbStatus.Items)
            if (item.Content?.ToString() == todo.Status) { CbStatus.SelectedItem = item; break; }
        if (CbStatus.SelectedItem == null) CbStatus.SelectedIndex = 0;

        // Priority
        foreach (ComboBoxItem item in CbPriority.Items)
            if (item.Tag?.ToString() == todo.Priority.ToString()) { CbPriority.SelectedItem = item; break; }
        if (CbPriority.SelectedItem == null) CbPriority.SelectedIndex = 1;

        // Due date
        if (!string.IsNullOrEmpty(todo.DueDate) &&
            DateTime.TryParseExact(todo.DueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            DpDueDate.SelectedDate = dt;

        // Projects
        var projectList = new List<Project> { new() { Id = 0, Name = "(Kein Projekt)" } };
        projectList.AddRange(projects);
        CbProject.ItemsSource = projectList;
        CbProject.SelectedValue = todo.ProjectId ?? 0L;
        if (CbProject.SelectedItem == null) CbProject.SelectedIndex = 0;

        SaveBtn.ToolTip = TooltipService.Get("Btn_Save");
        CancelBtn.ToolTip = TooltipService.Get("Btn_Cancel");
        TbTitle.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TbTitle.Text))
        {
            ModernMessageBox.Show("Bitte einen Titel eingeben.", "Fehler");
            return;
        }

        _todo.Title = TbTitle.Text.Trim();
        _todo.Description = string.IsNullOrWhiteSpace(TbDescription.Text) ? null : TbDescription.Text.Trim();
        _todo.Status = (CbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Offen";
        _todo.Priority = int.TryParse((CbPriority.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var p) ? p : 2;
        _todo.DueDate = DpDueDate.SelectedDate?.ToString("yyyy-MM-dd");

        var selectedProjectId = CbProject.SelectedValue as long?;
        _todo.ProjectId = selectedProjectId > 0 ? selectedProjectId : null;

        Result = _todo;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }
}

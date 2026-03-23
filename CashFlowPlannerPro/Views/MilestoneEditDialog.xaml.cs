using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CashFlowPlannerPro.Models;

namespace CashFlowPlannerPro.Views;

public partial class MilestoneEditDialog : Window
{
    private readonly ProjectMilestone _original;
    public ProjectMilestone? Result { get; private set; }

    public MilestoneEditDialog(ProjectMilestone milestone)
    {
        InitializeComponent();
        _original = milestone;

        if (Application.Current?.MainWindow != null) Owner = Application.Current.MainWindow;

        TbName.Text = milestone.Name;

        // Status
        for (int i = 0; i < CbStatus.Items.Count; i++)
        {
            if (((ComboBoxItem)CbStatus.Items[i]).Content.ToString() == milestone.Status)
            { CbStatus.SelectedIndex = i; break; }
        }
        if (CbStatus.SelectedIndex < 0) CbStatus.SelectedIndex = 0;

        // Priority
        for (int i = 0; i < CbPriority.Items.Count; i++)
        {
            if (((ComboBoxItem)CbPriority.Items[i]).Tag?.ToString() == milestone.Priority.ToString())
            { CbPriority.SelectedIndex = i; break; }
        }
        if (CbPriority.SelectedIndex < 0) CbPriority.SelectedIndex = 1;

        // Deadline
        if (!string.IsNullOrEmpty(milestone.Deadline))
        {
            if (DateTime.TryParseExact(milestone.Deadline, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                DpDeadline.SelectedDate = d;
        }

        TbResponsible.Text = milestone.Responsible ?? "";
        TbHours.Text = milestone.HoursBudget > 0 ? milestone.HoursBudget.ToString("0.#") : "";
        TbNotes.Text = milestone.Notes ?? "";

        Title = milestone.Id == 0 ? "Neuer Meilenstein" : $"Meilenstein: {milestone.Name}";
        TbName.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TbName.Text))
        {
            MessageBox.Show("Bitte einen Namen eingeben.", "Pflichtfeld", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var status = ((ComboBoxItem)CbStatus.SelectedItem).Content.ToString() ?? "Offen";
        var priority = int.TryParse(((ComboBoxItem)CbPriority.SelectedItem).Tag?.ToString(), out var p) ? p : 2;
        double.TryParse(TbHours.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var hours);

        Result = new ProjectMilestone {
            Id = _original.Id,
            ProjectId = _original.ProjectId,
            Name = TbName.Text.Trim(),
            Status = status,
            Priority = priority,
            Deadline = DpDeadline.SelectedDate?.ToString("yyyy-MM-dd"),
            Responsible = string.IsNullOrWhiteSpace(TbResponsible.Text) ? null : TbResponsible.Text.Trim(),
            HoursBudget = hours,
            Notes = string.IsNullOrWhiteSpace(TbNotes.Text) ? null : TbNotes.Text.Trim(),
            SortOrder = _original.SortOrder,
            Dependencies = _original.Dependencies
        };

        DialogResult = true;
        Close();
    }
}

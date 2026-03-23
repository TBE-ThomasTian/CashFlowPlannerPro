using System.Globalization;
using System.Windows;
using System.Windows.Input;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;

namespace CashFlowPlannerPro.Views;

public partial class ResourceEditDialog : Window
{
    public Resource Resource { get; private set; }
    public bool Saved { get; private set; }

    public ResourceEditDialog(Resource resource)
    {
        InitializeComponent();
        Resource = resource;
        LoadHourCombos();
        LoadData();
    }

    private void LoadHourCombos()
    {
        for (int h = 0; h <= 23; h++)
        {
            var label = $"{h:00}:00";
            CbStartHour.Items.Add(label);
            CbEndHour.Items.Add(label);
        }
    }

    private void LoadData()
    {
        TbName.Text = Resource.Name;
        TbRole.Text = Resource.Role;
        TbAvailability.Text = Resource.Availability.ToString("F1");
        TbHourlyRate.Text = Resource.HourlyRate.ToString("F2");
        CbStartHour.SelectedIndex = Resource.WorkStartHour;
        CbEndHour.SelectedIndex = Resource.WorkEndHour;

        if (Resource.Id == 0) DialogTitle.Text = "Neuer Mitarbeiter";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TbName.Text))
        {
            ModernMessageBox.ShowError("Bitte geben Sie einen Namen ein.", "Pflichtfeld");
            return;
        }

        if (CbStartHour.SelectedIndex >= CbEndHour.SelectedIndex)
        {
            ModernMessageBox.ShowError("Arbeitsbeginn muss vor Arbeitsende liegen.", "Ungültige Zeiten");
            return;
        }

        Resource.Name = TbName.Text.Trim();
        Resource.Role = TbRole.Text.Trim();
        if (double.TryParse(TbAvailability.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var avail))
            Resource.Availability = Math.Clamp(avail, 0, 1);
        if (double.TryParse(TbHourlyRate.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var rate))
            Resource.HourlyRate = rate;
        Resource.WorkStartHour = CbStartHour.SelectedIndex;
        Resource.WorkEndHour = CbEndHour.SelectedIndex;

        Saved = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        if (e.Key == Key.Enter) Save_Click(sender, e);
    }

    // --- Static API ---
    public static Resource? ShowEdit(Resource resource)
    {
        var dlg = new ResourceEditDialog(new Resource {
            Id = resource.Id, Name = resource.Name, Role = resource.Role,
            Availability = resource.Availability, HourlyRate = resource.HourlyRate,
            WorkStartHour = resource.WorkStartHour, WorkEndHour = resource.WorkEndHour
        });
        dlg.Owner = Application.Current.MainWindow?.IsVisible == true ? Application.Current.MainWindow : null;
        dlg.ShowDialog();
        return dlg.Saved ? dlg.Resource : null;
    }

    public static Resource? ShowNew()
    {
        return ShowEdit(new Resource());
    }
}

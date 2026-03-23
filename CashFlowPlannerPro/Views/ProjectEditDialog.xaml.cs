using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CashFlowPlannerPro.Models;

namespace CashFlowPlannerPro.Views;

public partial class ProjectEditDialog : Window
{
    public Project Project { get; private set; }
    public bool Saved { get; private set; }
    private string _selectedColor;

    private static readonly string[] Colors = [
        "#E74C3C", "#E67E22", "#F1C40F", "#2ECC71", "#3498DB",
        "#9B59B6", "#1ABC9C", "#34495E", "#BF247A", "#D9731A"
    ];

    public ProjectEditDialog(Project project)
    {
        InitializeComponent();
        Project = project;
        _selectedColor = project.Color ?? "#3498db";
        LoadData();
        BuildColorPicker();
    }

    private void LoadData()
    {
        TbProjectNumber.Text = Project.ProjectNumber;
        TbName.Text = Project.Name;
        TbClient.Text = Project.Client;
        TbBudget.Text = Project.Budget.ToString("F2");

        if (DateTime.TryParse(Project.StartDate, out var sd)) DpStart.SelectedDate = sd;
        if (DateTime.TryParse(Project.EndDate, out var ed)) DpEnd.SelectedDate = ed;

        foreach (ComboBoxItem item in CbStatus.Items)
        {
            if (item.Content?.ToString() == Project.Status)
            { CbStatus.SelectedItem = item; break; }
        }
        if (CbStatus.SelectedItem == null) CbStatus.SelectedIndex = 0;

        if (Project.Id == 0) DialogTitle.Text = "Neues Projekt";
    }

    private void BuildColorPicker()
    {
        ColorPicker.Children.Clear();
        foreach (var hex in Colors)
        {
            Color c;
            try { c = (Color)ColorConverter.ConvertFromString(hex); }
            catch { continue; }

            var circle = new Ellipse {
                Width = 28, Height = 28, Fill = new SolidColorBrush(c),
                Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                Stroke = hex == _selectedColor ? Brushes.White : Brushes.Transparent,
                StrokeThickness = 3
            };
            var capturedHex = hex;
            circle.MouseLeftButtonDown += (_, _) => {
                _selectedColor = capturedHex;
                BuildColorPicker(); // refresh selection
            };
            ColorPicker.Children.Add(circle);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TbName.Text))
        {
            ModernMessageBox.ShowError("Bitte geben Sie einen Projektnamen ein.", "Pflichtfeld");
            return;
        }

        Project.ProjectNumber = TbProjectNumber.Text.Trim();
        Project.Name = TbName.Text.Trim();
        Project.Client = TbClient.Text.Trim();
        Project.Color = _selectedColor;
        if (double.TryParse(TbBudget.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var budget))
            Project.Budget = budget;
        Project.StartDate = DpStart.SelectedDate?.ToString("yyyy-MM-dd");
        Project.EndDate = DpEnd.SelectedDate?.ToString("yyyy-MM-dd");
        Project.Status = (CbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "active";

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
    public static Project? ShowEdit(Project project)
    {
        var dlg = new ProjectEditDialog(new Project {
            Id = project.Id, ProjectNumber = project.ProjectNumber, Name = project.Name,
            Client = project.Client, Color = project.Color, StartDate = project.StartDate,
            EndDate = project.EndDate, Budget = project.Budget, Status = project.Status
        });
        dlg.Owner = Application.Current.MainWindow?.IsVisible == true ? Application.Current.MainWindow : null;
        dlg.ShowDialog();
        return dlg.Saved ? dlg.Project : null;
    }

    public static Project? ShowNew()
    {
        return ShowEdit(new Project());
    }
}

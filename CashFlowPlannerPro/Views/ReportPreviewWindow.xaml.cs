using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;
using CashFlowPlannerPro.ViewModels;

namespace CashFlowPlannerPro.Views;

public partial class ReportPreviewWindow : Window
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public ReportPreviewWindow(string title, string subtitle, DashboardViewModel vm)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        SubtitleText.Text = subtitle;

        if (Application.Current?.MainWindow != null)
            Owner = Application.Current.MainWindow;

        BuildPreview(vm);
    }

    private void BuildPreview(DashboardViewModel vm)
    {
        var company = CompanyProfileService.Load();
        CompanyNameText.Text = company.CompanyName;
        CompanyNameText.Visibility = string.IsNullOrWhiteSpace(company.CompanyName)
            ? Visibility.Collapsed
            : Visibility.Visible;
        CompanyContactText.Text = BuildCompanyContactLine(company);
        CompanyContactText.Visibility = string.IsNullOrWhiteSpace(CompanyContactText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        ReportTitleText.Text = "CashFlow Planner Pro";
        ReportSubtitleText.Text = string.IsNullOrWhiteSpace(company.CompanyName)
            ? "Executive Summary fuer Cashflow, Pipeline und operative Steuerung"
            : $"{company.CompanyName}  •  Executive Summary fuer Cashflow und operative Steuerung";
        GeneratedAtText.Text = DateTime.Now.ToString("dd.MM.yyyy HH:mm", De);
        ReportPeriodText.Text = $"{vm.HorizonMonths} Monate Vorschau";

        CurrentBalanceText.Text = vm.CurrentBalance;
        ForecastEndText.Text = vm.ForecastEnd;
        OpenInvoicesText.Text = vm.OpenInvoices;
        ActiveOffersText.Text = vm.ActiveOffers;
        MonthlyCashflowText.Text = vm.MonthlyCashflow;
        BurnRateText.Text = vm.BurnRate;
        RunwayText.Text = vm.Runway;
        HoursThisMonthText.Text = vm.HoursThisMonth;
        OpenTodosText.Text = vm.OpenTodos;
        OverdueTodosText.Text = vm.OverdueTodos;
        TeamUtilizationText.Text = vm.TeamUtilization;
        RunningTimersText.Text = vm.RunningTimers;

        ForecastConfigText.Text =
            $"Rechnungen {(vm.IncludeInvoices ? "an" : "aus")}  |  Angebote offen {(vm.IncludeOffersOffen ? "an" : "aus")}  |  Beauftragt {(vm.IncludeOffersBeauftragt ? "an" : "aus")}  |  Wiederkehrend {(vm.IncludeRecurring ? "an" : "aus")}";

        BuildCriticalTodos();
        BuildMonthTable(vm.MonthRows.Take(8).ToList());
    }

    private void BuildCriticalTodos()
    {
        CriticalTodoPanel.Children.Clear();

        var overdueTodos = Database.Instance.GetAllTodos()
            .Where(t => !string.Equals(t.Status, "Erledigt", StringComparison.OrdinalIgnoreCase))
            .Where(t => DateTime.TryParse(t.DueDate, out var due) && due.Date < DateTime.Today)
            .OrderBy(t => t.DueDate)
            .Take(4)
            .ToList();

        if (overdueTodos.Count == 0)
        {
            CriticalTodoPanel.Children.Add(new TextBlock
            {
                Text = "Keine kritischen ToDos im aktuellen Report gefunden.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x72, 0x96))
            });
            return;
        }

        CriticalTodoPanel.Children.Add(new TextBlock
        {
            Text = "Kritische ToDos",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x23, 0x59)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (var todo in overdueTodos)
        {
            var row = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF5, 0xF5)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xF4, 0xCF, 0xDB)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = todo.Title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x23, 0x59))
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"Faellig: {FormatDate(todo.DueDate)}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xBF, 0x24, 0x7A)),
                Margin = new Thickness(0, 4, 0, 0)
            });

            row.Child = sp;
            CriticalTodoPanel.Children.Add(row);
        }
    }

    private void BuildMonthTable(List<MonthRow> rows)
    {
        MonthTableGrid.Children.Clear();
        MonthTableGrid.RowDefinitions.Clear();
        MonthTableGrid.ColumnDefinitions.Clear();

        var headers = new[] { "Monat", "Einnahmen", "Ausgaben", "Netto", "Kumuliert", "Ziel", "Abweichung" };
        for (int i = 0; i < headers.Length; i++)
        {
            MonthTableGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = i == 0 ? new GridLength(96) : new GridLength(108)
            });
        }

        MonthTableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < headers.Length; i++)
        {
            MonthTableGrid.Children.Add(CreateCell(headers[i], 0, i, true));
        }

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            MonthTableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = rows[rowIndex];
            var values = new[]
            {
                row.Month,
                Money(row.Income),
                Money(row.Expenses),
                Money(row.Net),
                Money(row.Cumulative),
                Money(row.Target),
                Money(row.Variance)
            };

            for (int col = 0; col < values.Length; col++)
                MonthTableGrid.Children.Add(CreateCell(values[col], rowIndex + 1, col, false));
        }
    }

    private UIElement CreateCell(string text, int row, int column, bool header)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(header
                ? Color.FromRgb(0xEE, 0xE7, 0xF5)
                : row % 2 == 0
                    ? Color.FromRgb(0xFF, 0xFF, 0xFF)
                    : Color.FromRgb(0xFB, 0xF8, 0xFD)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xDD, 0xF1)),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(8, 7, 8, 7)
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);

        border.Child = new TextBlock
        {
            Text = text,
            FontSize = header ? 10 : 9.5,
            FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x23, 0x59))
        };

        return border;
    }

    private static string Money(double value) => value.ToString("N2", De) + " EUR";

    private static string FormatDate(string? value)
    {
        if (DateTime.TryParse(value, out var dt))
            return dt.ToString("dd.MM.yyyy", De);
        return "-";
    }

    private static string BuildCompanyContactLine(CompanyProfile profile)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile.AddressLine1)) parts.Add(profile.AddressLine1);
        if (!string.IsNullOrWhiteSpace(profile.ContactEmail)) parts.Add(profile.ContactEmail);
        if (!string.IsNullOrWhiteSpace(profile.ContactPhone)) parts.Add(profile.ContactPhone);
        if (!string.IsNullOrWhiteSpace(profile.Website)) parts.Add(profile.Website);
        return string.Join(" | ", parts);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize_Click(sender, new RoutedEventArgs());
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}

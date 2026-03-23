using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.ViewModels;

namespace CashFlowPlannerPro.Views;

public partial class ResourcesView : UserControl
{
    private readonly ResourcesViewModel _vm;
    static readonly CultureInfo DE = new("de-DE");
    static readonly Brush HeaderBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    static readonly Brush GridLine = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
    static readonly Brush WeekendBg = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
    static readonly Brush CellBg = new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFA));
    static readonly Brush TodayHeaderBg = new SolidColorBrush(Color.FromRgb(0xDB, 0xEA, 0xFE));
    static readonly Brush ResourceBg = Brushes.White;

    public ResourcesView()
    {
        InitializeComponent();
        _vm = new ResourcesViewModel();
        DataContext = _vm;
        _vm.CalendarChanged += BuildCalendar;
        _vm.Load();
    }

    private void ViewMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (RbDay.IsChecked == true) _vm.SetViewMode("Day");
        else if (RbMonth.IsChecked == true) _vm.SetViewMode("Month");
        else _vm.SetViewMode("Week");
    }

    private void BuildCalendar()
    {
        CalendarGrid.Children.Clear();
        CalendarGrid.ColumnDefinitions.Clear();
        CalendarGrid.RowDefinitions.Clear();

        var resources = _vm.Resources;
        var (start, _) = _vm.GetDateRange();
        int days = _vm.DaysToShow;

        // Columns: resource info + one per day
        CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        for (int d = 0; d < days; d++)
            CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 90 });

        // Rows: header + one per resource
        CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        foreach (var _ in resources)
            CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(65) });

        // Top-left corner
        AddToGrid(new Border {
            Background = HeaderBg,
            BorderBrush = GridLine, BorderThickness = new Thickness(0, 0, 1, 1),
            Child = new TextBlock {
                Text = "Mitarbeiter", FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Gray, FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0)
            }
        }, 0, 0);

        // Date headers
        for (int d = 0; d < days; d++)
        {
            var date = start.AddDays(d);
            bool isToday = date.Date == DateTime.Today;
            var dayAbbr = date.ToString("ddd", DE).TrimEnd('.');
            var bg = isToday ? TodayHeaderBg : HeaderBg;

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock {
                Text = dayAbbr.Substring(0, Math.Min(2, dayAbbr.Length)).ToUpper(),
                Foreground = Brushes.Gray, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            sp.Children.Add(new TextBlock {
                Text = date.Day.ToString(),
                Foreground = isToday ? Brushes.DodgerBlue : Brushes.Black,
                FontSize = 16, FontWeight = isToday ? FontWeights.Bold : FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            AddToGrid(new Border {
                Background = bg, BorderBrush = GridLine, BorderThickness = new Thickness(0, 0, 1, 1),
                Child = sp
            }, 0, d + 1);
        }

        // Resource rows
        for (int r = 0; r < resources.Count; r++)
        {
            var resource = resources[r];

            // Resource info cell
            var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
            infoPanel.Children.Add(new TextBlock {
                Text = resource.Name, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black, FontSize = 13
            });
            if (!string.IsNullOrEmpty(resource.Role))
            {
                infoPanel.Children.Add(new TextBlock {
                    Text = resource.Role, Foreground = Brushes.Gray, FontSize = 11
                });
            }
            AddToGrid(new Border {
                Background = ResourceBg, BorderBrush = GridLine,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child = infoPanel
            }, r + 1, 0);

            // Background cells (for click handling + weekend shading)
            for (int d = 0; d < days; d++)
            {
                var date = start.AddDays(d);
                bool isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                var cell = new Border {
                    Background = isWeekend ? WeekendBg : CellBg,
                    BorderBrush = GridLine, BorderThickness = new Thickness(0, 0, 1, 1),
                    Cursor = Cursors.Hand
                };
                var resId = resource.Id;
                var cellDate = date;
                cell.MouseLeftButtonUp += (s, _) => ShowProjectMenu((Border)s!, resId, cellDate);
                AddToGrid(cell, r + 1, d + 1);
            }

            // Gantt bars: group consecutive allocations by project
            var spans = GetAllocationSpans(resource.Id, start, days);
            foreach (var span in spans)
            {
                var project = _vm.Projects.FirstOrDefault(p => p.Id == span.ProjectId);
                var colorStr = project?.Color ?? "#3498db";
                Color barColor;
                try { barColor = (Color)ColorConverter.ConvertFromString(colorStr); }
                catch { barColor = Color.FromRgb(0x34, 0x98, 0xdb); }

                var barBrush = new SolidColorBrush(barColor);
                var barText = project?.Name ?? "Projekt";
                var hoursText = $"{span.Hours:0.#}h pro Tag";

                var textPanel = new StackPanel {
                    Orientation = Orientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 8, 0)
                };
                textPanel.Children.Add(new TextBlock {
                    Text = barText, Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold, FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                textPanel.Children.Add(new TextBlock {
                    Text = hoursText, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                    FontSize = 10
                });

                var bar = new Border {
                    Background = barBrush, CornerRadius = new CornerRadius(4),
                    Height = 36, Margin = new Thickness(2, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand,
                    Child = textPanel,
                    ToolTip = $"{barText}\n{span.StartDate:dd.MM} - {span.EndDate:dd.MM}\n{hoursText}",
                    Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.15, Color = Colors.Black }
                };

                // Right-click to delete all allocations in this span
                var allocIds = span.AllocationIds;
                bar.MouseRightButtonUp += (_, _) => {
                    if (MessageBox.Show($"{barText} entfernen?\n({span.StartDate:dd.MM} - {span.EndDate:dd.MM})",
                        "Zuordnung löschen", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        foreach (var id in allocIds)
                            _vm.DeleteAllocation(id);
                    }
                };

                Grid.SetRow(bar, r + 1);
                Grid.SetColumn(bar, span.StartCol + 1);
                Grid.SetColumnSpan(bar, span.ColSpan);
                CalendarGrid.Children.Add(bar);
            }
        }
    }

    private record AllocationSpan(long ProjectId, int StartCol, int ColSpan, DateTime StartDate, DateTime EndDate, double Hours, List<long> AllocationIds);

    private List<AllocationSpan> GetAllocationSpans(long resourceId, DateTime start, int days)
    {
        var spans = new List<AllocationSpan>();
        int d = 0;
        while (d < days)
        {
            var date = start.AddDays(d);
            var alloc = _vm.GetAllocation(resourceId, date);
            if (alloc != null)
            {
                var projectId = alloc.ProjectId;
                var spanStart = d;
                var startDate = date;
                var hours = alloc.Hours;
                var ids = new List<long> { alloc.Id };

                // Extend span while same project on consecutive days
                int next = d + 1;
                while (next < days)
                {
                    var nextDate = start.AddDays(next);
                    var nextAlloc = _vm.GetAllocation(resourceId, nextDate);
                    if (nextAlloc != null && nextAlloc.ProjectId == projectId)
                    {
                        ids.Add(nextAlloc.Id);
                        next++;
                    }
                    else break;
                }
                var endDate = start.AddDays(next - 1);
                spans.Add(new AllocationSpan(projectId, spanStart, next - spanStart, startDate, endDate, hours, ids));
                d = next;
            }
            else d++;
        }
        return spans;
    }

    private void AddToGrid(UIElement element, int row, int col)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, col);
        CalendarGrid.Children.Add(element);
    }

    private void ShowProjectMenu(Border cell, long resourceId, DateTime date)
    {
        var menu = new ContextMenu();
        foreach (var project in _vm.Projects)
        {
            var mi = new MenuItem { Header = project.Name, Foreground = Brushes.Black };
            try {
                var c = (Color)ColorConverter.ConvertFromString(project.Color);
                mi.Icon = new Border {
                    Width = 14, Height = 14, CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(c)
                };
            } catch { }
            var pid = project.Id;
            mi.Click += (_, _) => _vm.AddAllocation(resourceId, pid, date);
            menu.Items.Add(mi);
        }
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = "Keine Projekte vorhanden", IsEnabled = false });
        cell.ContextMenu = menu;
        menu.IsOpen = true;
    }
}

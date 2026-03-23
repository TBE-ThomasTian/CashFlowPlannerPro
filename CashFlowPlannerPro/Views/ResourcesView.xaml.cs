using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.ViewModels;

namespace CashFlowPlannerPro.Views;

public partial class ResourcesView : UserControl
{
    private readonly ResourcesViewModel _vm;
    static readonly CultureInfo DE = new("de-DE");
    static readonly Brush HeaderBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    static readonly Brush GridLine = new SolidColorBrush(Color.FromRgb(0xE2, 0xE0, 0xE8));
    static readonly Brush WeekendBg = new SolidColorBrush(Color.FromRgb(0xF0, 0xEE, 0xF4));
    static readonly Brush CellBg = new SolidColorBrush(Color.FromRgb(0xF8, 0xF7, 0xFA));
    static readonly Brush TodayHeaderBg = new SolidColorBrush(Color.FromRgb(0xE8, 0xDE, 0xF8));
    static readonly Brush ResourceBg = Brushes.White;
    static readonly Brush DropHighlight = new SolidColorBrush(Color.FromArgb(0x30, 0x81, 0x2B, 0x8C));

    private double _columnWidth = 90;
    private double _rowHeight = 70;

    // Resize state
    private Border? _resizeBar;
    private AllocationSpan? _resizeSpan;
    private long _resizeResourceId;
    private string _resizeEdge = ""; // "Left" or "Right"
    private int _resizeOrigCol;
    private int _resizeOrigSpan;
    private DateTime _startDate;
    private int _totalDays;

    public ResourcesView()
    {
        InitializeComponent();
        _vm = new ResourcesViewModel();
        DataContext = _vm;
        _vm.CalendarChanged += () => { BuildCalendar(); BuildProjectList(); BuildHardwareList(); };
        _vm.Load();
    }

    private void CalendarScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+Scroll: zoom column width
            _columnWidth = Math.Clamp(_columnWidth + (e.Delta > 0 ? 10 : -10), 40, 400);
            BuildCalendar();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            // Shift+Scroll: row height
            _rowHeight = Math.Clamp(_rowHeight + (e.Delta > 0 ? 10 : -10), 30, 250);
            BuildCalendar();
            e.Handled = true;
        }
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
        if (_vm.ViewMode == "Day")
        {
            BuildDayView();
            return;
        }

        CalendarGrid.Children.Clear();
        CalendarGrid.ColumnDefinitions.Clear();
        CalendarGrid.RowDefinitions.Clear();

        var resources = _vm.Resources;
        var (start, _) = _vm.GetDateRange();
        _startDate = start;
        int days = _vm.DaysToShow;
        _totalDays = days;

        CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        for (int d = 0; d < days; d++)
            CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_columnWidth) });

        CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        foreach (var _ in resources)
        {
            CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_rowHeight) }); // project row
            CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(30, _rowHeight * 0.5)) }); // hardware row
        }

        // Top-left corner
        AddToGrid(new Border {
            Background = HeaderBg, BorderBrush = GridLine, BorderThickness = new Thickness(0, 0, 1, 1),
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
                Foreground = Brushes.Gray, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center
            });
            sp.Children.Add(new TextBlock {
                Text = date.Day.ToString(),
                Foreground = isToday ? new SolidColorBrush(Color.FromRgb(0x81, 0x2B, 0x8C)) : Brushes.Black,
                FontSize = 16, FontWeight = isToday ? FontWeights.Bold : FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            AddToGrid(new Border { Background = bg, BorderBrush = GridLine, BorderThickness = new Thickness(0, 0, 1, 1), Child = sp }, 0, d + 1);
        }

        // Resource rows
        for (int r = 0; r < resources.Count; r++)
        {
            var resource = resources[r];

            // Resource info — double-click to edit
            var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
            infoPanel.Children.Add(new TextBlock { Text = resource.Name, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, FontSize = 13 });
            var roleText = resource.Role;
            if (!string.IsNullOrEmpty(roleText))
                infoPanel.Children.Add(new TextBlock { Text = roleText, Foreground = Brushes.Gray, FontSize = 11 });
            var hoursText = $"{resource.WorkStartHour:00}:00 – {resource.WorkEndHour:00}:00";
            infoPanel.Children.Add(new TextBlock { Text = hoursText, Foreground = new SolidColorBrush(Color.FromRgb(0x81, 0x2B, 0x8C)), FontSize = 10 });

            int projectRow = r * 2 + 1;
            int hardwareRow = r * 2 + 2;

            var infoBorder = new Border { Background = ResourceBg, BorderBrush = GridLine, BorderThickness = new Thickness(0, 0, 1, 1), Child = infoPanel, Cursor = Cursors.Hand };
            var capturedResource = resource;
            infoBorder.MouseLeftButtonDown += (s, e) => {
                if (e.ClickCount == 2)
                {
                    e.Handled = true;
                    var edited = ResourceEditDialog.ShowEdit(capturedResource);
                    if (edited != null)
                    {
                        Database.Instance.UpdateResource(edited);
                        _vm.Load();
                    }
                }
            };
            AddToGrid(infoBorder, projectRow, 0);

            // Hardware row label
            var hwLabel = new Border {
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8)),
                BorderBrush = GridLine, BorderThickness = new Thickness(0, 0, 1, 1),
                Child = new TextBlock {
                    Text = "   🖥 Hardware", FontSize = 10, Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                }
            };
            AddToGrid(hwLabel, hardwareRow, 0);

            // Background cells with drop support
            for (int d = 0; d < days; d++)
            {
                var date = start.AddDays(d);
                bool isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                var cell = new Border {
                    Background = isWeekend ? WeekendBg : CellBg,
                    BorderBrush = GridLine, BorderThickness = new Thickness(0, 0, 1, 1),
                    Cursor = Cursors.Hand, AllowDrop = true
                };
                var resId = resource.Id;
                var cellDate = date;
                cell.MouseLeftButtonUp += (s, _) => ShowProjectMenu((Border)s!, resId, cellDate);
                cell.DragEnter += (s, e) => { ((Border)s!).Background = DropHighlight; e.Handled = true; };
                cell.DragLeave += (s, _) => { ((Border)s!).Background = isWeekend ? WeekendBg : CellBg; };
                cell.DragOver += (_, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
                cell.Drop += (s, e) => {
                    ((Border)s!).Background = isWeekend ? WeekendBg : CellBg;
                    if (e.Data.GetDataPresent("ProjectId"))
                    {
                        var pid = (long)e.Data.GetData("ProjectId")!;
                        _vm.AddAllocation(resId, pid, cellDate);
                    }
                    else if (e.Data.GetDataPresent("SpanProjectId"))
                    {
                        var pid = (long)e.Data.GetData("SpanProjectId")!;
                        var fromResId = (long)e.Data.GetData("SpanResourceId")!;
                        var spanStart = (DateTime)e.Data.GetData("SpanStartDate")!;
                        var spanEnd = (DateTime)e.Data.GetData("SpanEndDate")!;
                        if (fromResId != resId)
                            _vm.MoveAllocation(fromResId, resId, pid, spanStart, spanEnd);
                    }
                    e.Handled = true;
                };
                AddToGrid(cell, projectRow, d + 1);

                // Hardware cell (drop target for hardware)
                var hwCell = new Border {
                    Background = isWeekend ? WeekendBg : new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8)),
                    BorderBrush = GridLine, BorderThickness = new Thickness(0, 0, 1, 1),
                    AllowDrop = true
                };
                var hwResId = resource.Id;
                var hwCellDate = date;
                hwCell.DragEnter += (s, e) => { ((Border)s!).Background = DropHighlight; e.Handled = true; };
                hwCell.DragLeave += (s, _) => { ((Border)s!).Background = isWeekend ? WeekendBg : new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8)); };
                hwCell.DragOver += (_, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
                hwCell.Drop += (s, e) => {
                    ((Border)s!).Background = isWeekend ? WeekendBg : new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8));
                    if (e.Data.GetDataPresent("HardwareId"))
                    {
                        var hwId = (long)e.Data.GetData("HardwareId")!;
                        // Need a project — use the project allocated on this day, or show menu
                        var projAllocs = _vm.GetAllocations(hwResId, hwCellDate);
                        if (projAllocs.Count == 1)
                            _vm.AddHardwareAllocation(hwResId, hwId, projAllocs[0].ProjectId, hwCellDate);
                        else if (projAllocs.Count > 1)
                            ShowHardwareProjectMenu(hwResId, hwId, hwCellDate, projAllocs);
                    }
                    e.Handled = true;
                };
                AddToGrid(hwCell, hardwareRow, d + 1);
            }

            // Gantt bars — stacked when multiple projects overlap
            var allSpans = GetAllocationSpansMulti(resource.Id, start, days);
            // Group by project to get separate span lists
            var spansByProject = allSpans.GroupBy(s => s.ProjectId).ToList();

            // Determine overlap count per column for stacking height
            var overlapCount = new int[days];
            foreach (var span in allSpans)
                for (int c = span.StartCol; c < span.StartCol + span.ColSpan; c++)
                    overlapCount[c]++;

            // Determine max overlap for row height
            int maxOverlap = overlapCount.Length > 0 ? overlapCount.Max() : 1;
            if (maxOverlap < 1) maxOverlap = 1;

            // Track which "slot" each project gets
            var projectSlot = new Dictionary<long, int>();
            int slotIdx = 0;
            foreach (var grp in spansByProject)
                projectSlot[grp.Key] = slotIdx++;

            int totalSlots = Math.Max(spansByProject.Count, 1);
            double barHeight = Math.Max(16, (_rowHeight - 4) / totalSlots - 2);

            foreach (var span in allSpans)
            {
                var project = _vm.Projects.FirstOrDefault(p => p.Id == span.ProjectId);
                var colorStr = project?.Color ?? "#3498db";
                Color barColor;
                try { barColor = (Color)ColorConverter.ConvertFromString(colorStr); }
                catch { barColor = Color.FromRgb(0x34, 0x98, 0xdb); }

                var barBrush = new SolidColorBrush(barColor);
                var barText = project?.Name ?? "Projekt";
                var barHoursText = $"{span.Hours:0.#}h/Tag";

                int slot = projectSlot[span.ProjectId];
                double topMargin = 2 + slot * (barHeight + 2);

                var textPanel = new StackPanel {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 6, 0)
                };
                textPanel.Children.Add(new TextBlock {
                    Text = barText, Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold, FontSize = barHeight > 20 ? 11 : 9,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                });
                if (barHeight > 18 && span.ColSpan > 1)
                {
                    textPanel.Children.Add(new TextBlock {
                        Text = $"  {barHoursText}",
                        Foreground = new SolidColorBrush(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF)),
                        FontSize = 9, VerticalAlignment = VerticalAlignment.Center
                    });
                }

                var bar = new Border {
                    Background = barBrush, CornerRadius = new CornerRadius(4),
                    Height = barHeight,
                    Margin = new Thickness(2, topMargin, 2, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Cursor = Cursors.Hand, Child = textPanel,
                    ToolTip = $"{barText}\n{span.StartDate:dd.MM} - {span.EndDate:dd.MM}\n{barHoursText}",
                    Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.12, Color = Colors.Black },
                    Tag = span // store for resize
                };

                // Mouse events for resize + drag
                var capturedSpan = span;
                var capturedResId = resource.Id;
                bar.MouseMove += (s, e) => {
                    if (_resizeBar != null) return;
                    var b = (Border)s!;
                    var pos = e.GetPosition(b);
                    if (pos.X < 8 || pos.X > b.ActualWidth - 8)
                        b.Cursor = Cursors.SizeWE;
                    else
                    {
                        b.Cursor = Cursors.Hand;
                        // Start drag if left button pressed and on center
                        if (e.LeftButton == MouseButtonState.Pressed && pos.X >= 8 && pos.X <= b.ActualWidth - 8)
                        {
                            var data = new DataObject();
                            data.SetData("SpanProjectId", capturedSpan.ProjectId);
                            data.SetData("SpanResourceId", capturedResId);
                            data.SetData("SpanStartDate", capturedSpan.StartDate);
                            data.SetData("SpanEndDate", capturedSpan.EndDate);
                            DragDrop.DoDragDrop(b, data, DragDropEffects.Move);
                        }
                    }
                };
                bar.MouseLeftButtonDown += (s, e) => Bar_MouseLeftButtonDown(s, e, capturedSpan, capturedResId);
                bar.MouseLeftButtonUp += Bar_MouseLeftButtonUp;

                // Double-click opens Kanban board
                var barProject = project;
                bar.MouseLeftButtonDown += (s, e) => {
                    if (e.ClickCount == 2 && barProject != null)
                    {
                        e.Handled = true;
                        ProjectKanbanDialog.Show(barProject);
                    }
                };

                // Right-click delete
                var allocIds = span.AllocationIds;
                bar.MouseRightButtonUp += (_, _) => {
                    if (ModernMessageBox.ShowConfirm($"{barText} entfernen?\n({span.StartDate:dd.MM} - {span.EndDate:dd.MM})", "Zuordnung löschen"))
                    {
                        foreach (var id in allocIds)
                            _vm.DeleteAllocation(id);
                    }
                };

                Grid.SetRow(bar, projectRow);
                Grid.SetColumn(bar, span.StartCol + 1);
                Grid.SetColumnSpan(bar, span.ColSpan);
                CalendarGrid.Children.Add(bar);

                // Weekend hatching overlays — only on weekend columns within this bar
                for (int wd = 0; wd < span.ColSpan; wd++)
                {
                    var dow = _startDate.AddDays(span.StartCol + wd).DayOfWeek;
                    if (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday)
                    {
                        var hatchGroup = new DrawingGroup();
                        hatchGroup.Children.Add(new GeometryDrawing(
                            new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null,
                            new RectangleGeometry(new Rect(0, 0, 10, 10))));
                        hatchGroup.Children.Add(new GeometryDrawing(
                            null, new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1.5),
                            Geometry.Parse("M0,10 L10,0 M-2,2 L2,-2 M8,12 L12,8")));
                        var hatchBrush = new DrawingBrush(hatchGroup) {
                            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 6, 6),
                            ViewportUnits = BrushMappingMode.Absolute
                        };
                        var overlay = new Border {
                            Background = hatchBrush,
                            Height = barHeight,
                            Margin = new Thickness(0, topMargin, 0, 0),
                            VerticalAlignment = VerticalAlignment.Top,
                            CornerRadius = new CornerRadius(
                                wd == 0 ? 4 : 0, wd == span.ColSpan - 1 ? 4 : 0,
                                wd == span.ColSpan - 1 ? 4 : 0, wd == 0 ? 4 : 0),
                            IsHitTestVisible = false // clicks pass through to bar
                        };
                        Grid.SetRow(overlay, projectRow);
                        Grid.SetColumn(overlay, span.StartCol + wd + 1);
                        CalendarGrid.Children.Add(overlay);
                    }
                }
            }

            // Hardware allocation bars in hardware row
            var hwSpans = GetHardwareAllocationSpans(resource.Id, start, days);
            foreach (var hwSpan in hwSpans)
            {
                var hw = _vm.HardwareResources.FirstOrDefault(h => h.Id == hwSpan.HardwareId);
                var hwColorStr = hw?.Color ?? "#17a2b8";
                Color hwBarColor;
                try { hwBarColor = (Color)ColorConverter.ConvertFromString(hwColorStr); }
                catch { hwBarColor = Color.FromRgb(0x17, 0xa2, 0xb8); }

                var hwBarText = hw?.Name ?? "Hardware";
                var project = _vm.Projects.FirstOrDefault(p => p.Id == hwSpan.ProjectId);
                var projLabel = project?.Name ?? "";

                var hwTextPanel = new StackPanel {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 0)
                };
                hwTextPanel.Children.Add(new TextBlock {
                    Text = $"🖥 {hwBarText}", Foreground = Brushes.White,
                    FontSize = 9, FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                if (!string.IsNullOrEmpty(projLabel))
                    hwTextPanel.Children.Add(new TextBlock {
                        Text = $"  ({projLabel})", FontSize = 8,
                        Foreground = new SolidColorBrush(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF)),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });

                var hwBar = new Border {
                    Background = new SolidColorBrush(hwBarColor),
                    CornerRadius = new CornerRadius(3),
                    Height = Math.Max(16, Math.Max(30, _rowHeight * 0.5) - 6),
                    Margin = new Thickness(2, 2, 2, 2),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = hwTextPanel,
                    ToolTip = $"{hwBarText}\nProjekt: {projLabel}\n{hwSpan.StartDate:dd.MM} - {hwSpan.EndDate:dd.MM}",
                    Effect = new DropShadowEffect { BlurRadius = 2, ShadowDepth = 1, Opacity = 0.08 }
                };

                // Right-click to delete
                var capturedHwSpan = hwSpan;
                hwBar.MouseRightButtonUp += (_, _) => {
                    if (ModernMessageBox.ShowConfirm($"Hardware \"{hwBarText}\" Zuordnung löschen?", "Hardware entfernen"))
                        foreach (var id in capturedHwSpan.AllocationIds)
                            _vm.DeleteHardwareAllocation(id);
                };

                Grid.SetRow(hwBar, hardwareRow);
                Grid.SetColumn(hwBar, hwSpan.StartCol + 1);
                Grid.SetColumnSpan(hwBar, hwSpan.ColSpan);
                CalendarGrid.Children.Add(hwBar);
            }
        }

        BuildProjectList();
    }

    // --- Resize logic ---
    private void Bar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e, AllocationSpan span, long resourceId)
    {
        var bar = (Border)sender;
        var pos = e.GetPosition(bar);
        if (pos.X < 8)
        {
            _resizeEdge = "Left";
        }
        else if (pos.X > bar.ActualWidth - 8)
        {
            _resizeEdge = "Right";
        }
        else return; // not on edge, don't start resize

        _resizeBar = bar;
        _resizeSpan = span;
        _resizeResourceId = resourceId;
        _resizeOrigCol = span.StartCol + 1; // grid column (1-based)
        _resizeOrigSpan = span.ColSpan;
        bar.Opacity = 0.7;
        bar.CaptureMouse();
        // Subscribe to global mouse move for live preview
        CalendarGrid.MouseMove += CalendarGrid_ResizePreview;
        e.Handled = true;
    }

    private void CalendarGrid_ResizePreview(object sender, MouseEventArgs e)
    {
        if (_resizeBar == null || _resizeSpan == null) return;

        var posInGrid = e.GetPosition(CalendarGrid);
        int targetCol = GetColumnAtPosition(posInGrid.X);
        if (targetCol < 1) targetCol = 1;
        if (targetCol > _totalDays) targetCol = _totalDays;

        var origStartCol0 = _resizeSpan.StartCol; // 0-based
        var origEndCol0 = _resizeSpan.StartCol + _resizeSpan.ColSpan - 1; // 0-based

        int newGridCol, newColSpan;

        if (_resizeEdge == "Right")
        {
            int newEndCol0 = targetCol - 1;
            if (newEndCol0 < origStartCol0) newEndCol0 = origStartCol0;
            newGridCol = origStartCol0 + 1; // 1-based for grid
            newColSpan = newEndCol0 - origStartCol0 + 1;
        }
        else // Left
        {
            int newStartCol0 = targetCol - 1;
            if (newStartCol0 > origEndCol0) newStartCol0 = origEndCol0;
            if (newStartCol0 < 0) newStartCol0 = 0;
            newGridCol = newStartCol0 + 1; // 1-based for grid
            newColSpan = origEndCol0 - newStartCol0 + 1;
        }

        if (newColSpan < 1) newColSpan = 1;

        // Live update the bar position and span
        Grid.SetColumn(_resizeBar, newGridCol);
        Grid.SetColumnSpan(_resizeBar, newColSpan);
    }

    private void Bar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizeBar == null || _resizeSpan == null) return;

        // Unsubscribe live preview
        CalendarGrid.MouseMove -= CalendarGrid_ResizePreview;

        var bar = _resizeBar;
        bar.ReleaseMouseCapture();
        bar.Opacity = 1.0;

        // Calculate final column position from mouse
        var posInGrid = e.GetPosition(CalendarGrid);
        int targetCol = GetColumnAtPosition(posInGrid.X);
        if (targetCol < 1) targetCol = 1;
        if (targetCol > _totalDays) targetCol = _totalDays;

        var span = _resizeSpan;
        var origStartCol = span.StartCol; // 0-based
        var origEndCol = span.StartCol + span.ColSpan - 1; // 0-based

        if (_resizeEdge == "Right")
        {
            int newEndCol = targetCol - 1; // convert to 0-based
            if (newEndCol < origStartCol) newEndCol = origStartCol;
            if (newEndCol > origEndCol)
            {
                var from = _startDate.AddDays(origEndCol + 1);
                var to = _startDate.AddDays(newEndCol);
                _vm.AddAllocationsRange(_resizeResourceId, span.ProjectId, from, to);
            }
            else if (newEndCol < origEndCol)
            {
                var from = _startDate.AddDays(newEndCol + 1);
                var to = _startDate.AddDays(origEndCol);
                _vm.DeleteAllocationsRange(_resizeResourceId, span.ProjectId, from, to);
            }
        }
        else // Left
        {
            int newStartCol = targetCol - 1; // 0-based
            if (newStartCol > origEndCol) newStartCol = origEndCol;
            if (newStartCol < origStartCol)
            {
                var from = _startDate.AddDays(newStartCol);
                var to = _startDate.AddDays(origStartCol - 1);
                _vm.AddAllocationsRange(_resizeResourceId, span.ProjectId, from, to);
            }
            else if (newStartCol > origStartCol)
            {
                var from = _startDate.AddDays(origStartCol);
                var to = _startDate.AddDays(newStartCol - 1);
                _vm.DeleteAllocationsRange(_resizeResourceId, span.ProjectId, from, to);
            }
        }

        _resizeBar = null;
        _resizeSpan = null;
        e.Handled = true;
    }

    private int GetColumnAtPosition(double x)
    {
        double accum = 0;
        for (int i = 0; i < CalendarGrid.ColumnDefinitions.Count; i++)
        {
            accum += CalendarGrid.ColumnDefinitions[i].ActualWidth;
            if (x < accum) return i;
        }
        return CalendarGrid.ColumnDefinitions.Count - 1;
    }

    // --- Allocation spans with multi-project support ---
    private record AllocationSpan(long ProjectId, int StartCol, int ColSpan, DateTime StartDate, DateTime EndDate, double Hours, List<long> AllocationIds);
    private record HwAllocationSpan(long HardwareId, long ProjectId, int StartCol, int ColSpan, DateTime StartDate, DateTime EndDate, List<long> AllocationIds);

    private List<AllocationSpan> GetAllocationSpansMulti(long resourceId, DateTime start, int days)
    {
        // Get all allocations for this resource grouped by project
        var byProject = new Dictionary<long, List<(int col, DateTime date, long id, double hours)>>();
        for (int d = 0; d < days; d++)
        {
            var date = start.AddDays(d);
            var allocs = _vm.GetAllocations(resourceId, date);
            foreach (var a in allocs)
            {
                if (!byProject.ContainsKey(a.ProjectId))
                    byProject[a.ProjectId] = [];
                byProject[a.ProjectId].Add((d, date, a.Id, a.Hours));
            }
        }

        var spans = new List<AllocationSpan>();
        foreach (var (projectId, entries) in byProject)
        {
            var sorted = entries.OrderBy(e => e.col).ToList();
            int i = 0;
            while (i < sorted.Count)
            {
                var spanStart = sorted[i].col;
                var startDate = sorted[i].date;
                var hours = sorted[i].hours;
                var ids = new List<long> { sorted[i].id };
                int j = i + 1;
                while (j < sorted.Count && sorted[j].col == sorted[j - 1].col + 1)
                {
                    ids.Add(sorted[j].id);
                    j++;
                }
                var endDate = sorted[j - 1].date;
                spans.Add(new AllocationSpan(projectId, spanStart, j - i, startDate, endDate, hours, ids));
                i = j;
            }
        }
        return spans;
    }

    // --- Day View: hour columns ---
    private void BuildDayView()
    {
        CalendarGrid.Children.Clear();
        CalendarGrid.ColumnDefinitions.Clear();
        CalendarGrid.RowDefinitions.Clear();

        var resources = _vm.Resources;
        var (date, _) = _vm.GetDateRange();
        _startDate = date;
        _totalDays = 1;

        // Determine global hour range (earliest start to latest end)
        int minHour = resources.Count > 0 ? resources.Min(r => r.WorkStartHour) : 8;
        int maxHour = resources.Count > 0 ? resources.Max(r => r.WorkEndHour) : 17;
        if (minHour >= maxHour) { minHour = 8; maxHour = 17; }
        int hourCount = maxHour - minHour;

        // Columns: name + one per hour
        CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        for (int h = 0; h < hourCount; h++)
            CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_columnWidth) });

        // Rows: header + project row + hardware row per resource
        CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        foreach (var _ in resources)
        {
            CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_rowHeight) });
            CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(30, _rowHeight * 0.5)) });
        }

        // Top-left corner
        bool isToday = date.Date == DateTime.Today;
        var dayAbbr = date.ToString("ddd", DE).TrimEnd('.');
        AddToGrid(new Border {
            Background = HeaderBg, BorderBrush = GridLine, BorderThickness = new Thickness(0, 0, 1, 1),
            Child = new TextBlock {
                Text = $"{dayAbbr} {date:dd.MM.yyyy}",
                FontWeight = FontWeights.SemiBold,
                Foreground = isToday ? new SolidColorBrush(Color.FromRgb(0x81, 0x2B, 0x8C)) : Brushes.Black,
                FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0)
            }
        }, 0, 0);

        // Hour headers
        for (int h = 0; h < hourCount; h++)
        {
            int hour = minHour + h;
            AddToGrid(new Border {
                Background = HeaderBg, BorderBrush = GridLine, BorderThickness = new Thickness(0, 0, 1, 1),
                Child = new TextBlock {
                    Text = $"{hour:00}:00", FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }, 0, h + 1);
        }

        // Resource rows
        for (int r = 0; r < resources.Count; r++)
        {
            var resource = resources[r];

            // Resource info (same as week/month view)
            var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
            infoPanel.Children.Add(new TextBlock { Text = resource.Name, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, FontSize = 13 });
            if (!string.IsNullOrEmpty(resource.Role))
                infoPanel.Children.Add(new TextBlock { Text = resource.Role, Foreground = Brushes.Gray, FontSize = 11 });
            var hoursLabel = $"{resource.WorkStartHour:00}:00 – {resource.WorkEndHour:00}:00";
            infoPanel.Children.Add(new TextBlock { Text = hoursLabel, Foreground = new SolidColorBrush(Color.FromRgb(0x81, 0x2B, 0x8C)), FontSize = 10 });

            var infoBorder = new Border { Background = ResourceBg, BorderBrush = GridLine, BorderThickness = new Thickness(0, 0, 1, 1), Child = infoPanel, Cursor = Cursors.Hand };
            var capturedResource = resource;
            infoBorder.MouseLeftButtonDown += (s, e) => {
                if (e.ClickCount == 2)
                {
                    e.Handled = true;
                    var edited = ResourceEditDialog.ShowEdit(capturedResource);
                    if (edited != null) { Database.Instance.UpdateResource(edited); _vm.Load(); }
                }
            };
            int dayProjectRow = r * 2 + 1;
            int dayHardwareRow = r * 2 + 2;
            AddToGrid(infoBorder, dayProjectRow, 0);

            // Hardware row label
            AddToGrid(new Border {
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8)),
                BorderBrush = GridLine, BorderThickness = new Thickness(0, 0, 1, 1),
                Child = new TextBlock {
                    Text = "   🖥 Hardware", FontSize = 10, Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                }
            }, dayHardwareRow, 0);

            // Hour cells — shade working vs. non-working hours
            for (int h = 0; h < hourCount; h++)
            {
                int hour = minHour + h;
                bool isWorking = hour >= resource.WorkStartHour && hour < resource.WorkEndHour;
                var cellBg = isWorking ? CellBg : new SolidColorBrush(Color.FromRgb(0xEE, 0xEC, 0xF0)); // light gray for off-hours

                var cell = new Border {
                    Background = cellBg, BorderBrush = GridLine,
                    BorderThickness = new Thickness(0, 0, 1, 1), AllowDrop = true
                };

                var resId = resource.Id;
                cell.DragOver += (s, e) => {
                    ((Border)s!).Background = new SolidColorBrush(Color.FromArgb(30, 0x81, 0x2B, 0x8C));
                    e.Handled = true;
                };
                cell.DragLeave += (s, _) => {
                    ((Border)s!).Background = isWorking ? CellBg : new SolidColorBrush(Color.FromRgb(0xEE, 0xEC, 0xF0));
                };
                cell.Drop += (s, e) => {
                    ((Border)s!).Background = isWorking ? CellBg : new SolidColorBrush(Color.FromRgb(0xEE, 0xEC, 0xF0));
                    if (e.Data.GetDataPresent("ProjectId"))
                    {
                        var pid = (long)e.Data.GetData("ProjectId")!;
                        _vm.AddAllocation(resId, pid, date);
                    }
                    e.Handled = true;
                };

                AddToGrid(cell, dayProjectRow, h + 1);

                // Hardware hour cell
                var hwHourCell = new Border {
                    Background = isWorking ? new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8)) : new SolidColorBrush(Color.FromRgb(0xEE, 0xEC, 0xF0)),
                    BorderBrush = GridLine, BorderThickness = new Thickness(0, 0, 1, 1)
                };
                AddToGrid(hwHourCell, dayHardwareRow, h + 1);
            }

            // Show allocation bars spanning work hours
            var allocs = _vm.GetAllocations(resource.Id, date);
            foreach (var alloc in allocs)
            {
                Color c;
                try { c = (Color)ColorConverter.ConvertFromString(alloc.ProjectColor ?? "#3498db"); }
                catch { c = Color.FromRgb(0x34, 0x98, 0xdb); }

                // Bar spans from work start to work end
                int barStartCol = resource.WorkStartHour - minHour;
                int barSpan = resource.WorkEndHour - resource.WorkStartHour;
                if (barStartCol < 0) barStartCol = 0;
                if (barStartCol + barSpan > hourCount) barSpan = hourCount - barStartCol;

                var bar = new Border {
                    Background = new SolidColorBrush(c), CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(2, 6, 2, 6), VerticalAlignment = VerticalAlignment.Stretch,
                    Child = new TextBlock {
                        Text = $"{alloc.ProjectName ?? "?"} ({alloc.Hours}h)",
                        Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                };

                Grid.SetRow(bar, dayProjectRow);
                Grid.SetColumn(bar, barStartCol + 1);
                Grid.SetColumnSpan(bar, barSpan);
                CalendarGrid.Children.Add(bar);
                break; // Show first allocation as full-width bar for now
            }
        }
    }

    // --- Project list for drag & drop ---
    private void BuildProjectList()
    {
        if (ProjectListPanel == null) return;
        ProjectListPanel.Children.Clear();

        foreach (var project in _vm.Projects)
        {
            Color c;
            try { c = (Color)ColorConverter.ConvertFromString(project.Color ?? "#3498db"); }
            catch { c = Color.FromRgb(0x34, 0x98, 0xdb); }

            var dot = new Border {
                Width = 12, Height = 12, CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(c),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
            };
            var name = new TextBlock {
                Text = project.Name, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                VerticalAlignment = VerticalAlignment.Center
            };
            var details = new List<string>();
            if (!string.IsNullOrEmpty(project.ProjectNumber)) details.Add(project.ProjectNumber);
            if (!string.IsNullOrEmpty(project.Client)) details.Add(project.Client);
            var detailText = new TextBlock {
                Text = details.Count > 0 ? $"  ({string.Join(" | ", details)})" : "",
                FontSize = 10, Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(dot);
            sp.Children.Add(name);
            sp.Children.Add(detailText);

            var card = new Border {
                Background = Brushes.White, CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand, Child = sp,
                BorderBrush = GridLine, BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.06 }
            };

            var pid = project.Id;
            var pName = project.Name;
            var capturedProject = project;

            // Double-click to edit project properties
            card.MouseLeftButtonDown += (s, e) => {
                if (e.ClickCount == 2)
                {
                    e.Handled = true;
                    var edited = ProjectEditDialog.ShowEdit(capturedProject);
                    if (edited != null)
                    {
                        Database.Instance.UpdateProject(edited);
                        _vm.Load();
                    }
                }
            };

            card.MouseMove += (s, e) => {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    var data = new DataObject();
                    data.SetData("ProjectId", pid);
                    DragDrop.DoDragDrop((Border)s!, data, DragDropEffects.Copy);
                }
            };

            // Right-click context menu
            var ctxMenu = new ContextMenu();

            // Kanban board
            var kanbanItem = new MenuItem { Header = "📋  Meilensteine (Kanban)", Foreground = Brushes.Black };
            var kanbanProject = project;
            kanbanItem.Click += (_, _) => { if (kanbanProject != null) ProjectKanbanDialog.Show(kanbanProject); };
            ctxMenu.Items.Add(kanbanItem);
            ctxMenu.Items.Add(new Separator());

            // Color picker submenu
            var colorItem = new MenuItem { Header = "🎨  Farbe ändern" };
            var colors = new[] {
                ("#E74C3C", "Rot"), ("#E67E22", "Orange"), ("#F1C40F", "Gelb"),
                ("#2ECC71", "Grün"), ("#3498DB", "Blau"), ("#9B59B6", "Lila"),
                ("#1ABC9C", "Türkis"), ("#34495E", "Dunkelgrau"), ("#BF247A", "Pink")
            };
            foreach (var (hex, label) in colors)
            {
                var colorMi = new MenuItem { Header = label, Foreground = Brushes.Black };
                colorMi.Icon = new Border {
                    Width = 14, Height = 14, CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))
                };
                var capturedHex = hex;
                colorMi.Click += (_, _) => _vm.UpdateProjectColor(pid, capturedHex);
                colorItem.Items.Add(colorMi);
            }
            ctxMenu.Items.Add(colorItem);
            ctxMenu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "🗑  Löschen", Foreground = Brushes.Black };
            deleteItem.Click += (_, _) => {
                if (ModernMessageBox.ShowConfirm($"Projekt \"{pName}\" wirklich löschen?", "Projekt löschen"))
                    _vm.DeleteProject(pid);
            };
            ctxMenu.Items.Add(deleteItem);
            card.ContextMenu = ctxMenu;

            ProjectListPanel.Children.Add(card);
        }
    }

    private void AddToGrid(UIElement element, int row, int col)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, col);
        CalendarGrid.Children.Add(element);
    }

    private void AddResource_Click(object sender, RoutedEventArgs e)
    {
        var newResource = ResourceEditDialog.ShowNew();
        if (newResource != null)
        {
            Database.Instance.AddResource(newResource);
            _vm.Load();
        }
    }

    private void AddProject_Click(object sender, RoutedEventArgs e)
    {
        var newProject = ProjectEditDialog.ShowNew();
        if (newProject != null)
        {
            Database.Instance.AddProject(newProject);
            _vm.Load();
        }
    }

    private void ShowProjectMenu(Border cell, long resourceId, DateTime date)
    {
        var menu = new ContextMenu();
        foreach (var project in _vm.Projects)
        {
            var mi = new MenuItem { Header = project.Name, Foreground = Brushes.Black };
            try {
                var c = (Color)ColorConverter.ConvertFromString(project.Color);
                mi.Icon = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(c) };
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

    private void ShowHardwareProjectMenu(long resourceId, long hardwareId, DateTime date, List<ResourceAllocation> projAllocs)
    {
        var menu = new ContextMenu();
        foreach (var alloc in projAllocs)
        {
            var project = _vm.Projects.FirstOrDefault(p => p.Id == alloc.ProjectId);
            var mi = new MenuItem { Header = project?.Name ?? $"Projekt #{alloc.ProjectId}", Foreground = Brushes.Black };
            var pid = alloc.ProjectId;
            mi.Click += (_, _) => _vm.AddHardwareAllocation(resourceId, hardwareId, pid, date);
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    private List<HwAllocationSpan> GetHardwareAllocationSpans(long resourceId, DateTime start, int days)
    {
        var byKey = new Dictionary<(long hwId, long projId), List<(int col, DateTime date, long id)>>();
        for (int d = 0; d < days; d++)
        {
            var date = start.AddDays(d);
            var allocs = _vm.GetHardwareAllocationsForResource(resourceId, date);
            foreach (var a in allocs)
            {
                var key = (a.HardwareId, a.ProjectId);
                if (!byKey.ContainsKey(key)) byKey[key] = [];
                byKey[key].Add((d, date, a.Id));
            }
        }

        var spans = new List<HwAllocationSpan>();
        foreach (var ((hwId, projId), entries) in byKey)
        {
            var sorted = entries.OrderBy(e => e.col).ToList();
            int i = 0;
            while (i < sorted.Count)
            {
                var spanStart = sorted[i].col;
                var startDate = sorted[i].date;
                var ids = new List<long> { sorted[i].id };
                int j = i + 1;
                while (j < sorted.Count && sorted[j].col == sorted[j - 1].col + 1)
                {
                    ids.Add(sorted[j].id);
                    j++;
                }
                var endDate = sorted[j - 1].date;
                spans.Add(new HwAllocationSpan(hwId, projId, spanStart, j - i, startDate, endDate, ids));
                i = j;
            }
        }
        return spans;
    }

    // --- Hardware list for drag & drop ---
    private void BuildHardwareList()
    {
        if (HardwareListPanel == null) return;
        HardwareListPanel.Children.Clear();

        foreach (var hw in _vm.HardwareResources)
        {
            Color c;
            try { c = (Color)ColorConverter.ConvertFromString(hw.Color ?? "#17a2b8"); }
            catch { c = Color.FromRgb(0x17, 0xa2, 0xb8); }

            var dot = new Border {
                Width = 12, Height = 12, CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(c),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
            };
            var icon = new TextBlock {
                Text = "🖥", FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            var name = new TextBlock {
                Text = hw.Name, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                VerticalAlignment = VerticalAlignment.Center
            };
            var typeText = new TextBlock {
                Text = string.IsNullOrEmpty(hw.Type) ? "" : $"  ({hw.Type})",
                FontSize = 10, Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(dot);
            sp.Children.Add(icon);
            sp.Children.Add(name);
            sp.Children.Add(typeText);

            var card = new Border {
                Background = Brushes.White, CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand, Child = sp,
                BorderBrush = GridLine, BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.06 }
            };

            var hwId = hw.Id;
            var hwName = hw.Name;
            card.MouseMove += (s, e) => {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    var data = new DataObject();
                    data.SetData("HardwareId", hwId);
                    DragDrop.DoDragDrop((Border)s!, data, DragDropEffects.Copy);
                }
            };

            // Right-click: delete
            var ctxMenu = new ContextMenu();
            var deleteItem = new MenuItem { Header = "🗑  Löschen", Foreground = Brushes.Black };
            deleteItem.Click += (_, _) => {
                if (ModernMessageBox.ShowConfirm($"Hardware \"{hwName}\" wirklich löschen?", "Hardware löschen"))
                    _vm.DeleteHardware(hwId);
            };
            ctxMenu.Items.Add(deleteItem);
            card.ContextMenu = ctxMenu;

            HardwareListPanel.Children.Add(card);
        }
    }

    private void AddHardware_Click(object sender, RoutedEventArgs e)
    {
        _vm.AddHardwareCommand.Execute(null);
        BuildHardwareList();
    }

    private void OpenKanban_Click(object sender, RoutedEventArgs e)
    {
        ProjectKanbanDialog.ShowGlobal();
    }
}

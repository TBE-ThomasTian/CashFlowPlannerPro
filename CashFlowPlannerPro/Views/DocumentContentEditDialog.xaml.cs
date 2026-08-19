using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class DocumentContentEditDialog : Window
{
    private const uint MonitorDefaultToNearest = 2;
    private const double WorkAreaInset = 16;
    private readonly DocumentContent _workingContent;
    private readonly double _unscaledWidth;
    private readonly double _unscaledHeight;
    private readonly double _unscaledMinWidth;
    private readonly double _unscaledMinHeight;

    public DocumentContent? ResultContent { get; private set; }

    public DocumentContentEditDialog(DocumentContent? content, string documentName = "Dokument")
    {
        InitializeComponent();

        _unscaledWidth = Width;
        _unscaledHeight = Height;
        _unscaledMinWidth = MinWidth;
        _unscaledMinHeight = MinHeight;

        _workingContent = (content ?? new DocumentContent()).DeepClone();
        DialogTitle.Text = $"{documentName}: Dokumentinhalt";
        Title = $"{documentName}: Dokumentinhalt bearbeiten";

        TbHeader.Text = _workingContent.Header;
        TbPreText.Text = _workingContent.PreText;
        TbPostText.Text = _workingContent.PostText;
        TbInternalNote.Text = _workingContent.InternalNote;
        LineItemsGrid.ItemsSource = _workingContent.LineItems;

        UpdatePositionUi();
    }

    private void AddLine_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingGridEdit();

        var line = new DocumentLineItem
        {
            SortOrder = _workingContent.LineItems.Count,
            PositionNumber = (_workingContent.LineItems.Count + 1).ToString(CultureInfo.InvariantCulture),
            Quantity = 1
        };

        _workingContent.LineItems.Add(line);
        LineItemsGrid.SelectedItem = line;
        LineItemsGrid.ScrollIntoView(line);
        UpdatePositionUi();
        LineItemsGrid.Focus();
    }

    private void DeleteLine_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingGridEdit();
        if (LineItemsGrid.SelectedItem is not DocumentLineItem selected)
            return;

        var oldIndex = _workingContent.LineItems.IndexOf(selected);
        _workingContent.LineItems.Remove(selected);
        NormalizeLineOrdering(fillMissingPositionNumbers: false);

        if (_workingContent.LineItems.Count > 0)
            LineItemsGrid.SelectedIndex = Math.Min(oldIndex, _workingContent.LineItems.Count - 1);

        UpdatePositionUi();
    }

    private void MoveLineUp_Click(object sender, RoutedEventArgs e) => MoveSelectedLine(-1);

    private void MoveLineDown_Click(object sender, RoutedEventArgs e) => MoveSelectedLine(1);

    private void MoveSelectedLine(int offset)
    {
        CommitPendingGridEdit();
        if (LineItemsGrid.SelectedItem is not DocumentLineItem selected)
            return;

        var oldIndex = _workingContent.LineItems.IndexOf(selected);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= _workingContent.LineItems.Count)
            return;

        _workingContent.LineItems.Move(oldIndex, newIndex);
        NormalizeLineOrdering(fillMissingPositionNumbers: false);
        LineItemsGrid.SelectedItem = selected;
        LineItemsGrid.ScrollIntoView(selected);
        UpdatePositionUi();
    }

    private void LineItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePositionUi();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitPendingGridEdit())
        {
            ModernMessageBox.ShowError(
                "Eine Position enthält einen ungültigen Zahlenwert. Bitte korrigieren Sie die markierte Zelle.",
                "Dokumentinhalt");
            return;
        }

        var invalidLine = _workingContent.LineItems.FirstOrDefault(line =>
            !IsFinite(line.Quantity) ||
            !IsFinite(line.UnitPrice) ||
            !IsFinite(line.DiscountPercent) || line.DiscountPercent is < 0 or > 100 ||
            !IsFinite(line.TaxRate) || line.TaxRate < 0 ||
            !IsFinite(line.NetAmount) ||
            !IsFinite(line.GrossAmount));

        if (invalidLine != null)
        {
            LineItemsGrid.SelectedItem = invalidLine;
            LineItemsGrid.ScrollIntoView(invalidLine);
            ContentTabs.SelectedItem = PositionsTab;
            ModernMessageBox.ShowError(
                "Bitte prüfen Sie Menge, Preise, Rabatt, MwSt, Netto und Brutto. Rabatt muss zwischen 0 und 100 % liegen; MwSt darf nicht negativ sein.",
                "Ungültige Position");
            return;
        }

        _workingContent.Header = TbHeader.Text.Trim();
        _workingContent.PreText = TbPreText.Text.Trim();
        _workingContent.PostText = TbPostText.Text.Trim();
        _workingContent.InternalNote = TbInternalNote.Text.Trim();
        NormalizeLineOrdering(fillMissingPositionNumbers: true);

        ResultContent = _workingContent.DeepClone();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Save_Click(sender, e);
            e.Handled = true;
        }
    }

    private bool CommitPendingGridEdit()
    {
        var cellCommitted = LineItemsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        var rowCommitted = LineItemsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        return cellCommitted && rowCommitted;
    }

    private void NormalizeLineOrdering(bool fillMissingPositionNumbers)
    {
        for (var index = 0; index < _workingContent.LineItems.Count; index++)
        {
            var line = _workingContent.LineItems[index];
            line.SortOrder = index;
            if (fillMissingPositionNumbers && string.IsNullOrWhiteSpace(line.PositionNumber))
                line.PositionNumber = (index + 1).ToString(CultureInfo.InvariantCulture);
        }
    }

    private void UpdatePositionUi()
    {
        var count = _workingContent.LineItems.Count;
        PositionsTab.Header = $"Positionen ({count})";
        PositionSummaryText.Text = count == 1 ? "1 Position" : $"{count} Positionen";

        var selectedIndex = LineItemsGrid.SelectedIndex;
        DeleteLineButton.IsEnabled = selectedIndex >= 0;
        MoveLineUpButton.IsEnabled = selectedIndex > 0;
        MoveLineDownButton.IsEnabled = selectedIndex >= 0 && selectedIndex < count - 1;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ConstrainToCurrentMonitor();
        TbHeader.Focus();
    }

    private void ConstrainToCurrentMonitor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var workLeft = info.WorkArea.Left / dpi.DpiScaleX;
        var workTop = info.WorkArea.Top / dpi.DpiScaleY;
        var workRight = info.WorkArea.Right / dpi.DpiScaleX;
        var workBottom = info.WorkArea.Bottom / dpi.DpiScaleY;
        ApplyScaledBoundsToWorkArea(
            workLeft,
            workTop,
            workRight,
            workBottom,
            UiScaleService.CurrentPercent / 100d);
    }

    private void ApplyScaledBoundsToWorkArea(
        double workLeft,
        double workTop,
        double workRight,
        double workBottom,
        double uiScaleFactor)
    {
        var workWidth = workRight - workLeft;
        var workHeight = workBottom - workTop;
        if (!IsFinite(workWidth) || !IsFinite(workHeight) ||
            workWidth <= 0 || workHeight <= 0 ||
            !IsFinite(uiScaleFactor) || uiScaleFactor <= 0)
        {
            return;
        }

        // Keep the normal 16-DIP breathing room whenever the work area can afford it.
        // On exceptionally small work areas, use the complete area instead of inventing
        // a minimum size that would extend beyond the monitor.
        var horizontalInset = workWidth > WorkAreaInset * 2 ? WorkAreaInset : 0;
        var verticalInset = workHeight > WorkAreaInset * 2 ? WorkAreaInset : 0;
        var availableWidth = workWidth - horizontalInset * 2;
        var availableHeight = workHeight - verticalInset * 2;

        var scaledMinWidth = Math.Min(_unscaledMinWidth * uiScaleFactor, availableWidth);
        var scaledMinHeight = Math.Min(_unscaledMinHeight * uiScaleFactor, availableHeight);
        var scaledWidth = Math.Clamp(_unscaledWidth * uiScaleFactor, scaledMinWidth, availableWidth);
        var scaledHeight = Math.Clamp(_unscaledHeight * uiScaleFactor, scaledMinHeight, availableHeight);

        // Clear earlier constraints first so this remains safe if it is applied again after
        // a scale or monitor change where the new bounds are larger than the old maximum.
        MinWidth = 0;
        MinHeight = 0;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;

        MinWidth = scaledMinWidth;
        MinHeight = scaledMinHeight;
        MaxWidth = availableWidth;
        MaxHeight = availableHeight;
        Width = scaledWidth;
        Height = scaledHeight;

        var minimumLeft = workLeft + horizontalInset;
        var maximumLeft = workRight - horizontalInset - Width;
        var minimumTop = workTop + verticalInset;
        var maximumTop = workBottom - verticalInset - Height;
        var currentLeft = IsFinite(Left) ? Left : minimumLeft;
        var currentTop = IsFinite(Top) ? Top : minimumTop;
        Left = Math.Clamp(currentLeft, minimumLeft, maximumLeft);
        Top = Math.Clamp(currentTop, minimumTop, maximumTop);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rectangle Monitor;
        public Rectangle WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

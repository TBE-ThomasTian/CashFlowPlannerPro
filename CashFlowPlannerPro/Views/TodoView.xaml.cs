using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class TodoView : UserControl
{
    private List<UserTodo> _todos = [];
    private List<Project> _projects = [];
    private string _filter = "Alle";

    private static readonly Dictionary<int, (string label, Color color)> Priorities = new() {
        [1] = ("🔴 Hoch", Color.FromRgb(0xBF, 0x39, 0x39)),
        [2] = ("🟡 Mittel", Color.FromRgb(0xD9, 0x73, 0x1A)),
        [3] = ("🟢 Niedrig", Color.FromRgb(0x27, 0xAE, 0x60))
    };

    private static readonly Dictionary<string, Color> StatusColors = new() {
        ["Offen"] = Color.FromRgb(0xBF, 0x39, 0x39),
        ["In Arbeit"] = Color.FromRgb(0xD9, 0x73, 0x1A),
        ["Erledigt"] = Color.FromRgb(0x27, 0xAE, 0x60)
    };

    public TodoView()
    {
        InitializeComponent();
        AddTodoBtn.ToolTip = TooltipService.Get("Btn_AddTodo");
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) Refresh(); };
    }

    private void Refresh()
    {
        try
        {
            var userId = App.CurrentUserId;
            _todos = Database.Instance.GetTodos(userId);
            _projects = Database.Instance.GetProjects();
            BuildList();
        }
        catch { }
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CbFilter?.SelectedItem is ComboBoxItem item)
        {
            _filter = item.Content?.ToString() ?? "Alle";
            BuildList();
        }
    }

    private void BuildList()
    {
        if (TodoListPanel == null) return;
        TodoListPanel.Children.Clear();

        var filtered = _filter == "Alle" ? _todos : _todos.Where(t => t.Status == _filter).ToList();

        if (filtered.Count == 0)
        {
            TodoListPanel.Children.Add(new TextBlock {
                Text = "Keine Aufgaben vorhanden. Klicke '➕ Neue Aufgabe' um eine zu erstellen.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                FontSize = 14, Margin = new Thickness(0, 40, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        foreach (var todo in filtered)
            TodoListPanel.Children.Add(CreateTodoCard(todo));
    }

    private Border CreateTodoCard(UserTodo todo)
    {
        var statusColor = StatusColors.GetValueOrDefault(todo.Status, Color.FromRgb(0x94, 0xA3, 0xB8));
        var prioInfo = Priorities.GetValueOrDefault(todo.Priority, ("🟡 Mittel", Color.FromRgb(0xD9, 0x73, 0x1A)));

        // Main content
        var sp = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };

        // Row 1: Checkbox + Title + Priority
        var row1 = new DockPanel();
        var checkBox = new CheckBox {
            IsChecked = todo.Status == "Erledigt",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        var todoId = todo.Id;
        checkBox.Checked += (_, _) => { ToggleStatus(todoId, true); };
        checkBox.Unchecked += (_, _) => { ToggleStatus(todoId, false); };

        var prioLabel = new TextBlock {
            Text = prioInfo.Item1, FontSize = 11,
            Foreground = new SolidColorBrush(prioInfo.Item2),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(prioLabel, Dock.Right);

        var statusBadge = new Border {
            Background = new SolidColorBrush(statusColor), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        statusBadge.Child = new TextBlock {
            Text = todo.Status, FontSize = 10, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold
        };
        DockPanel.SetDock(statusBadge, Dock.Right);

        var title = new TextBlock {
            Text = todo.Title, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            TextDecorations = todo.Status == "Erledigt" ? TextDecorations.Strikethrough : null,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };

        row1.Children.Add(prioLabel);
        row1.Children.Add(statusBadge);
        row1.Children.Add(checkBox);
        row1.Children.Add(title);
        sp.Children.Add(row1);

        // Row 2: Details
        var row2 = new WrapPanel { Margin = new Thickness(26, 4, 0, 0) };

        if (!string.IsNullOrEmpty(todo.DueDate))
        {
            var dueDateStr = todo.DueDate;
            if (DateTime.TryParseExact(todo.DueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                dueDateStr = dt.ToString("dd.MM.yyyy");
            var isOverdue = DateTime.TryParseExact(todo.DueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtCheck) && dtCheck < DateTime.Today && todo.Status != "Erledigt";
            row2.Children.Add(new TextBlock {
                Text = $"📅 {dueDateStr}", FontSize = 11, Margin = new Thickness(0, 0, 12, 0),
                Foreground = new SolidColorBrush(isOverdue ? Color.FromRgb(0xBF, 0x39, 0x39) : Color.FromRgb(0x64, 0x74, 0x8B)),
                FontWeight = isOverdue ? FontWeights.Bold : FontWeights.Normal
            });
        }

        if (todo.ProjectId.HasValue)
        {
            var proj = _projects.FirstOrDefault(p => p.Id == todo.ProjectId.Value);
            if (proj != null)
            {
                Color projColor;
                try { projColor = (Color)ColorConverter.ConvertFromString(proj.Color ?? "#3498db"); }
                catch { projColor = Color.FromRgb(0x34, 0x98, 0xdb); }
                var projPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
                projPanel.Children.Add(new Border {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(projColor),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0)
                });
                projPanel.Children.Add(new TextBlock {
                    Text = proj.Name, FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B))
                });
                row2.Children.Add(projPanel);
            }
        }

        if (!string.IsNullOrEmpty(todo.Description))
        {
            row2.Children.Add(new TextBlock {
                Text = todo.Description, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                MaxWidth = 400
            });
        }

        if (row2.Children.Count > 0) sp.Children.Add(row2);

        // Card border
        var card = new Border {
            Background = Brushes.White, CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.06 },
            Child = sp
        };

        // Left color indicator
        var outerGrid = new Grid();
        outerGrid.Children.Add(card);
        var leftBar = new Border {
            Width = 4, Background = new SolidColorBrush(statusColor),
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(8, 0, 0, 8)
        };
        outerGrid.Children.Add(leftBar);

        var wrapper = new Border { Child = outerGrid, Margin = new Thickness(0, 0, 0, 0) };

        // Double-click to edit
        card.MouseLeftButtonDown += (_, e) => {
            if (e.ClickCount == 2) { EditTodo(todo); e.Handled = true; }
        };

        // Context menu
        var ctx = new ContextMenu();
        var editMi = new MenuItem { Header = "✏️  Bearbeiten", Foreground = Brushes.Black };
        editMi.Click += (_, _) => EditTodo(todo);
        ctx.Items.Add(editMi);

        // Status submenu
        var statusMi = new MenuItem { Header = "📋  Status ändern", Foreground = Brushes.Black };
        foreach (var s in new[] { "Offen", "In Arbeit", "Erledigt" })
        {
            var si = new MenuItem { Header = s, Foreground = Brushes.Black };
            var capturedStatus = s;
            si.Click += (_, _) => { todo.Status = capturedStatus; Database.Instance.UpdateTodo(todo); Refresh(); };
            statusMi.Items.Add(si);
        }
        ctx.Items.Add(statusMi);
        ctx.Items.Add(new Separator());

        var delMi = new MenuItem { Header = "🗑  Löschen", Foreground = Brushes.Black };
        delMi.Click += (_, _) => {
            if (ModernMessageBox.ShowConfirm($"Aufgabe \"{todo.Title}\" löschen?", "Aufgabe löschen"))
            { Database.Instance.DeleteTodo(todo.Id); Refresh(); }
        };
        ctx.Items.Add(delMi);
        card.ContextMenu = ctx;

        return wrapper;
    }

    private void ToggleStatus(long todoId, bool completed)
    {
        var todo = _todos.FirstOrDefault(t => t.Id == todoId);
        if (todo == null) return;
        todo.Status = completed ? "Erledigt" : "Offen";
        Database.Instance.UpdateTodo(todo);
        Refresh();
    }

    private void EditTodo(UserTodo todo)
    {
        var dlg = new TodoEditDialog(todo, _projects);
        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            Database.Instance.UpdateTodo(dlg.Result);
            Refresh();
        }
    }

    private void AddTodo_Click(object sender, RoutedEventArgs e)
    {
        var newTodo = new UserTodo { UserId = App.CurrentUserId };
        var dlg = new TodoEditDialog(newTodo, _projects);
        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            Database.Instance.AddTodo(dlg.Result);
            Refresh();
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class ProjectKanbanDialog : Window
{
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}

public partial class ProjectKanbanDialog
{
    private readonly Project? _project;
    private readonly bool _globalMode;
    private List<ProjectMilestone> _milestones = [];
    private Dictionary<long, (string name, string color)> _projectInfo = [];

    private static readonly string[] Statuses = ["Offen", "Aktiv", "Review", "Abgeschlossen"];
    private static readonly Dictionary<string, Color> StatusColors = new() {
        ["Offen"] = Color.FromRgb(0xBF, 0x39, 0x39),
        ["Aktiv"] = Color.FromRgb(0x2A, 0x23, 0x59),
        ["Review"] = Color.FromRgb(0xD9, 0x73, 0x1A),
        ["Abgeschlossen"] = Color.FromRgb(0x27, 0xAE, 0x60)
    };

    private static readonly Dictionary<int, string> PriorityLabels = new() {
        [1] = "🔴 Hoch", [2] = "🟡 Mittel", [3] = "🟢 Niedrig"
    };

    // Single project mode
    public ProjectKanbanDialog(Project project)
    {
        InitializeComponent();
        _project = project;
        _globalMode = false;
        TbProjectName.Text = project.Name;
        TbProjectInfo.Text = $"{(string.IsNullOrEmpty(project.ProjectNumber) ? "" : $"#{project.ProjectNumber}  •  ")}{project.Client}  •  Status: {project.Status}";
        if (Application.Current?.MainWindow != null) Owner = Application.Current.MainWindow;
        CloseBtn.ToolTip = TooltipService.Get("Btn_Close");
        AddMilestoneBtn.ToolTip = TooltipService.Get("Btn_AddMilestone");
        AddMilestoneBtn.IsEnabled = App.CanEdit(PageKeys.Resources);
        LoadMilestones();
    }

    // Global mode — all projects
    public ProjectKanbanDialog()
    {
        InitializeComponent();
        _project = null;
        _globalMode = true;
        TbProjectName.Text = "Alle Projekte — Meilensteine";
        TbProjectInfo.Text = "Globale Übersicht aller Simulationsfälle und Meilensteine";
        if (Application.Current?.MainWindow != null) Owner = Application.Current.MainWindow;
        CloseBtn.ToolTip = TooltipService.Get("Btn_Close");
        AddMilestoneBtn.ToolTip = TooltipService.Get("Btn_AddMilestone");
        AddMilestoneBtn.IsEnabled = App.CanEdit(PageKeys.Resources);
        LoadMilestones();
    }

    public static void Show(Project project)
    {
        var dlg = new ProjectKanbanDialog(project);
        dlg.ShowDialog();
    }

    public static void ShowGlobal()
    {
        var dlg = new ProjectKanbanDialog();
        dlg.ShowDialog();
    }

    private void LoadMilestones()
    {
        if (_globalMode)
        {
            var all = Database.Instance.GetAllMilestones();
            _milestones = all.Select(x => x.milestone).ToList();
            _projectInfo = all.ToDictionary(x => x.milestone.Id, x => (x.projectName, x.projectColor));
        }
        else
        {
            _milestones = Database.Instance.GetMilestones(_project!.Id);
        }
        RenderBoard();
    }

    private void RenderBoard()
    {
        ColOffen.Children.Clear();
        ColAktiv.Children.Clear();
        ColReview.Children.Clear();
        ColAbgeschlossen.Children.Clear();

        foreach (var m in _milestones)
        {
            var card = CreateCard(m);
            GetColumn(m.Status).Children.Add(card);
        }
    }

    private StackPanel GetColumn(string status) => status switch {
        "Aktiv" => ColAktiv,
        "Review" => ColReview,
        "Abgeschlossen" => ColAbgeschlossen,
        _ => ColOffen
    };

    private Border CreateCard(ProjectMilestone m)
    {
        var canEdit = App.CanEdit(PageKeys.Resources);
        var sp = new StackPanel { Margin = new Thickness(4) };

        // Title + Priority
        var header = new DockPanel();
        var priorityDot = new TextBlock {
            Text = PriorityLabels.GetValueOrDefault(m.Priority, "🟡 Mittel"),
            FontSize = 10, VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(priorityDot, Dock.Right);
        header.Children.Add(priorityDot);
        header.Children.Add(new TextBlock {
            Text = m.Name, FontWeight = FontWeights.SemiBold, FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            TextWrapping = TextWrapping.Wrap
        });
        sp.Children.Add(header);

        // Project label (global mode)
        if (_globalMode && _projectInfo.TryGetValue(m.Id, out var pInfo))
        {
            Color projColor;
            try { projColor = (Color)ColorConverter.ConvertFromString(pInfo.color); }
            catch { projColor = Color.FromRgb(0x34, 0x98, 0xdb); }

            var projPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            projPanel.Children.Add(new Border {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(projColor),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0)
            });
            projPanel.Children.Add(new TextBlock {
                Text = pInfo.name, FontSize = 10, Foreground = new SolidColorBrush(projColor),
                FontWeight = FontWeights.SemiBold
            });
            sp.Children.Add(projPanel);
        }

        // Details
        if (!string.IsNullOrEmpty(m.Responsible))
            sp.Children.Add(new TextBlock {
                Text = $"👤 {m.Responsible}", FontSize = 11, Foreground = Brushes.Gray,
                Margin = new Thickness(0, 4, 0, 0)
            });

        if (!string.IsNullOrEmpty(m.Deadline))
            sp.Children.Add(new TextBlock {
                Text = $"📅 {m.Deadline}", FontSize = 11, Foreground = Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 0)
            });

        if (m.HoursBudget > 0)
            sp.Children.Add(new TextBlock {
                Text = $"⏱ {m.HoursBudget:0.#}h Budget", FontSize = 11, Foreground = Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 0)
            });

        if (!string.IsNullOrEmpty(m.Notes))
            sp.Children.Add(new TextBlock {
                Text = m.Notes, FontSize = 10, Foreground = Brushes.DarkGray,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
                FontStyle = FontStyles.Italic
            });

        var statusColor = StatusColors.GetValueOrDefault(m.Status, Color.FromRgb(0x99, 0x99, 0x99));
        var card = new Border {
            Background = Brushes.White, CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(statusColor), BorderThickness = new Thickness(0, 0, 0, 3),
            Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(4, 4, 4, 4),
            Child = sp, Cursor = canEdit ? Cursors.Hand : Cursors.Arrow,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.08 },
            Tag = m.Id
        };

        // Drag to move between columns
        Point? dragStart = null;
        if (canEdit)
        {
            card.MouseLeftButtonDown += (s, e) => {
                if (e.ClickCount == 1)
                    dragStart = e.GetPosition(card);
            };
            card.MouseMove += (s, e) => {
                if (e.LeftButton == MouseButtonState.Pressed && dragStart.HasValue)
                {
                    var pos = e.GetPosition(card);
                    if (Math.Abs(pos.X - dragStart.Value.X) > 10 || Math.Abs(pos.Y - dragStart.Value.Y) > 10)
                    {
                        dragStart = null;
                        var data = new DataObject();
                        data.SetData("MilestoneId", m.Id);
                        DragDrop.DoDragDrop(card, data, DragDropEffects.Move);
                    }
                }
            };
            card.MouseLeftButtonUp += (s, e) => { dragStart = null; };

            // Double-click to edit
            card.MouseLeftButtonDown += (s, e) => {
                if (e.ClickCount == 2)
                {
                    e.Handled = true;
                    EditMilestone(m);
                }
            };
        }

        // Right-click context menu
        var ctx = new ContextMenu();
        var editItem = new MenuItem { Header = "✏️  Bearbeiten", Foreground = Brushes.Black };
        editItem.Click += (_, _) => EditMilestone(m);
        ctx.Items.Add(editItem);

        ctx.Items.Add(new Separator());

        // Quick status change
        foreach (var status in Statuses)
        {
            if (status == m.Status) continue;
            var statusItem = new MenuItem {
                Header = $"→ {status}",
                Foreground = new SolidColorBrush(StatusColors.GetValueOrDefault(status, Colors.Gray))
            };
            var capturedStatus = status;
            statusItem.Click += (_, _) => {
                if (!PermissionGuard.DemandEdit(PageKeys.Resources, "milestone.status.update")) return;
                m.Status = capturedStatus;
                Database.Instance.UpdateMilestone(m);
                LoadMilestones();
            };
            ctx.Items.Add(statusItem);
        }

        ctx.Items.Add(new Separator());
        var deleteItem = new MenuItem { Header = "🗑  Löschen", Foreground = new SolidColorBrush(Color.FromRgb(0xBF, 0x39, 0x39)) };
        deleteItem.Click += (_, _) => {
            if (!PermissionGuard.DemandEdit(PageKeys.Resources, "milestone.delete")) return;
            if (ModernMessageBox.ShowConfirm($"Meilenstein \"{m.Name}\" löschen?", "Löschen"))
            {
                if (!PermissionGuard.DemandEdit(PageKeys.Resources, "milestone.delete.confirmed")) return;
                Database.Instance.DeleteMilestone(m.Id);
                LoadMilestones();
            }
        };
        ctx.Items.Add(deleteItem);
        if (canEdit)
            card.ContextMenu = ctx;

        return card;
    }

    private void Column_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = App.CanEdit(PageKeys.Resources) && e.Data.GetDataPresent("MilestoneId")
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Column_Drop(object sender, DragEventArgs e)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "milestone.move")) return;
        if (!e.Data.GetDataPresent("MilestoneId")) return;
        var milestoneId = (long)e.Data.GetData("MilestoneId")!;
        var m = _milestones.FirstOrDefault(x => x.Id == milestoneId);
        if (m == null) return;

        string newStatus;
        if (sender is FrameworkElement fe && fe.Tag is string tag)
            newStatus = tag;
        else
            newStatus = sender == ColOffen ? "Offen"
                : sender == ColAktiv ? "Aktiv"
                : sender == ColReview ? "Review"
                : "Abgeschlossen";

        if (m.Status != newStatus)
        {
            m.Status = newStatus;
            Database.Instance.UpdateMilestone(m);
            LoadMilestones();
        }
    }

    private void AddMilestone_Click(object sender, RoutedEventArgs e)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "milestone.add")) return;
        long projectId;
        if (_globalMode)
        {
            // Show project picker
            var projects = Database.Instance.GetProjects();
            if (projects.Count == 0) { MessageBox.Show("Keine Projekte vorhanden."); return; }
            var menu = new ContextMenu();
            long selectedPid = 0;
            foreach (var p in projects)
            {
                var mi = new MenuItem { Header = p.Name, Foreground = Brushes.Black, Tag = p.Id };
                mi.Click += (sender2, _) => {
                    selectedPid = (long)((MenuItem)sender2!).Tag;
                    var dlg2 = new MilestoneEditDialog(new ProjectMilestone { ProjectId = selectedPid });
                    if (dlg2.ShowDialog() == true && dlg2.Result != null)
                    {
                        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "milestone.add.confirmed")) return;
                        Database.Instance.AddMilestone(dlg2.Result);
                        LoadMilestones();
                    }
                };
                menu.Items.Add(mi);
            }
            menu.PlacementTarget = (Button)sender;
            menu.IsOpen = true;
            return;
        }
        else
        {
            projectId = _project!.Id;
        }

        var editDlg = new MilestoneEditDialog(new ProjectMilestone { ProjectId = projectId });
        if (editDlg.ShowDialog() == true && editDlg.Result != null)
        {
            if (!PermissionGuard.DemandEdit(PageKeys.Resources, "milestone.add.confirmed")) return;
            Database.Instance.AddMilestone(editDlg.Result);
            LoadMilestones();
        }
    }

    private void EditMilestone(ProjectMilestone m)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Resources, "milestone.update")) return;
        var dlg = new MilestoneEditDialog(m);
        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            if (!PermissionGuard.DemandEdit(PageKeys.Resources, "milestone.update.confirmed")) return;
            Database.Instance.UpdateMilestone(dlg.Result);
            LoadMilestones();
        }
    }
}

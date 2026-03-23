using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;

namespace CashFlowPlannerPro.Views;

public partial class AdminView : UserControl
{
    public AdminView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) => {
            if (e.NewValue is true) Refresh();
        };
    }

    private void Refresh()
    {
        try
        {
            BuildUserList();
            BuildRoleList();
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowError(ex.Message, "Verwaltung Fehler");
        }
    }

    // --- Users ---
    private void BuildUserList()
    {
        UserListPanel.Children.Clear();
        var users = Database.Instance.GetUsernames();
        var roles = Database.Instance.GetRoles();

        foreach (var username in users)
        {
            var roleId = Database.Instance.GetUserRoleId(username);
            var roleName = roles.FirstOrDefault(r => r.Id == roleId)?.Name ?? "Keine Rolle";
            var fullName = Database.Instance.GetFullName(username) ?? "";

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Border {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x23, 0x59)),
                Child = new TextBlock {
                    Text = username[..1].ToUpper(), Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold, FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Margin = new Thickness(0, 0, 12, 0)
            });

            var info = new StackPanel();
            info.Children.Add(new TextBlock {
                Text = string.IsNullOrEmpty(fullName) ? username : $"{fullName} ({username})",
                FontWeight = FontWeights.SemiBold, FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B))
            });

            // Role dropdown
            var roleCombo = new ComboBox {
                Width = 150, FontSize = 11, Margin = new Thickness(0, 2, 0, 0),
                Background = Brushes.White
            };
            roleCombo.Items.Add(new ComboBoxItem { Content = "Keine Rolle", Tag = (long)0 });
            foreach (var role in roles)
            {
                var item = new ComboBoxItem { Content = role.Name, Tag = role.Id };
                if (role.Id == roleId) item.IsSelected = true;
                roleCombo.Items.Add(item);
            }
            if (roleId == null) ((ComboBoxItem)roleCombo.Items[0]).IsSelected = true;

            var capturedUser = username;
            roleCombo.SelectionChanged += (_, _) => {
                if (roleCombo.SelectedItem is ComboBoxItem ci && ci.Tag is long rid && rid > 0)
                    Database.Instance.SetUserRole(capturedUser, rid);
            };
            info.Children.Add(roleCombo);
            sp.Children.Add(info);

            // Delete button
            var deleteBtn = new Button {
                Content = "🗑", FontSize = 14, Cursor = System.Windows.Input.Cursors.Hand,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(4)
            };
            if (username != "admin")
            {
                deleteBtn.Click += (_, _) => {
                    if (ModernMessageBox.ShowConfirm($"Benutzer \"{capturedUser}\" wirklich löschen?", "Benutzer löschen"))
                    {
                        Database.Instance.DeleteUser(capturedUser);
                        Refresh();
                    }
                };
            }
            else
            {
                deleteBtn.IsEnabled = false;
                deleteBtn.ToolTip = "Admin kann nicht gelöscht werden";
            }

            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(deleteBtn, Dock.Right);
            row.Children.Add(deleteBtn);
            row.Children.Add(sp);

            var card = new Border {
                Child = row, Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1)
            };
            UserListPanel.Children.Add(card);
        }
    }

    private void AddUser_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddUserDialog();
        if (dlg.ShowDialog() == true)
        {
            try
            {
                Database.Instance.AddUser(dlg.Username, dlg.Password, dlg.FullName);
                Refresh();
            }
            catch (Exception ex)
            {
                ModernMessageBox.ShowError(ex.Message, "Fehler");
            }
        }
    }

    // --- Roles ---
    private void BuildRoleList()
    {
        RoleListPanel.Children.Clear();
        var roles = Database.Instance.GetRoles();

        foreach (var role in roles)
        {
            var perms = Database.Instance.GetRolePermissions(role.Id);
            var permDict = perms.ToDictionary(p => p.PageKey, p => p.AccessLevel);

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            header.Children.Add(new TextBlock {
                Text = role.Name, FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x23, 0x59)),
                VerticalAlignment = VerticalAlignment.Center
            });
            if (role.Name != "Admin")
            {
                var delBtn = new Button {
                    Content = "🗑 Löschen", FontSize = 11, Cursor = System.Windows.Input.Cursors.Hand,
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xBF, 0x39, 0x39)),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                var capturedRole = role;
                delBtn.Click += (_, _) => {
                    if (ModernMessageBox.ShowConfirm($"Rolle \"{capturedRole.Name}\" löschen?", "Rolle löschen"))
                    {
                        Database.Instance.DeleteRole(capturedRole.Id);
                        Refresh();
                    }
                };
                DockPanel.SetDock(delBtn, Dock.Right);
                header.Children.Add(delBtn);
            }

            var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            // Header row
            int row = 0;
            grid.RowDefinitions.Add(new RowDefinition());
            AddToGrid(grid, new TextBlock { Text = "Seite", FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = Brushes.Gray }, row, 0);
            AddToGrid(grid, new TextBlock { Text = "Kein", FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center }, row, 1);
            AddToGrid(grid, new TextBlock { Text = "Lesen", FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center }, row, 2);
            AddToGrid(grid, new TextBlock { Text = "Voll", FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center }, row, 3);

            foreach (var (pageKey, label) in PageKeys.All)
            {
                row++;
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

                AddToGrid(grid, new TextBlock {
                    Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B))
                }, row, 0);

                var currentLevel = permDict.GetValueOrDefault(pageKey, "none");
                var capturedRoleId = role.Id;
                var capturedKey = pageKey;

                foreach (var (level, col) in new[] { ("none", 1), ("read", 2), ("full", 3) })
                {
                    var rb = new RadioButton {
                        IsChecked = currentLevel == level,
                        GroupName = $"role_{role.Id}_{pageKey}",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var capturedLevel = level;
                    rb.Checked += (_, _) => Database.Instance.SetRolePermission(capturedRoleId, capturedKey, capturedLevel);
                    AddToGrid(grid, rb, row, col);
                }
            }

            var sp = new StackPanel();
            sp.Children.Add(header);
            sp.Children.Add(grid);

            var card = new Border {
                Child = sp, Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 12),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1)
            };
            RoleListPanel.Children.Add(card);
        }
    }

    private void AddRole_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InputDialog("Neue Rolle", "Rollenname:");
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText))
        {
            try
            {
                Database.Instance.AddRole(new Role { Name = dlg.InputText });
                Refresh();
            }
            catch (Exception ex)
            {
                ModernMessageBox.ShowError(ex.Message, "Fehler");
            }
        }
    }

    private static void AddToGrid(Grid grid, UIElement element, int row, int col)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, col);
        grid.Children.Add(element);
    }
}

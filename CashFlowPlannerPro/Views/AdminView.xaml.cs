using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class AdminView : UserControl
{
    private List<Role> _roles = [];
    private long? _selectedRoleId;
    private bool _isRefreshing;

    public AdminView()
    {
        InitializeComponent();
        AddUserBtn.ToolTip = TooltipService.Get("Btn_AddUser");
        AddRoleBtn.ToolTip = TooltipService.Get("Btn_AddRole");
        IsVisibleChanged += (_, e) => {
            if (e.NewValue is true) Refresh();
        };
    }

    private void Refresh()
    {
        if (_isRefreshing) return;
        try
        {
            _isRefreshing = true;
            var canEdit = App.CanEdit(PageKeys.Admin);
            AddUserBtn.IsEnabled = canEdit;
            AddRoleBtn.IsEnabled = canEdit;
            _roles = Database.Instance.GetRoles();
            BuildUserList();
            BuildRoleSelector();
            BuildRoleList();
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException("admin.refresh_failed", ex);
            ModernMessageBox.ShowError($"Die Verwaltung konnte nicht geladen werden. Referenz: {reference}", "Verwaltung Fehler");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    // --- Users ---
    private void BuildUserList()
    {
        UserListPanel.Children.Clear();
        var users = Database.Instance.GetUsernames();
        var canEdit = App.CanEdit(PageKeys.Admin);

        foreach (var username in users)
        {
            var roleId = Database.Instance.GetUserRoleId(username);
            var fullName = Database.Instance.GetFullName(username) ?? "";

            var contentGrid = new Grid();
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var identityPanel = new Grid {
                MinWidth = 0,
                VerticalAlignment = VerticalAlignment.Center
            };
            identityPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            identityPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var avatar = new Border {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x23, 0x59)),
                Child = new TextBlock {
                    Text = username[..1].ToUpper(), Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold, FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Margin = new Thickness(0, 0, 12, 0)
            };
            identityPanel.Children.Add(avatar);

            var displayText = string.IsNullOrEmpty(fullName) ? username : $"{fullName} ({username})";
            var nameText = new TextBlock {
                Text = displayText,
                ToolTip = displayText,
                FontWeight = FontWeights.SemiBold, FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameText, 1);
            identityPanel.Children.Add(nameText);
            Grid.SetRow(identityPanel, 0);
            Grid.SetColumn(identityPanel, 0);
            contentGrid.Children.Add(identityPanel);

            // Role dropdown
            var roleCombo = new ComboBox {
                MinWidth = 0,
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0),
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            roleCombo.Items.Add(new ComboBoxItem { Content = "Keine Rolle", Tag = (long)0 });
            foreach (var role in _roles)
            {
                var item = new ComboBoxItem { Content = role.Name, Tag = role.Id };
                if (role.Id == roleId) item.IsSelected = true;
                roleCombo.Items.Add(item);
            }
            if (roleId == null) ((ComboBoxItem)roleCombo.Items[0]).IsSelected = true;

            var capturedUser = username;
            var isCurrentUser = string.Equals(username, App.CurrentUsername, StringComparison.OrdinalIgnoreCase);
            roleCombo.IsEnabled = canEdit && !isCurrentUser;
            if (isCurrentUser)
                roleCombo.ToolTip = "Die eigene Rolle kann während einer aktiven Sitzung nicht geändert werden.";
            roleCombo.SelectionChanged += (_, _) => {
                if (!CheckAdminAccess()) { Refresh(); return; }
                if (roleCombo.SelectedItem is ComboBoxItem ci && ci.Tag is long rid)
                {
                    try
                    {
                        Database.Instance.SetUserRole(capturedUser, rid > 0 ? rid : null);
                        AppLogger.Audit("admin.user_role.changed", capturedUser, success: true, new { roleId = rid });
                    }
                    catch (InvalidOperationException ex)
                    {
                        ModernMessageBox.ShowError(ex.Message, "Rolle ändern");
                        Refresh();
                    }
                    catch (Exception ex)
                    {
                        var reference = AppLogger.LogException("admin.user_role_change_failed", ex, new { capturedUser });
                        ModernMessageBox.ShowError($"Die Rolle konnte nicht geändert werden. Referenz: {reference}", "Rolle ändern");
                        Refresh();
                    }
                }
            };
            Grid.SetRow(roleCombo, 1);
            Grid.SetColumn(roleCombo, 0);
            Grid.SetColumnSpan(roleCombo, 2);
            contentGrid.Children.Add(roleCombo);

            // Delete button
            var isBuiltInAdmin = string.Equals(username, "admin", StringComparison.Ordinal);
            var isProtectedUser = isBuiltInAdmin || isCurrentUser;
            var deleteBtn = new Button {
                Content = "\uE74D",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Style = (Style)FindResource("CompactDeleteButton"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                IsEnabled = canEdit && !isProtectedUser,
                ToolTip = isBuiltInAdmin
                    ? "Der integrierte Administrator kann nicht deaktiviert werden."
                    : isCurrentUser
                    ? "Der aktuell angemeldete Benutzer kann sich nicht selbst deaktivieren."
                    : $"Benutzer „{username}“ deaktivieren"
            };
            ToolTipService.SetShowOnDisabled(deleteBtn, true);
            AutomationProperties.SetAutomationId(deleteBtn, $"DeleteUser_{username}");
            AutomationProperties.SetName(deleteBtn, isProtectedUser
                ? "Dieser Benutzer kann in der aktuellen Sitzung nicht deaktiviert werden"
                : $"Benutzer {username} deaktivieren");

            if (!isProtectedUser)
            {
                deleteBtn.Click += (_, _) => {
                    if (!CheckAdminAccess()) return;
                    if (ModernMessageBox.ShowConfirm(
                            $"Benutzer \"{capturedUser}\" wirklich deaktivieren?\n\n" +
                            "Die Anmeldung wird gesperrt und bereits offene Sitzungen verlieren ihre Gültigkeit. " +
                            "Mitarbeiterressource, Projektplanungen, Aufgaben, Einstellungen und Zeiterfassungen bleiben erhalten.",
                            "Benutzer deaktivieren"))
                    {
                        if (!CheckAdminAccess()) return;
                        try
                        {
                            Database.Instance.DeleteUser(capturedUser);
                            AppLogger.Audit("admin.user.deactivated", capturedUser, success: true);
                            Refresh();
                        }
                        catch (InvalidOperationException ex)
                        {
                            ModernMessageBox.ShowError(ex.Message, "Benutzer deaktivieren");
                        }
                        catch (Exception ex)
                        {
                            var reference = AppLogger.LogException("admin.user_deactivate_failed", ex, new { capturedUser });
                            ModernMessageBox.ShowError($"Der Benutzer konnte nicht deaktiviert werden. Referenz: {reference}", "Benutzer deaktivieren");
                        }
                    }
                };
            }
            else
            {
                deleteBtn.Cursor = Cursors.Arrow;
            }
            Grid.SetRow(deleteBtn, 0);
            Grid.SetColumn(deleteBtn, 1);
            contentGrid.Children.Add(deleteBtn);

            var card = new Border {
                Child = contentGrid, Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1)
            };
            UserListPanel.Children.Add(card);
        }
    }

    private void BuildRoleSelector()
    {
        RoleSelector.Items.Clear();
        foreach (var role in _roles)
        {
            var item = new ComboBoxItem { Content = role.Name, Tag = role.Id };
            if (_selectedRoleId == role.Id)
                item.IsSelected = true;
            RoleSelector.Items.Add(item);
        }

        if (_roles.Count == 0)
        {
            _selectedRoleId = null;
            return;
        }

        if (_selectedRoleId == null || _roles.All(r => r.Id != _selectedRoleId.Value))
            _selectedRoleId = _roles[0].Id;

        foreach (var item in RoleSelector.Items.OfType<ComboBoxItem>())
        {
            item.IsSelected = Equals(item.Tag, _selectedRoleId);
        }
    }

    private bool CheckAdminAccess()
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Admin, "admin.manage"))
        {
            ModernMessageBox.ShowError("Keine Berechtigung für diese Aktion. Nur Admins dürfen Benutzer und Rollen verwalten.", "Zugriff verweigert");
            return false;
        }
        return true;
    }

    private void AddUser_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckAdminAccess()) return;
        var dlg = new AddUserDialog();
        if (dlg.ShowDialog() == true)
        {
            if (!CheckAdminAccess()) return;
            try
            {
                var resource = Database.Instance.AddUserWithResource(dlg.Username, dlg.Password, dlg.FullName);
                AppLogger.Audit("admin.user.created", dlg.Username, success: true);
                Refresh();
                ModernMessageBox.ShowSuccess(
                    $"Der Benutzer \"{dlg.Username}\" wurde angelegt.\n\n" +
                    $"Die Mitarbeiterressource \"{resource.Name}\" wurde automatisch zugeordnet.",
                    "Benutzer angelegt");
            }
            catch (Exception ex)
            {
                var reference = AppLogger.LogException("admin.user_add_failed", ex);
                ModernMessageBox.ShowError(
                    $"Der Benutzer konnte nicht angelegt werden. Referenz: {reference}",
                    "Benutzer anlegen");
            }
        }
    }

    // --- Roles ---
    private void BuildRoleList()
    {
        RoleListPanel.Children.Clear();
        if (_selectedRoleId == null) return;

        var role = _roles.FirstOrDefault(r => r.Id == _selectedRoleId.Value);
        if (role == null) return;
        var canEdit = App.CanEdit(PageKeys.Admin);

        var perms = Database.Instance.GetRolePermissions(role.Id);
        var permDict = perms.ToDictionary(p => p.PageKey, p => p.AccessLevel);

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var titlePanel = new StackPanel();
        titlePanel.Children.Add(new TextBlock {
            Text = role.Name, FontSize = 14, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x23, 0x59)),
            VerticalAlignment = VerticalAlignment.Center
        });
        if (!string.IsNullOrWhiteSpace(role.Description))
        {
            titlePanel.Children.Add(new TextBlock {
                Text = role.Description, FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
        header.Children.Add(titlePanel);
        if (role.Name != "Admin")
        {
            var delBtn = new Button {
                Content = "🗑 Löschen", FontSize = 11, Cursor = System.Windows.Input.Cursors.Hand,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xBF, 0x39, 0x39)),
                HorizontalAlignment = HorizontalAlignment.Right,
                IsEnabled = canEdit
            };
            var capturedRole = role;
            delBtn.Click += (_, _) => {
                if (!CheckAdminAccess()) return;
                if (ModernMessageBox.ShowConfirm($"Rolle \"{capturedRole.Name}\" löschen?", "Rolle löschen"))
                {
                    if (!CheckAdminAccess()) return;
                    try
                    {
                        Database.Instance.DeleteRole(capturedRole.Id);
                        AppLogger.Audit("admin.role.deleted", capturedRole.Name, success: true, new { capturedRole.Id });
                        _selectedRoleId = null;
                        Refresh();
                    }
                    catch (InvalidOperationException ex)
                    {
                        ModernMessageBox.ShowError(ex.Message, "Rolle löschen");
                    }
                    catch (Exception ex)
                    {
                        var reference = AppLogger.LogException("admin.role_delete_failed", ex, new { capturedRole.Id });
                        ModernMessageBox.ShowError($"Die Rolle konnte nicht gelöscht werden. Referenz: {reference}", "Rolle löschen");
                    }
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
                    VerticalAlignment = VerticalAlignment.Center,
                    IsEnabled = canEdit
                };
                var capturedLevel = level;
                rb.Checked += (_, _) => {
                    if (!CheckAdminAccess()) { Refresh(); return; }
                    try
                    {
                        Database.Instance.SetRolePermission(capturedRoleId, capturedKey, capturedLevel);
                        AppLogger.Audit(
                            "admin.role_permission.changed",
                            capturedKey,
                            success: true,
                            new { roleId = capturedRoleId, access = capturedLevel });
                    }
                    catch (InvalidOperationException ex)
                    {
                        ModernMessageBox.ShowError(ex.Message, "Berechtigung ändern");
                        Refresh();
                    }
                    catch (Exception ex)
                    {
                        var reference = AppLogger.LogException(
                            "admin.role_permission_change_failed",
                            ex,
                            new { roleId = capturedRoleId, page = capturedKey });
                        ModernMessageBox.ShowError($"Die Berechtigung konnte nicht geändert werden. Referenz: {reference}", "Berechtigung ändern");
                        Refresh();
                    }
                };
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

    private void RoleSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing) return;
        if (RoleSelector.SelectedItem is ComboBoxItem item && item.Tag is long roleId)
        {
            _selectedRoleId = roleId;
            BuildRoleList();
        }
    }

    private void AddRole_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckAdminAccess()) return;
        var dlg = new InputDialog("Neue Rolle", "Rollenname:");
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText))
        {
            if (!CheckAdminAccess()) return;
            try
            {
                Database.Instance.AddRole(new Role { Name = dlg.InputText });
                AppLogger.Audit("admin.role.created", dlg.InputText, success: true);
                _selectedRoleId = null;
                Refresh();
            }
            catch (Exception ex)
            {
                var reference = AppLogger.LogException("admin.role_add_failed", ex);
                ModernMessageBox.ShowError($"Die Rolle konnte nicht angelegt werden. Referenz: {reference}", "Fehler");
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

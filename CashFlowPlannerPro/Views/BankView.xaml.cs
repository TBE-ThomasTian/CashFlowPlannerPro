using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class BankView : UserControl
{
    private readonly ObservableCollection<BankAccountOption> _accounts = [];
    private readonly ObservableCollection<BankTransactionPreviewRow> _transactions = [];
    private readonly Dictionary<string, BankAccount> _localAccountsByExternalId = new(StringComparer.Ordinal);
    private readonly Dictionary<long, List<BankTransaction>> _localTransactionsByAccountId = [];
    private bool _isBusy;
    private bool _isSelectingAccount;
    private bool _hasLoadedAccounts;
    private bool _isSubscribed;
    private bool _isIdleStatus = true;

    public BankView()
    {
        InitializeComponent();
        AccountCombo.ItemsSource = _accounts;
        TransactionsGrid.ItemsSource = _transactions;
        FromDatePicker.SelectedDate = DateTime.Today.AddMonths(-3);
        ToDatePicker.SelectedDate = DateTime.Today;
        ApplyLocalization();
        ApplyPermissionState();
        Loaded += (_, _) =>
        {
            if (_isSubscribed) return;
            LocalizationManager.LanguageChanged += OnLanguageChanged;
            _isSubscribed = true;
        };
        Unloaded += (_, _) =>
        {
            if (!_isSubscribed) return;
            LocalizationManager.LanguageChanged -= OnLanguageChanged;
            _isSubscribed = false;
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLocalization();
        ApplyPermissionState();
    }

    public async Task ActivateAsync()
    {
        if (App.IsDemoMode)
        {
            SetStatus(LocalizationManager.Get("BankDemoDisabled"), Brushes.DarkOrange);
            ApplyPermissionState();
            return;
        }

        if (!PermissionGuard.EnsureSessionValid("bank.activate") || !App.CanView(PageKeys.Bank))
            return;

        if (_hasLoadedAccounts || _isBusy)
            return;

        // Imported movements are application data and must remain available
        // without an API token or a working sevDesk connection.
        if (!await LoadLocalBankDataAsync())
            await LoadAccountsAsync(forceRefresh: false);
    }

    private void ApplyLocalization()
    {
        TitleText.Text = LocalizationManager.Get("BankTitle");
        SubtitleText.Text = LocalizationManager.Get("BankSubtitle");
        AccountLabelText.Text = LocalizationManager.Get("BankAccount");
        BalanceLabelText.Text = LocalizationManager.Get("BankBalance");
        BalanceHintText.Text = LocalizationManager.Get("BankBalanceHint");
        LastSyncLabelText.Text = LocalizationManager.Get("BankLastSync");
        ProviderHintText.Text = LocalizationManager.Get("BankProviderHint");
        FromLabelText.Text = LocalizationManager.Get("BankFrom");
        ToLabelText.Text = LocalizationManager.Get("BankTo");
        RefreshAccountsButton.Content = LocalizationManager.Get("BankRefreshAccounts");
        LoadTransactionsButton.Content = LocalizationManager.Get("BankLoadTransactions");
        SelectNewButton.Content = LocalizationManager.Get("BankSelectNew");
        ImportButton.Content = LocalizationManager.Get("BankImportSelection");
        CreateFixedCostButton.Content = LocalizationManager.Get("BankCreateFixedCost");
        BusyText.Text = LocalizationManager.Get("BankLoading");

        TransactionsGrid.Columns[0].Header = LocalizationManager.Get("BankImportColumn");
        TransactionsGrid.Columns[1].Header = LocalizationManager.Get("BankValueDate");
        TransactionsGrid.Columns[2].Header = LocalizationManager.Get("BankPayee");
        TransactionsGrid.Columns[3].Header = LocalizationManager.Get("BankPurpose");
        TransactionsGrid.Columns[4].Header = LocalizationManager.Get("BankAmount");
        TransactionsGrid.Columns[5].Header = LocalizationManager.Get("BankSevDeskStatus");
        TransactionsGrid.Columns[6].Header = LocalizationManager.Get("BankImportState");

        foreach (var row in _transactions)
            row.RefreshLocalization();

        if (_isIdleStatus || string.IsNullOrWhiteSpace(StatusText.Text))
            SetStatus(LocalizationManager.Get("BankStatusIdle"), Brushes.Gray, isIdle: true);
    }

    private void ApplyPermissionState()
    {
        var canView = App.CanView(PageKeys.Bank) && !App.IsDemoMode;
        var canEdit = App.CanEdit(PageKeys.Bank) && !App.IsDemoMode;
        AccountCombo.IsEnabled = canView && !_isBusy;
        RefreshAccountsButton.IsEnabled = canView && !_isBusy;
        LoadTransactionsButton.IsEnabled = canView && !_isBusy;
        SelectNewButton.IsEnabled = canEdit && !_isBusy && _transactions.Any(row => row.CanImport && !row.ExistsLocally);
        ImportButton.IsEnabled = canEdit && !_isBusy
            && _transactions.Any(row => row.CanImport && row.IsSelected);
        CreateFixedCostButton.IsEnabled = canEdit && App.CanEdit(PageKeys.Fixkosten) && !_isBusy
            && TransactionsGrid.SelectedItem is BankTransactionPreviewRow
            {
                ExistsLocally: true,
                Source: { Amount: < 0 }
            };

        if (App.IsDemoMode)
            SetStatus(LocalizationManager.Get("BankDemoDisabled"), Brushes.DarkOrange);
    }

    private async void RefreshAccountsButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadAccountsAsync(forceRefresh: true);
    }

    private void AccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSelectingAccount || _isBusy || AccountCombo.SelectedItem is not BankAccountOption account)
            return;

        UpdateAccountSummary(account.Source);
        ShowLocalTransactions(account);
        ApplyPermissionState();
    }

    private async void LoadTransactionsButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadTransactionsAsync();
    }

    private void SelectNewButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _transactions)
            row.IsSelected = row.CanImport && !row.ExistsLocally;

        ApplyPermissionState();
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.IsDemoMode)
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("BankDemoDisabled"),
                LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        if (!PermissionGuard.DemandEdit(PageKeys.Bank, "bank.transactions.import"))
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("BankReadOnly"),
                LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        if (AccountCombo.SelectedItem is not BankAccountOption account)
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("BankSelectAccount"),
                LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        var selectedRows = _transactions
            .Where(row => row.CanImport && row.IsSelected)
            .ToList();
        if (selectedRows.Count == 0)
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("BankNothingSelected"),
                LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        try
        {
            SetBusy(true, LocalizationManager.Get("BankImporting"));
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);

            if (!PermissionGuard.DemandEdit(PageKeys.Bank, "bank.transactions.import.confirmed"))
                return;

            var sourceAccount = account.Source;
            var accountSnapshot = new BankAccount
            {
                SourceProvider = "sevdesk",
                ExternalAccountId = sourceAccount.ExternalId.Trim(),
                AccountName = sourceAccount.Name.Trim(),
                IbanMasked = sourceAccount.IbanMasked,
                Currency = NormalizeCurrency(sourceAccount.Currency),
                Balance = sourceAccount.Balance,
                LastSync = sourceAccount.LastSync?.ToString("O", CultureInfo.InvariantCulture)
                    ?? ""
            };

            var importItems = selectedRows.Select(row => ToBankTransaction(row.Source, sourceAccount)).ToList();
            var result = await Database.Instance.ImportBankTransactionsAsync(accountSnapshot, importItems);

            account.LocalAccountId = result.BankAccountId;
            try
            {
                CacheLocalSnapshot(await ReadLocalBankSnapshotAsync());
            }
            catch (Exception ex)
            {
                // The import itself is committed. A cache refresh failure must
                // not incorrectly report that successful import as failed.
                AppLogger.LogException("bank.local_cache_refresh_failed", ex);
            }

            foreach (var row in selectedRows)
            {
                row.ExistsLocally = true;
                row.IsSelected = false;
                row.ImportState = LocalizationManager.Get("BankStateImported");
            }

            SetStatus(
                string.Format(
                    LocalizationManager.Get("BankImportSuccess"),
                    result.Added,
                    result.Updated,
                    result.Skipped),
                Brushes.SeaGreen);

            ModernMessageBox.Show(
                string.Format(
                    LocalizationManager.Get("BankImportSuccess"),
                    result.Added,
                    result.Updated,
                    result.Skipped),
                LocalizationManager.Get("BankTitle"));
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException("bank.import_failed", ex);
            SetStatus(
                string.Format(LocalizationManager.Get("BankImportFailed"), $"Referenz: {reference}"),
                Brushes.IndianRed);
            ModernMessageBox.ShowError(
                string.Format(LocalizationManager.Get("BankImportFailed"), $"Referenz: {reference}"),
                LocalizationManager.Get("AppErrorTitle"));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void TransactionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ApplyPermissionState();

    private void CreateFixedCostButton_Click(object sender, RoutedEventArgs e)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Bank, "bank.fixed_cost.create") ||
            !PermissionGuard.DemandEdit(PageKeys.Fixkosten, "fixed_cost.create_from_bank"))
            return;

        if (AccountCombo.SelectedItem is not BankAccountOption account ||
            TransactionsGrid.SelectedItem is not BankTransactionPreviewRow row)
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("BankSelectTransaction"),
                LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        if (!row.ExistsLocally)
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("BankImportBeforeFixedCost"),
                LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        if (row.Source.Amount >= 0)
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("BankOnlyDebitFixedCost"),
                LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        var bookingDate = (row.Source.ValueDate ?? row.Source.EntryDate)?.ToString("dd.MM.yyyy") ?? "–";
        var suggestedDescription = FirstNonEmpty(row.Purpose, row.Payee, "Bankabbuchung");
        var dialog = new BankFixedCostDialog(
            bookingDate,
            row.Source.Amount,
            suggestedDescription,
            Database.Instance.GetCategories())
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true)
            return;

        if (!PermissionGuard.DemandEdit(PageKeys.Bank, "bank.fixed_cost.create.confirmed") ||
            !PermissionGuard.DemandEdit(PageKeys.Fixkosten, "fixed_cost.create_from_bank.confirmed"))
            return;

        var categoryId = string.IsNullOrWhiteSpace(dialog.CategoryName)
            ? null
            : Database.Instance.GetCategoryId(dialog.CategoryName);
        if (!string.IsNullOrWhiteSpace(dialog.CategoryName) && categoryId == null)
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("BankFixedCostCategoryMissing"),
                LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        try
        {
            var transaction = Database.Instance.CreateFixedCostFromBankTransaction(
                "sevdesk",
                account.Source.ExternalId,
                row.Source.ExternalId,
                dialog.Interval,
                dialog.FixedCostDescription,
                categoryId);
            row.ImportState = LocalizationManager.Get("BankStateFixedCost");
            ModernMessageBox.ShowSuccess(
                string.Format(
                    LocalizationManager.Get("BankFixedCostCreated"),
                    transaction.Description,
                    Math.Abs(transaction.Amount)),
                LocalizationManager.Get("BankCreateFixedCost"));
        }
        catch (InvalidOperationException ex)
        {
            ModernMessageBox.ShowError(ex.Message, LocalizationManager.Get("AppErrorTitle"));
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException("bank.fixed_cost_create_failed", ex);
            ModernMessageBox.ShowError(
                string.Format(LocalizationManager.Get("BankFixedCostFailed"), reference),
                LocalizationManager.Get("AppErrorTitle"));
        }
        finally
        {
            ApplyPermissionState();
        }
    }

    private async Task<bool> LoadLocalBankDataAsync()
    {
        try
        {
            SetBusy(true, LocalizationManager.Get("BankLoading"));
            var previousAccountId = (AccountCombo.SelectedItem as BankAccountOption)?.Source.ExternalId;
            var snapshot = await ReadLocalBankSnapshotAsync();

            if (!PermissionGuard.EnsureSessionValid("bank.local_data.load.completed") ||
                !App.CanView(PageKeys.Bank))
                return false;

            CacheLocalSnapshot(snapshot);
            ApplyLocalAccounts(previousAccountId);
            _hasLoadedAccounts = _accounts.Count > 0;
            return _hasLoadedAccounts;
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException("bank.local_data_load_failed", ex);
            SetStatus(
                string.Format(LocalizationManager.Get("BankLoadFailed"), $"Referenz: {reference}"),
                Brushes.IndianRed);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private Task<LocalBankSnapshot> ReadLocalBankSnapshotAsync() =>
        Database.Instance.RunWithIndependentConnectionAsync(database =>
            new LocalBankSnapshot(database.GetBankAccounts(), database.GetBankTransactions()));

    private void CacheLocalSnapshot(LocalBankSnapshot snapshot)
    {
        _localAccountsByExternalId.Clear();
        foreach (var account in snapshot.Accounts.Where(IsSevDeskAccount))
        {
            var externalId = account.ExternalAccountId.Trim();
            if (!string.IsNullOrWhiteSpace(externalId))
                _localAccountsByExternalId[externalId] = account;
        }

        _localTransactionsByAccountId.Clear();
        foreach (var group in snapshot.Transactions
                     .Where(transaction => string.Equals(
                         transaction.SourceProvider,
                         "sevdesk",
                         StringComparison.OrdinalIgnoreCase))
                     .GroupBy(transaction => transaction.BankAccountId))
        {
            _localTransactionsByAccountId[group.Key] = group.ToList();
        }
    }

    private void ApplyLocalAccounts(string? preferredExternalId)
    {
        _isSelectingAccount = true;
        try
        {
            _accounts.Clear();
            foreach (var localAccount in _localAccountsByExternalId.Values
                         .OrderBy(account => account.AccountName, StringComparer.CurrentCultureIgnoreCase)
                         .ThenBy(account => account.ExternalAccountId, StringComparer.Ordinal))
            {
                _accounts.Add(new BankAccountOption(ToSourceAccount(localAccount), localAccount.Id));
            }

            AccountCombo.SelectedItem = _accounts.FirstOrDefault(item =>
                    string.Equals(item.Source.ExternalId, preferredExternalId, StringComparison.Ordinal))
                ?? _accounts.FirstOrDefault();
        }
        finally
        {
            _isSelectingAccount = false;
        }

        if (AccountCombo.SelectedItem is BankAccountOption selected)
        {
            UpdateAccountSummary(selected.Source);
            ShowLocalTransactions(selected);
        }
        else
        {
            ClearTransactions();
            ClearAccountSummary();
        }
    }

    private void ShowLocalTransactions(BankAccountOption account)
    {
        ClearTransactions();
        if (account.LocalAccountId is long localAccountId &&
            _localTransactionsByAccountId.TryGetValue(localAccountId, out var localTransactions))
        {
            foreach (var transaction in localTransactions)
            {
                AddTransaction(new BankTransactionPreviewRow(ToSourceTransaction(transaction, account.Source))
                {
                    CanImport = false,
                    ExistsLocally = true,
                    IsSelected = false,
                    ImportState = transaction.FixedCostTransactionId.HasValue
                        ? LocalizationManager.Get("BankStateFixedCost")
                        : LocalizationManager.Get("BankStateImported")
                });
            }
        }

        SetStatus(
            string.Format(LocalizationManager.Get("BankLocalTransactionsLoaded"), _transactions.Count),
            Brushes.SeaGreen);
    }

    private static bool IsSevDeskAccount(BankAccount account) =>
        string.Equals(account.SourceProvider, "sevdesk", StringComparison.OrdinalIgnoreCase);

    private static SevDeskCheckAccount ToSourceAccount(BankAccount account) => new()
    {
        ExternalId = account.ExternalAccountId.Trim(),
        Name = account.AccountName,
        IbanMasked = account.IbanMasked,
        Currency = NormalizeCurrency(account.Currency),
        Balance = account.Balance,
        LastSync = ParseStoredDate(account.LastSync),
        LastSyncRaw = account.LastSync
    };

    private static SevDeskCheckAccountTransaction ToSourceTransaction(
        BankTransaction transaction,
        SevDeskCheckAccount account)
    {
        var parsedStatus = int.TryParse(
            transaction.Status,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var status)
            ? status
            : (int?)null;

        return new SevDeskCheckAccountTransaction
        {
            ExternalId = transaction.SourceExternalId,
            AccountExternalId = account.ExternalId,
            EntryDate = ParseStoredDate(transaction.EntryDate),
            EntryDateRaw = transaction.EntryDate,
            ValueDate = ParseStoredDate(transaction.ValueDate),
            ValueDateRaw = transaction.ValueDate,
            Amount = transaction.Amount,
            HasValidAmount = !double.IsNaN(transaction.Amount) && !double.IsInfinity(transaction.Amount),
            Currency = NormalizeCurrency(transaction.Currency),
            PaymtPurpose = transaction.Purpose,
            PayeePayerName = transaction.Payee,
            Status = parsedStatus,
            StatusRaw = parsedStatus.HasValue ? "" : transaction.Status
        };
    }

    private static DateTimeOffset? ParseStoredDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                out var timestamp))
            return timestamp;

        return DateTime.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? new DateTimeOffset(date)
            : null;
    }

    private async Task LoadAccountsAsync(bool forceRefresh)
    {
        if (_isBusy || (!forceRefresh && _hasLoadedAccounts))
            return;

        if (!PermissionGuard.EnsureSessionValid("bank.accounts.load") || !App.CanView(PageKeys.Bank))
            return;

        var token = GetApiToken();
        if (token == null)
            return;

        var previousAccountId = (AccountCombo.SelectedItem as BankAccountOption)?.Source.ExternalId;

        try
        {
            SetBusy(true, LocalizationManager.Get("BankLoadingAccounts"));
            var accounts = await SevDeskClient.GetCheckAccountsAsync(token);

            if (!PermissionGuard.EnsureSessionValid("bank.accounts.load.completed") ||
                !App.CanView(PageKeys.Bank))
                return;

            _isSelectingAccount = true;
            try
            {
                _accounts.Clear();
                foreach (var source in accounts
                    .Where(item => !string.IsNullOrWhiteSpace(item.ExternalId))
                    .OrderByDescending(item => item.IsDefaultAccount)
                    .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    _localAccountsByExternalId.TryGetValue(source.ExternalId.Trim(), out var localAccount);
                    _accounts.Add(new BankAccountOption(source, localAccount?.Id));
                }

                AccountCombo.SelectedItem = _accounts.FirstOrDefault(item =>
                        string.Equals(item.Source.ExternalId, previousAccountId, StringComparison.Ordinal))
                    ?? _accounts.FirstOrDefault();
            }
            finally
            {
                _isSelectingAccount = false;
            }

            _hasLoadedAccounts = true;

            if (AccountCombo.SelectedItem is BankAccountOption selected)
            {
                UpdateAccountSummary(selected.Source);
                ShowLocalTransactions(selected);
            }
            else
            {
                ClearAccountSummary();
                SetStatus(LocalizationManager.Get("BankNoAccounts"), Brushes.DarkOrange);
            }
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException("bank.accounts_load_failed", ex);
            _hasLoadedAccounts = false;
            SetStatus(
                string.Format(LocalizationManager.Get("BankLoadFailed"), $"Referenz: {reference}"),
                Brushes.IndianRed);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadTransactionsAsync()
    {
        if (_isBusy)
            return;

        if (!PermissionGuard.EnsureSessionValid("bank.transactions.load") || !App.CanView(PageKeys.Bank))
            return;

        if (AccountCombo.SelectedItem is not BankAccountOption account)
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("BankSelectAccount"),
                LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        if (FromDatePicker.SelectedDate is not DateTime from || ToDatePicker.SelectedDate is not DateTime to)
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("BankDateRequired"),
                LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        if (from.Date > to.Date)
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("BankDateOrderInvalid"),
                LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        var token = GetApiToken();
        if (token == null)
            return;

        try
        {
            SetBusy(true, LocalizationManager.Get("BankLoadingTransactions"));
            var sourceTransactions = await SevDeskClient.GetCheckAccountTransactionsAsync(
                token,
                account.Source.ExternalId,
                DateOnly.FromDateTime(from),
                DateOnly.FromDateTime(to));

            if (!PermissionGuard.EnsureSessionValid("bank.transactions.load.completed") ||
                !App.CanView(PageKeys.Bank))
                return;

            if (AccountCombo.SelectedItem != account)
                return;

            var existingSourceIds = await Database.Instance.GetBankTransactionSourceIdsAsync(
                "sevdesk",
                account.Source.ExternalId);

            if (!PermissionGuard.EnsureSessionValid("bank.transactions.local_compare.completed") ||
                !App.CanView(PageKeys.Bank))
                return;

            if (AccountCombo.SelectedItem != account)
                return;

            var duplicateIds = sourceTransactions
                .Where(item => !string.IsNullOrWhiteSpace(item.ExternalId))
                .GroupBy(item => item.ExternalId.Trim(), StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);

            ClearTransactions();
            foreach (var source in sourceTransactions
                .OrderByDescending(item => item.ValueDate ?? item.EntryDate))
            {
                var sourceId = source.ExternalId.Trim();
                var exists = !string.IsNullOrWhiteSpace(sourceId) && existingSourceIds.Contains(sourceId);
                var validationState = GetValidationState(source, account.Source, duplicateIds);
                var canImport = validationState == null && App.CanEdit(PageKeys.Bank);

                AddTransaction(new BankTransactionPreviewRow(source)
                {
                    ExistsLocally = exists,
                    CanImport = canImport,
                    IsSelected = canImport && !exists,
                    ImportState = validationState
                        ?? (exists
                            ? LocalizationManager.Get("BankStateExisting")
                            : LocalizationManager.Get("BankStateNew"))
                });
            }

            var newCount = _transactions.Count(row => row.CanImport && !row.ExistsLocally);
            SetStatus(
                string.Format(
                    LocalizationManager.Get("BankTransactionsLoaded"),
                    _transactions.Count,
                    newCount),
                Brushes.SeaGreen);
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException("bank.transactions_load_failed", ex);
            SetStatus(
                string.Format(LocalizationManager.Get("BankLoadFailed"), $"Referenz: {reference}"),
                Brushes.IndianRed);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private string? GetApiToken()
    {
        if (App.IsDemoMode)
        {
            SetStatus(LocalizationManager.Get("BankDemoDisabled"), Brushes.DarkOrange);
            return null;
        }

        if (!PermissionGuard.EnsureSessionValid("bank.token.read") || !App.CanView(PageKeys.Bank))
            return null;

        var token = SevDeskSecureStore.LoadForCurrentDatabase()?.ApiToken?.Trim();
        if (!string.IsNullOrWhiteSpace(token))
            return token;

        var legacy = SevDeskSecureStore.Load();
        if (legacy != null
            && string.IsNullOrWhiteSpace(legacy.DatabaseInstanceId)
            && !string.IsNullOrWhiteSpace(legacy.ApiToken)
            && ModernMessageBox.ShowConfirm(
                LocalizationManager.Get("BankLegacyTokenConfirm"),
                LocalizationManager.Get("BankTitle")))
        {
            if (!PermissionGuard.DemandEdit(PageKeys.Integrations, "sevdesk.token.legacy_bind_from_bank"))
                return null;
            token = legacy.ApiToken.Trim();
            if (SevDeskSecureStore.SaveForCurrentDatabase(token))
                return token;

            SetStatus(LocalizationManager.Get("IntegrationsTokenSaveFailed"), Brushes.IndianRed);
            ModernMessageBox.ShowError(
                LocalizationManager.Get("IntegrationsTokenSaveFailed"),
                LocalizationManager.Get("AppErrorTitle"));
            return null;
        }

        SetStatus(LocalizationManager.Get("BankTokenMissing"), Brushes.DarkOrange);
        ModernMessageBox.ShowError(
            LocalizationManager.Get("BankTokenMissing"),
            LocalizationManager.Get("AppErrorTitle"));
        return null;
    }

    private static string? GetValidationState(
        SevDeskCheckAccountTransaction transaction,
        SevDeskCheckAccount account,
        IReadOnlySet<string> duplicateIds)
    {
        var sourceId = transaction.ExternalId.Trim();
        if (string.IsNullOrWhiteSpace(sourceId))
            return LocalizationManager.Get("BankStateMissingId");
        if (duplicateIds.Contains(sourceId))
            return LocalizationManager.Get("BankStateDuplicateId");
        if (!account.IsCurrencySupported)
            return string.Format(LocalizationManager.Get("BankStateUnsupportedCurrency"), account.CurrencyDisplay);
        if (transaction.Status == 300)
            return LocalizationManager.Get("BankStatePrivate");
        if (!transaction.HasValidAmount)
            return LocalizationManager.Get("BankStateInvalidAmount");
        if (!transaction.ValueDate.HasValue && !transaction.EntryDate.HasValue)
            return LocalizationManager.Get("BankStateMissingDate");
        return null;
    }

    private static BankTransaction ToBankTransaction(
        SevDeskCheckAccountTransaction source,
        SevDeskCheckAccount account)
    {
        var valueDate = source.ValueDate ?? source.EntryDate
            ?? throw new InvalidOperationException(LocalizationManager.Get("BankStateMissingDate"));
        var entryDate = source.EntryDate ?? valueDate;

        return new BankTransaction
        {
            SourceProvider = "sevdesk",
            SourceExternalId = source.ExternalId.Trim(),
            EntryDate = entryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ValueDate = valueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Amount = source.Amount,
            Currency = NormalizeCurrency(account.Currency),
            Purpose = FirstNonEmpty(source.PaymtPurpose, source.EntryText),
            Payee = source.PayeePayerName.Trim(),
            Status = source.Status?.ToString(CultureInfo.InvariantCulture) ?? source.StatusRaw.Trim(),
            IsSelected = true
        };
    }

    private void UpdateAccountSummary(SevDeskCheckAccount account)
    {
        var currency = NormalizeCurrency(account.Currency);
        BalanceText.Text = account.Balance.HasValue
            ? $"{account.Balance.Value:N2} {currency}"
            : "–";
        LastSyncText.Text = account.LastSync?.LocalDateTime.ToString("dd.MM.yyyy HH:mm")
            ?? (string.IsNullOrWhiteSpace(account.LastSyncRaw) ? "–" : account.LastSyncRaw);

        var maskedIban = account.IbanMasked;
        AccountHintText.Text = string.IsNullOrWhiteSpace(maskedIban)
            ? currency
            : $"{maskedIban} · {currency}";
    }

    private void ClearAccountSummary()
    {
        BalanceText.Text = "–";
        LastSyncText.Text = "–";
        AccountHintText.Text = "";
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _isBusy = busy;
        BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(message))
            BusyText.Text = message;
        ApplyPermissionState();
    }

    private void SetStatus(string text, Brush brush, bool isIdle = false)
    {
        _isIdleStatus = isIdle;
        StatusText.Text = text;
        StatusText.Foreground = brush;
    }

    private void AddTransaction(BankTransactionPreviewRow row)
    {
        row.PropertyChanged += BankTransactionRow_PropertyChanged;
        _transactions.Add(row);
    }

    private void ClearTransactions()
    {
        foreach (var row in _transactions)
            row.PropertyChanged -= BankTransactionRow_PropertyChanged;

        _transactions.Clear();
    }

    private void BankTransactionRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BankTransactionPreviewRow.IsSelected))
            ApplyPermissionState();
    }

    private static string NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim().ToUpperInvariant();

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private sealed class BankAccountOption
    {
        public BankAccountOption(SevDeskCheckAccount source, long? localAccountId = null)
        {
            Source = source;
            LocalAccountId = localAccountId;
            var name = string.IsNullOrWhiteSpace(source.Name)
                ? LocalizationManager.Get("BankUnnamedAccount")
                : source.Name.Trim();
            var maskedIban = source.IbanMasked;
            DisplayName = string.IsNullOrWhiteSpace(maskedIban)
                ? $"{name} · {NormalizeCurrency(source.Currency)}"
                : $"{name} · {maskedIban} · {NormalizeCurrency(source.Currency)}";
        }

        public SevDeskCheckAccount Source { get; }
        public long? LocalAccountId { get; set; }
        public string DisplayName { get; }
    }

    private sealed record LocalBankSnapshot(
        List<BankAccount> Accounts,
        List<BankTransaction> Transactions);
}

public sealed class BankTransactionPreviewRow : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _existsLocally;
    private string _importState = "";

    public BankTransactionPreviewRow(SevDeskCheckAccountTransaction source)
    {
        Source = source;
    }

    public SevDeskCheckAccountTransaction Source { get; }
    public bool CanImport { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value || (value && !CanImport)) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool ExistsLocally
    {
        get => _existsLocally;
        set
        {
            if (_existsLocally == value) return;
            _existsLocally = value;
            OnPropertyChanged();
        }
    }

    public string ImportState
    {
        get => _importState;
        set
        {
            if (_importState == value) return;
            _importState = value;
            OnPropertyChanged();
        }
    }

    public string ValueDateDisplay => Source.ValueDateDisplay;
    public string Payee => Source.PayeePayerName;
    public string Purpose => string.IsNullOrWhiteSpace(Source.PaymtPurpose) ? Source.EntryText : Source.PaymtPurpose;
    public double Amount => Source.Amount;
    public string SourceStatus => Source.Status switch
    {
        100 => LocalizationManager.Get("BankSevDeskStateCreated"),
        200 => LocalizationManager.Get("BankSevDeskStateAssigned"),
        300 => LocalizationManager.Get("BankSevDeskStatePrivate"),
        350 => LocalizationManager.Get("BankSevDeskStateAutoBooked"),
        400 => LocalizationManager.Get("BankSevDeskStateBooked"),
        _ when !string.IsNullOrWhiteSpace(Source.StatusRaw) => Source.StatusRaw,
        _ => LocalizationManager.Get("BankSevDeskStateUnknown")
    };

    public void RefreshLocalization() => OnPropertyChanged(nameof(SourceStatus));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

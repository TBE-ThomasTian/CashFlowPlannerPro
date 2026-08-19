using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Linq;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;
using CashFlowPlannerPro.ViewModels;
using Microsoft.Win32;

namespace CashFlowPlannerPro.Views;

public partial class InvoicesView : UserControl
{
    private readonly InvoicesViewModel _vm;

    public InvoicesView()
    {
        InitializeComponent();
        _vm = new InvoicesViewModel();
        DataContext = _vm;
        _vm.Load();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                _vm.Load();
        };

        AddBtn.ToolTip = TooltipService.Get("Btn_AddInvoice");
        EditBtn.ToolTip = "Ausgewaehlte Rechnung bearbeiten";
        DeleteBtn.ToolTip = TooltipService.Get("Btn_DeleteInvoice");
        ScanPdfBtn.ToolTip = TooltipService.Get("Btn_ScanPdf");
    }

    private void ScanPdf_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog {
            Filter = "PDF-Dateien (*.pdf)|*.pdf",
            Title = "Rechnung (PDF) scannen"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var scanned = InvoiceScanner.ScanPdf(dlg.FileName);
            var preview = new ScanPreviewDialog(scanned, dlg.FileName);
            if (preview.ShowDialog() == true && preview.ResultInvoice != null)
            {
                _vm.AddScannedInvoice(preview.ResultInvoice);
            }
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowError($"Fehler beim Scannen:\n{ex.Message}", "PDF Scan");
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        TryDeleteSelection();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        EditSelectedInvoice();
    }

    private void InvoicesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        EditSelectedInvoice();
    }

    private void EditSelectedInvoice()
    {
        if (_vm.SelectedInvoice == null)
        {
            ModernMessageBox.Show("Bitte waehle zuerst eine Rechnung aus.", "Rechnungen");
            return;
        }

        var editedInvoice = InvoiceEditDialog.ShowEdit(_vm.SelectedInvoice, _vm.CustomerNames);
        if (editedInvoice == null)
            return;

        try
        {
            _vm.ApplyInvoiceChanges(_vm.SelectedInvoice, editedInvoice);
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowError(
                $"Die Rechnung konnte nicht gespeichert werden:\n{ex.Message}",
                "Rechnung speichern");
        }
    }

    private void InvoicesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
            return;

        if (e.OriginalSource is TextBox or ComboBox)
            return;

        if (TryDeleteSelection())
            e.Handled = true;
    }

    private bool TryDeleteSelection()
    {
        var selectedInvoices = InvoicesGrid.SelectedItems.Cast<Invoice>().Distinct().ToList();
        if (selectedInvoices.Count == 0 && _vm.SelectedInvoice != null)
            selectedInvoices.Add(_vm.SelectedInvoice);

        if (selectedInvoices.Count == 0)
        {
            ModernMessageBox.Show("Bitte waehle zuerst mindestens eine Rechnung aus.", "Rechnungen");
            return false;
        }

        var message = selectedInvoices.Count == 1
            ? $"Soll die Rechnung fuer \"{selectedInvoices[0].Customer}\" wirklich geloescht werden?"
            : $"Sollen die {selectedInvoices.Count} ausgewaehlten Rechnungen wirklich geloescht werden?";

        if (!ModernMessageBox.ShowConfirm(message, "Rechnungen"))
            return false;

        _vm.DeleteInvoices(selectedInvoices);
        return true;
    }
}

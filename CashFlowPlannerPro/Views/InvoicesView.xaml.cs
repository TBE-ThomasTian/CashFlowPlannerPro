using System.Windows;
using System.Windows.Controls;
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

        AddBtn.ToolTip = TooltipService.Get("Btn_AddInvoice");
        DeleteBtn.ToolTip = TooltipService.Get("Btn_DeleteInvoice");
        ScanPdfBtn.ToolTip = TooltipService.Get("Btn_ScanPdf");
    }

    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is Invoice inv)
            Dispatcher.BeginInvoke(() => _vm.Save(inv));
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
}

using System.Windows;
using System.Windows.Controls;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;
using CashFlowPlannerPro.ViewModels;
using Microsoft.Win32;

namespace CashFlowPlannerPro.Views;

public partial class OffersView : UserControl
{
    private readonly OffersViewModel _vm;

    public OffersView()
    {
        InitializeComponent();
        _vm = new OffersViewModel();
        DataContext = _vm;
        _vm.Load();

        AddBtn.ToolTip = TooltipService.Get("Btn_AddOffer");
        DeleteBtn.ToolTip = TooltipService.Get("Btn_DeleteOffer");
        ScanPdfBtn.ToolTip = TooltipService.Get("Btn_ScanOfferPdf");
    }

    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is Offer o)
            Dispatcher.BeginInvoke(() => _vm.Save(o));
    }

    private void ScanPdf_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog {
            Filter = "PDF-Dateien (*.pdf)|*.pdf",
            Title = "Angebot (PDF) scannen"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var scanned = OfferScanner.ScanPdf(dlg.FileName);
            var preview = new OfferScanPreviewDialog(scanned, dlg.FileName);
            if (preview.ShowDialog() == true && preview.ResultOffer != null)
            {
                _vm.AddScannedOffer(preview.ResultOffer);
            }
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowError($"Fehler beim Scannen:\n{ex.Message}", "PDF Scan");
        }
    }
}

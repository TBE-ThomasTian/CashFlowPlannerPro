using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Linq;
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
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                _vm.Load();
        };

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

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        TryDeleteSelection();
    }

    private void OffersGrid_PreviewKeyDown(object sender, KeyEventArgs e)
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
        var selectedOffers = OffersGrid.SelectedItems.Cast<Offer>().Distinct().ToList();
        if (selectedOffers.Count == 0 && _vm.SelectedOffer != null)
            selectedOffers.Add(_vm.SelectedOffer);

        if (selectedOffers.Count == 0)
        {
            ModernMessageBox.Show("Bitte waehle zuerst mindestens ein Angebot aus.", "Angebote");
            return false;
        }

        var message = selectedOffers.Count == 1
            ? $"Soll \"{selectedOffers[0].OfferNumber}\" wirklich geloescht werden?"
            : $"Sollen die {selectedOffers.Count} ausgewaehlten Angebote wirklich geloescht werden?";

        if (!ModernMessageBox.ShowConfirm(message, "Angebote"))
            return false;

        _vm.DeleteOffers(selectedOffers);
        return true;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        var canEditOffers = App.CanEdit(PageKeys.Offers);
        AddBtn.IsEnabled = canEditOffers;
        DeleteBtn.IsEnabled = canEditOffers;
        ScanPdfBtn.IsEnabled = canEditOffers;
        CreateProjectBtn.IsEnabled = App.CanEdit(PageKeys.Offers) && App.CanEdit(PageKeys.Resources);
        if (!CreateProjectBtn.IsEnabled)
            CreateProjectBtn.ToolTip = "Nur mit Vollzugriff auf Angebote und Ressourcen verfügbar";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanEditOffers())
            return;

        EditOffer(_vm.CreateDraft());
    }

    private void OffersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source)
            return;

        if (FindVisualParent<Button>(source) != null)
            return;

        var row = FindVisualParent<DataGridRow>(source);
        if (row?.Item is not Offer offer)
            return;

        _vm.SelectedOffer = offer;
        if (!EnsureCanEditOffers())
            return;

        EditOffer(offer);
        e.Handled = true;
    }

    private void EditOffer(Offer offer)
    {
        var dialog = new OfferEditDialog(offer, _vm.CustomerNames)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultOffer == null)
            return;

        try
        {
            _vm.SaveEditedOffer(dialog.ResultOffer);
            if (_vm.SelectedOffer != null)
                OffersGrid.ScrollIntoView(_vm.SelectedOffer);
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowError($"Das Angebot konnte nicht gespeichert werden:\n{ex.Message}", "Angebot speichern");
        }
    }

    private static T? FindVisualParent<T>(DependencyObject source) where T : DependencyObject
    {
        DependencyObject? current = source;
        while (current != null)
        {
            if (current is T match)
                return match;
            current = current switch
            {
                Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(current)
            };
        }
        return null;
    }

    private static bool EnsureCanEditOffers()
    {
        if (App.CanEdit(PageKeys.Offers))
            return true;

        ModernMessageBox.ShowError("Zum Bearbeiten von Angeboten ist Vollzugriff erforderlich.", "Keine Berechtigung");
        return false;
    }

    private void ScanPdf_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanEditOffers())
            return;

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

    private void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        if (!App.CanEdit(PageKeys.Offers) || !App.CanEdit(PageKeys.Resources))
        {
            ModernMessageBox.ShowError(
                "Zum Erstellen eines Projekts ist Vollzugriff auf Angebote und Ressourcen erforderlich.",
                "Keine Berechtigung");
            return;
        }

        var offer = _vm.SelectedOffer;
        if (offer == null)
        {
            ModernMessageBox.Show(
                "Bitte wählen Sie zuerst das beauftragte Angebot aus, aus dem ein Projekt erstellt werden soll.",
                "Projekt erstellen");
            return;
        }

        if (offer.Id <= 0)
        {
            ModernMessageBox.ShowError(
                "Das ausgewählte Angebot wurde noch nicht gespeichert. Bitte speichern Sie es und versuchen Sie es erneut.",
                "Projekt erstellen");
            return;
        }

        if (offer.ProjectId is > 0)
        {
            ModernMessageBox.Show(
                $"Aus dem Angebot „{DisplayOfferNumber(offer)}“ wurde bereits ein Projekt erstellt.",
                "Projekt bereits vorhanden");
            return;
        }

        if (!string.Equals(offer.Status?.Trim(), "Beauftragt", StringComparison.Ordinal))
        {
            var currentStatus = string.IsNullOrWhiteSpace(offer.Status) ? "nicht festgelegt" : offer.Status;
            ModernMessageBox.Show(
                $"Das Angebot „{DisplayOfferNumber(offer)}“ hat den Status „{currentStatus}“.\n\n" +
                "Ein Projekt kann nur aus einem beauftragten Angebot erstellt werden. Setzen Sie den Status zuerst auf „Beauftragt“.",
                "Projekt erstellen");
            return;
        }

        var projectName = PromptForProjectName();
        if (projectName == null)
            return;

        var customer = string.IsNullOrWhiteSpace(offer.Customer) ? "ohne Kundenangabe" : offer.Customer.Trim();
        var confirmation =
            $"Aus dem Angebot „{DisplayOfferNumber(offer)}“ für „{customer}“ wird das Projekt „{projectName}“ erstellt.\n" +
            $"Das Angebotsvolumen von {offer.Amount:N2} € wird als Projektbudget übernommen.\n\n" +
            "Möchten Sie fortfahren?";

        if (!ModernMessageBox.ShowConfirm(confirmation, "Projekt erstellen"))
            return;

        try
        {
            var project = _vm.CreateProjectFromOffer(offer.Id, projectName);
            var projectNumber = string.IsNullOrWhiteSpace(project.ProjectNumber)
                ? "ohne Projektnummer"
                : project.ProjectNumber;
            var createdProjectName = string.IsNullOrWhiteSpace(project.Name)
                ? "Unbenanntes Projekt"
                : project.Name;

            ModernMessageBox.ShowSuccess(
                $"Das Projekt wurde erfolgreich erstellt.\n\nProjektnummer: {projectNumber}\nName: {createdProjectName}\n\n" +
                "Sie finden es unter Ressourcen.",
                "Projekt erstellt");
        }
        catch (InvalidOperationException ex)
        {
            ModernMessageBox.ShowError(ex.Message, "Projekt konnte nicht erstellt werden");
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowError(
                $"Beim Erstellen des Projekts ist ein unerwarteter Fehler aufgetreten:\n{ex.Message}",
                "Projekt konnte nicht erstellt werden");
        }
    }

    private static string DisplayOfferNumber(Offer offer) =>
        string.IsNullOrWhiteSpace(offer.OfferNumber) ? $"#{offer.Id}" : offer.OfferNumber.Trim();

    private static string? PromptForProjectName()
    {
        while (true)
        {
            var dialog = new InputDialog(
                "Projekt erstellen",
                "Bitte geben Sie einen kurzen Projektnamen ein:");

            if (dialog.ShowDialog() != true)
                return null;

            var projectName = dialog.ResultText.Trim();
            if (!string.IsNullOrWhiteSpace(projectName))
                return projectName;

            ModernMessageBox.ShowError(
                "Der Projektname darf nicht leer sein.",
                "Projekt erstellen");
        }
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
        if (!EnsureCanEditOffers())
            return false;

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

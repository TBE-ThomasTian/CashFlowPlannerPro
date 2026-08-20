using System;
using System.Collections.ObjectModel;
using System.Linq;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;
using CashFlowPlannerPro.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CashFlowPlannerPro.ViewModels;

public partial class OffersViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<Offer> offers = new();
    [ObservableProperty] private ObservableCollection<string> customerNames = new();
    [ObservableProperty] private Offer? selectedOffer;

    public bool CanEditOffers => App.CanEdit(PageKeys.Offers);

    public void Load(long? offerIdToSelect = null)
    {
        var selectedId = offerIdToSelect ?? SelectedOffer?.Id;
        ApplyLoadedData(Database.Instance.GetOffers(), Database.Instance.GetCustomers(), selectedId);
    }

    public async Task LoadAsync(long? offerIdToSelect = null, CancellationToken cancellationToken = default)
    {
        var selectedId = offerIdToSelect ?? SelectedOffer?.Id;
        var snapshot = await Database.Instance.GetOffersPageDataAsync(cancellationToken);
        ApplyLoadedData(snapshot.Offers, snapshot.Customers, selectedId);
    }

    private void ApplyLoadedData(
        IEnumerable<Offer> loadedOffers,
        IEnumerable<Customer> loadedCustomers,
        long? selectedId)
    {
        Offers = new ObservableCollection<Offer>(loadedOffers);
        CustomerNames = new ObservableCollection<string>(
            loadedCustomers
                .Select(c => c.DisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name));

        SelectedOffer = selectedId is > 0
            ? Offers.FirstOrDefault(offer => offer.Id == selectedId.Value)
            : null;
    }

    public Offer CreateDraft()
    {
        return new Offer {
            OfferNumber = Database.Instance.NextOfferNumber(),
            OfferDate = DateTime.Today.ToString("yyyy-MM-dd"),
            DateExpected = DateTime.Today.ToString("yyyy-MM-dd"),
            Customer = CustomerNames.FirstOrDefault() ?? "",
            Status = "Offen",
            Probability = 50,
            AmountBeforeDiscount = 0,
            DiscountPercent = 0,
            Amount = 0,
            PaymentDelay = 30
        };
    }

    public void SaveEditedOffer(Offer offer)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Offers, "offer.save")) return;
        if (offer.Id > 0)
            Database.Instance.UpdateOffer(offer);
        else
            Database.Instance.AddOffer(offer);

        Load(offer.Id);
    }

    [RelayCommand]
    private void Delete()
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Offers, "offer.delete")) return;
        if (SelectedOffer == null) return;
        Database.Instance.DeleteOffer(SelectedOffer.Id);
        Offers.Remove(SelectedOffer);
    }

    public void DeleteOffers(IEnumerable<Offer> offersToDelete)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Offers, "offer.delete_many")) return;

        var ids = offersToDelete
            .Where(o => o.Id > 0)
            .Select(o => o.Id)
            .Distinct()
            .ToHashSet();

        if (ids.Count == 0)
            return;

        foreach (var id in ids)
            Database.Instance.DeleteOffer(id);

        Offers = new ObservableCollection<Offer>(Offers.Where(o => !ids.Contains(o.Id)));
        SelectedOffer = Offers.FirstOrDefault();
    }

    [RelayCommand]
    private void SelectPdf()
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Offers, "offer.attach_pdf") || SelectedOffer == null) return;
        var dlg = new OpenFileDialog { Filter = "PDF Dateien (*.pdf)|*.pdf" };
        if (dlg.ShowDialog() == true) {
            SelectedOffer.PdfPath = dlg.FileName;
            Save(SelectedOffer);
            var idx = Offers.IndexOf(SelectedOffer);
            if (idx >= 0) Offers[idx] = SelectedOffer;
        }
    }

    [RelayCommand]
    private void OpenPdf()
    {
        if (!SafeDocumentLauncher.TryOpenLocalPdf(SelectedOffer?.PdfPath, out var error))
            ModernMessageBox.ShowError(error, LocalizationManager.Get("AppErrorTitle"));
    }

    public void Save(Offer o)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Offers, "offer.update")) return;
        if (o.Id > 0) Database.Instance.UpdateOffer(o);
    }

    public Project CreateProjectFromOffer(long offerId, string projectName)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Offers, "offer.create_project") ||
            !PermissionGuard.DemandEdit(PageKeys.Resources, "project.create_from_offer"))
            throw new UnauthorizedAccessException("Keine Berechtigung zum Erstellen eines Projekts aus diesem Angebot.");

        if (offerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(offerId), "Das Angebot wurde noch nicht gespeichert.");

        var project = Database.Instance.CreateProjectFromOffer(offerId, projectName);
        var sourceOffer = Offers.FirstOrDefault(o => o.Id == offerId);
        if (sourceOffer != null)
        {
            sourceOffer.ProjectId = project.Id;
            sourceOffer.ProjectNumber = project.ProjectNumber;
            var index = Offers.IndexOf(sourceOffer);
            if (index >= 0)
                Offers[index] = sourceOffer;
        }

        return project;
    }

    public void AddScannedOffer(Offer o)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Offers, "offer.scan_import")) return;
        Database.Instance.AddOffer(o);
        Load();
    }
}

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
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
        Offers = new ObservableCollection<Offer>(Database.Instance.GetOffers());
        CustomerNames = new ObservableCollection<string>(
            Database.Instance.GetCustomers()
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
        if (offer.Id > 0)
            Database.Instance.UpdateOffer(offer);
        else
            Database.Instance.AddOffer(offer);

        Load(offer.Id);
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedOffer == null) return;
        Database.Instance.DeleteOffer(SelectedOffer.Id);
        Offers.Remove(SelectedOffer);
    }

    public void DeleteOffers(IEnumerable<Offer> offersToDelete)
    {
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
        if (!CanEditOffers || SelectedOffer == null) return;
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
        if (SelectedOffer?.PdfPath == null || !File.Exists(SelectedOffer.PdfPath)) return;
        Process.Start(new ProcessStartInfo(SelectedOffer.PdfPath) { UseShellExecute = true });
    }

    public void Save(Offer o)
    {
        if (o.Id > 0) Database.Instance.UpdateOffer(o);
    }

    public Project CreateProjectFromOffer(long offerId, string projectName)
    {
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
        Database.Instance.AddOffer(o);
        Load();
    }
}

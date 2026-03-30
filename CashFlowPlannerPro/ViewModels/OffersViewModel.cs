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

    public void Load()
    {
        Offers = new ObservableCollection<Offer>(Database.Instance.GetOffers());
        CustomerNames = new ObservableCollection<string>(
            Database.Instance.GetCustomers()
                .Select(c => c.DisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name));
    }

    [RelayCommand]
    private void Add()
    {
        var o = new Offer {
            OfferNumber = Database.Instance.NextOfferNumber(),
            OfferDate = DateTime.Today.ToString("yyyy-MM-dd"),
            DateExpected = DateTime.Today.ToString("yyyy-MM-dd"),
            Customer = CustomerNames.FirstOrDefault() ?? "",
            Status = "Offen",
            Probability = 50,
            Amount = 0,
            PaymentDelay = 30
        };
        Database.Instance.AddOffer(o);
        Offers.Add(o);
        SelectedOffer = o;
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
        if (SelectedOffer == null) return;
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

    public void AddScannedOffer(Offer o)
    {
        Database.Instance.AddOffer(o);
        Load();
    }
}

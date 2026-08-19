using System.Windows;
using System.Windows.Input;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class CustomerEditDialog : Window
{
    public Customer Customer { get; private set; }
    public bool Saved { get; private set; }

    public CustomerEditDialog(Customer customer)
    {
        InitializeComponent();
        Customer = customer;
        LoadStatusCombo();
        LoadData();
        SaveBtn.ToolTip = TooltipService.Get("Btn_Save");
        CancelBtn.ToolTip = TooltipService.Get("Btn_Cancel");
    }

    private void LoadStatusCombo()
    {
        CbStatus.Items.Add("Aktiv");
        CbStatus.Items.Add("Inaktiv");
    }

    private void LoadData()
    {
        TbCompany.Text = Customer.Company;
        TbCustomerNumber.Text = Customer.CustomerNumber;
        TbContactName.Text = Customer.ContactName;
        TbEmail.Text = Customer.Email;
        TbPhone.Text = Customer.Phone;
        TbStreet.Text = Customer.Street;
        TbZipCode.Text = Customer.ZipCode;
        TbCity.Text = Customer.City;
        TbCountry.Text = Customer.Country;
        TbTaxId.Text = Customer.TaxId;
        TbNotes.Text = Customer.Notes;
        CbStatus.SelectedItem = Customer.Status;

        if (Customer.Id == 0) DialogTitle.Text = "Neuer Kunde";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TbCompany.Text) && string.IsNullOrWhiteSpace(TbContactName.Text))
        {
            ModernMessageBox.ShowError("Bitte geben Sie mindestens eine Firma oder einen Ansprechpartner ein.", "Pflichtfeld");
            return;
        }

        Customer.Company = TbCompany.Text.Trim();
        Customer.CustomerNumber = TbCustomerNumber.Text.Trim();
        Customer.ContactName = TbContactName.Text.Trim();
        Customer.Email = TbEmail.Text.Trim();
        Customer.Phone = TbPhone.Text.Trim();
        Customer.Street = TbStreet.Text.Trim();
        Customer.ZipCode = TbZipCode.Text.Trim();
        Customer.City = TbCity.Text.Trim();
        Customer.Country = TbCountry.Text.Trim();
        Customer.TaxId = TbTaxId.Text.Trim();
        Customer.Notes = TbNotes.Text.Trim();
        Customer.Status = CbStatus.SelectedItem as string ?? "Aktiv";

        if (Customer.Id == 0)
            Database.Instance.AddCustomer(Customer);
        else
            Database.Instance.UpdateCustomer(Customer);

        Saved = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    // --- Static API ---
    public static Customer? ShowEdit(Customer customer)
    {
        var dlg = new CustomerEditDialog(new Customer {
            Id = customer.Id, CustomerNumber = customer.CustomerNumber,
            Company = customer.Company, ContactName = customer.ContactName,
            Email = customer.Email, Phone = customer.Phone, Street = customer.Street,
            ZipCode = customer.ZipCode, City = customer.City, Country = customer.Country,
            TaxId = customer.TaxId, Status = customer.Status, Notes = customer.Notes
        });
        dlg.Owner = Application.Current.MainWindow?.IsVisible == true ? Application.Current.MainWindow : null;
        dlg.ShowDialog();
        return dlg.Saved ? dlg.Customer : null;
    }

    public static Customer? ShowNew()
    {
        return ShowEdit(new Customer());
    }
}

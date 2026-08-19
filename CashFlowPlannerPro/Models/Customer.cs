namespace CashFlowPlannerPro.Models;

public class Customer
{
    public long Id { get; set; }
    public string CustomerNumber { get; set; } = "";
    public string Company { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Street { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "Deutschland";
    public string TaxId { get; set; } = "";
    public string Status { get; set; } = "Aktiv";
    public string Notes { get; set; } = "";
    public string? CreatedAt { get; set; }

    // Display helper
    public string DisplayName => string.IsNullOrWhiteSpace(Company) ? ContactName : Company;
}

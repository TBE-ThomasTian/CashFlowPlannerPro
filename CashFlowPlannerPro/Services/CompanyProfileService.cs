using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;

namespace CashFlowPlannerPro.Services;

public static class CompanyProfileService
{
    public static CompanyProfile Load()
    {
        return new CompanyProfile
        {
            CompanyName = Database.Instance.GetSetting("company_name") ?? "",
            AddressLine1 = Database.Instance.GetSetting("company_address_1") ?? "",
            AddressLine2 = Database.Instance.GetSetting("company_address_2") ?? "",
            ContactEmail = Database.Instance.GetSetting("company_email") ?? "",
            ContactPhone = Database.Instance.GetSetting("company_phone") ?? "",
            Website = Database.Instance.GetSetting("company_website") ?? "",
            TaxId = Database.Instance.GetSetting("company_tax_id") ?? "",
            LogoBase64 = Database.Instance.GetSetting("company_logo") ?? ""
        };
    }

    public static void Save(CompanyProfile profile)
    {
        Database.Instance.SaveSetting("company_name", profile.CompanyName);
        Database.Instance.SaveSetting("company_address_1", profile.AddressLine1);
        Database.Instance.SaveSetting("company_address_2", profile.AddressLine2);
        Database.Instance.SaveSetting("company_email", profile.ContactEmail);
        Database.Instance.SaveSetting("company_phone", profile.ContactPhone);
        Database.Instance.SaveSetting("company_website", profile.Website);
        Database.Instance.SaveSetting("company_tax_id", profile.TaxId);
        Database.Instance.SaveSetting("company_logo", profile.LogoBase64);
    }
}

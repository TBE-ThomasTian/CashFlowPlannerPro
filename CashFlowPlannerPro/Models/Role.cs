namespace CashFlowPlannerPro.Models;

public class Role
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

public class RolePermission
{
    public long Id { get; set; }
    public long RoleId { get; set; }
    public string PageKey { get; set; } = "";
    public string AccessLevel { get; set; } = "none"; // none, read, full
}

public static class PageKeys
{
    public const string Dashboard = "dashboard";
    public const string Transactions = "transactions";
    public const string Fixkosten = "fixkosten";
    public const string Taxes = "taxes";
    public const string Invoices = "invoices";
    public const string Offers = "offers";
    public const string Resources = "resources";
    public const string Targets = "targets";
    public const string Todos = "todos";
    public const string TimeTracking = "timetracking";
    public const string Kunden = "kunden";
    public const string Integrations = "integrations";
    public const string Admin = "admin";

    public static readonly (string key, string label)[] All = [
        (Dashboard, "📊 Übersicht"),
        (Transactions, "💰 Ein/Ausgaben"),
        (Fixkosten, "📋 Fixkosten"),
        (Taxes, "🏛 Steuer"),
        (Invoices, "📄 Rechnungen"),
        (Offers, "📝 Angebote"),
        (Resources, "👥 Ressourcen"),
        (Targets, "🎯 Ziele"),
        (Todos, "✅ Aufgaben"),
        (TimeTracking, "⏱ Zeiterfassung"),
        (Kunden, "📇 Adressbuch"),
        (Integrations, "🔗 Integrationen"),
        (Admin, "🔐 Benutzerverwaltung")
    ];
}

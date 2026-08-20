using System.Collections.Generic;

namespace CashFlowPlannerPro.Services;

public static class TooltipService
{
    private static readonly Dictionary<string, (string De, string En)> Tooltips = new()
    {
        // === MainWindow Sidebar ===
        ["Nav_Dashboard"] = ("Übersicht anzeigen", "Show overview"),
        ["Nav_Transactions"] = ("Ein- und Ausgaben verwalten", "Manage income and expenses"),
        ["Nav_Bank"] = ("sevDesk-Zahlungskonten und Kontobewegungen anzeigen", "Show sevDesk payment accounts and transactions"),
        ["Nav_Fixkosten"] = ("Wiederkehrende Kosten verwalten", "Manage recurring costs"),
        ["Nav_Taxes"] = ("Steuerübersicht anzeigen", "Show tax overview"),
        ["Nav_Invoices"] = ("Rechnungen verwalten", "Manage invoices"),
        ["Nav_Offers"] = ("Angebote verwalten", "Manage offers"),
        ["Nav_Resources"] = ("Mitarbeiter und Ressourcen planen", "Plan employees and resources"),
        ["Nav_Targets"] = ("Umsatzziele festlegen", "Set revenue targets"),
        ["Nav_Todos"] = ("Aufgaben verwalten", "Manage tasks"),
        ["Nav_TimeTracking"] = ("Arbeitszeiten erfassen", "Track working hours"),
        ["Nav_Customers"] = ("Kunden und Kontakte verwalten", "Manage customers and contacts"),
        ["Nav_Integrations"] = ("Add-ons und Drittanbieter anbinden", "Connect add-ons and third-party services"),
        ["Nav_Admin"] = ("Benutzer und Rollen verwalten", "Manage users and roles"),
        ["Nav_Settings"] = ("Programmeinstellungen öffnen", "Open application settings"),
        ["Nav_Profile"] = ("Profil und Passwort bearbeiten", "Edit profile and password"),
        ["Nav_SwitchDb"] = ("Datenbank wechseln oder neu verbinden", "Switch or reconnect database"),
        ["Nav_About"] = ("Informationen über die Anwendung", "About this application"),
        ["Nav_Exit"] = ("Anwendung beenden", "Exit application"),

        // === Common Actions ===
        ["Btn_Add"] = ("Neuen Eintrag hinzufügen", "Add new entry"),
        ["Btn_Delete"] = ("Ausgewählten Eintrag löschen", "Delete selected entry"),
        ["Btn_Save"] = ("Änderungen speichern", "Save changes"),
        ["Btn_Cancel"] = ("Abbrechen und schließen", "Cancel and close"),
        ["Btn_Close"] = ("Fenster schließen", "Close window"),
        ["Btn_OK"] = ("Bestätigen", "Confirm"),
        ["Btn_Refresh"] = ("Daten aktualisieren", "Refresh data"),

        // === Dashboard ===
        ["Btn_SaveBalance"] = ("Aktuellen Kontostand speichern", "Save current account balance"),
        ["Btn_PreviewDashboardPdf"] = ("PDF-Report zuerst in der Vorschau ansehen", "Preview the PDF report first"),
        ["Btn_ExportDashboardPdf"] = ("PDF-Monatsreport exportieren", "Export monthly PDF report"),

        // === Transactions ===
        ["Btn_AddTransaction"] = ("Neue Buchung hinzufügen", "Add new transaction"),
        ["Btn_DeleteTransaction"] = ("Ausgewählte Buchung löschen", "Delete selected transaction"),

        // === Fixkosten ===
        ["Btn_AddFixkosten"] = ("Neue Fixkosten hinzufügen", "Add new fixed cost"),
        ["Btn_DeleteFixkosten"] = ("Ausgewählte Fixkosten löschen", "Delete selected fixed cost"),

        // === Taxes ===
        ["Btn_AddTax"] = ("Neuen Steuereintrag hinzufügen", "Add new tax entry"),
        ["Btn_DeleteTax"] = ("Ausgewählten Steuereintrag löschen", "Delete selected tax entry"),

        // === Invoices ===
        ["Btn_AddInvoice"] = ("Neue Rechnung erstellen", "Create new invoice"),
        ["Btn_DeleteInvoice"] = ("Ausgewählte Rechnung löschen", "Delete selected invoice"),
        ["Btn_ScanPdf"] = ("PDF-Datei scannen und importieren", "Scan and import PDF file"),
        ["Btn_AttachPdf"] = ("PDF-Datei anhängen", "Attach PDF file"),
        ["Btn_OpenPdf"] = ("Angehängtes PDF öffnen", "Open attached PDF"),

        // === Offers ===
        ["Btn_AddOffer"] = ("Neues Angebot erstellen", "Create new offer"),
        ["Btn_DeleteOffer"] = ("Ausgewähltes Angebot löschen", "Delete selected offer"),
        ["Btn_ScanOfferPdf"] = ("Angebots-PDF scannen und importieren", "Scan and import offer PDF"),
        ["Btn_AcceptOffer"] = ("Gescanntes PDF als Angebot übernehmen", "Accept scanned PDF as offer"),
        ["Btn_AcceptInvoice"] = ("Gescanntes PDF als Rechnung übernehmen", "Accept scanned PDF as invoice"),

        // === Resources ===
        ["Btn_PrevPeriod"] = ("Vorherigen Zeitraum anzeigen", "Show previous period"),
        ["Btn_Today"] = ("Zum heutigen Datum springen", "Jump to today"),
        ["Btn_NextPeriod"] = ("Nächsten Zeitraum anzeigen", "Show next period"),
        ["Btn_AddResource"] = ("Neuen Mitarbeiter hinzufügen", "Add new employee"),
        ["Btn_AddProject"] = ("Neues Projekt erstellen", "Create new project"),
        ["Btn_AddHardware"] = ("Neue Hardware hinzufügen", "Add new hardware"),
        ["Btn_Kanban"] = ("Kanban-Übersicht der Projekte öffnen", "Open project Kanban board"),

        // === Targets ===
        ["Btn_AddTarget"] = ("Neues Umsatzziel hinzufügen", "Add new revenue target"),
        ["Btn_DeleteTarget"] = ("Ausgewähltes Ziel löschen", "Delete selected target"),

        // === Todos ===
        ["Btn_AddTodo"] = ("Neue Aufgabe erstellen", "Create new task"),

        // === Time Tracking ===
        ["Btn_StartTimer"] = ("Zeiterfassung starten", "Start time tracking"),
        ["Btn_StopTimer"] = ("Zeiterfassung stoppen", "Stop time tracking"),

        // === Customers ===
        ["Btn_AddCustomer"] = ("Neuen Kunden anlegen", "Create new customer"),
        ["Btn_DeleteCustomer"] = ("Ausgewählten Kunden löschen", "Delete selected customer"),

        // === Admin ===
        ["Btn_AddUser"] = ("Neuen Benutzer anlegen", "Create new user"),
        ["Btn_AddRole"] = ("Neue Rolle erstellen", "Create new role"),

        // === PDF Preview ===
        ["Btn_PrevPage"] = ("Vorherige Seite", "Previous page"),
        ["Btn_NextPage"] = ("Nächste Seite", "Next page"),
        ["Btn_ZoomIn"] = ("Hineinzoomen", "Zoom in"),
        ["Btn_ZoomOut"] = ("Herauszoomen", "Zoom out"),

        // === Kanban ===
        ["Btn_AddMilestone"] = ("Neuen Meilenstein erstellen", "Create new milestone"),

        // === Login ===
        ["Btn_OpenDb"] = ("Bestehende Datenbank öffnen", "Open existing database"),
        ["Btn_NewDb"] = ("Neue Datenbank erstellen", "Create new database"),
        ["Btn_TestConnection"] = ("Verbindung zum Server testen", "Test server connection"),
        ["Btn_ImportLocal"] = ("Lokale SQLite-Datenbank importieren", "Import local SQLite database"),
        ["Btn_ImportServer"] = ("Daten vom Server importieren", "Import data from server"),
        ["Btn_Login"] = ("Anmelden", "Log in"),

        // === User Settings ===
        ["Btn_ChangeAvatar"] = ("Profilbild ändern", "Change profile picture"),
        ["Btn_RemoveAvatar"] = ("Profilbild entfernen", "Remove profile picture"),

        // === Dialogs ===
        ["Btn_Create"] = ("Neuen Eintrag erstellen", "Create new entry"),
    };

    public static string Get(string key)
    {
        if (!Tooltips.TryGetValue(key, out var tip)) return key;
        return LocalizationManager.CurrentLanguageCode == "de" ? tip.De : tip.En;
    }
}

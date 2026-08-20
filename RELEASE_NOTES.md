# CashFlow Planner Pro 2.3.0

## Banking und Fixkosten

- Neues Menü **Bank** für die in sevDesk synchronisierten Bankkonten und Kontobewegungen.
- Anzeige des von sevDesk gemeldeten Kontostands, des letzten Synchronisierungszeitpunkts und der importierbaren Buchungen.
- Kontobewegungen werden wiederholbar und ohne Duplikate in eigenen Banktabellen gespeichert; manuelle Ein- und Ausgaben bleiben davon getrennt.
- Gespeicherte Bankkonten und Kontobewegungen werden nach einem Neustart sofort aus der Datenbank angezeigt – auch ohne erneuten API-Abruf.
- Aus einer negativen EUR-Kontobuchung kann direkt eine verknüpfte Fixkostenbuchung erstellt werden.
- Neu erzeugte Bank-Fixkosten erscheinen beim Wechsel in die Fixkostenansicht sofort; die Standardkategorien enthalten nun auch **Telefon**, **Internet** und **Fahrkosten**.
- Bank- und sevDesk-Zugangsdaten sind lokal verschlüsselt und an die jeweilige MariaDB-Datenbank gebunden.

## Planung, Angebote und Bedienung

- Ressourceneinsätze lassen sich beim Ziehen über die sichtbare Monatsgrenze hinaus verlängern oder verkürzen; verdeckte Fortsetzungen und individuelle Stunden bleiben erhalten.
- Das Laden der Angebotsansicht wurde von vielen Einzelabfragen auf gebündelte Datenbankabfragen umgestellt und erhält eine sichtbare Ladeanzeige.
- Wiederkehrende Buchungen berücksichtigen nun auch tägliche Intervalle, alte Serien, Monatsenden und Schaltjahre korrekt.
- Angebotsnummern werden auch bei parallelen MariaDB-Clients eindeutig und numerisch korrekt vergeben.
- Kundenverknüpfungen bei Angeboten und Rechnungen bleiben über stabile Kunden-IDs konsistent.
- Verbesserte Berechtigungszustände verhindern Bearbeiten, Drag-and-drop und Kontextaktionen in schreibgeschützten Ansichten.

## Sicherheit und Zuverlässigkeit

- Laufende Sitzungen werden nach Passwort-, Rollen-, Rechte- oder Kontostatusänderungen sicher widerrufen.
- Passwortregeln, Anmelde-Drosselung, Schutz des integrierten Administrators und atomare Passwortwechsel wurden vereinheitlicht.
- Datenbankwechsel und Schema-Upgrades prüfen Identität und Berechtigung unmittelbar vor kritischen Schritten und beenden ungültige Sitzungen ausfallsicher.
- Technische Fehler werden mit Referenz protokolliert; Zugangsdaten, API-Tokens und sensible Bankinformationen werden aus Protokollen und Fehlermeldungen entfernt.
- Native MariaDB-Datums- und Zeitstempelspalten werden typfest gelesen; dadurch stürzt die Rechnungsansicht bei `DATETIME`-Werten nicht mehr ab.
- Der automatische Updater prüft HTTPS-Quelle, Paketmanifest, SHA-256-Hashes, Authenticode-Signatur und eine getrennte CMS-Signatur, bevor Dateien ersetzt werden.
- Das Release-Verfahren validiert Versionen, Paketinhalt und Schwachstellen und veröffentlicht nur vollständig signierte Installer- und ZIP-Artefakte.

## Voraussetzungen und Hinweise

- Windows 10 Version 2004 (Build 19041) oder neuer, 64 Bit.
- Setup und ZIP sind selbstenthaltend; eine separate .NET-Installation ist nicht erforderlich.
- Die Anwendung verwendet ausschließlich MariaDB. Lokale SQLite-Dateien werden weder geöffnet noch verändert; benötigte Altdaten müssen vor dem Upgrade mit einer älteren Version nach MariaDB übertragen werden.
- MariaDB-Backups und Wiederherstellungen werden durch den Serveranbieter bzw. die Datenbankadministration durchgeführt.
- Bankdaten entsprechen dem letzten Stand, den sevDesk von der Bank synchronisiert hat; es handelt sich nicht zwingend um einen Echtzeitabruf direkt bei der Bank.
- Der sevDesk-Import unterstützt derzeit ausschließlich EUR-Belege und EUR-Bankbuchungen.

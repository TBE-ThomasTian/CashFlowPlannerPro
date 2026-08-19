# CashFlow Planner Pro 2.1.0

## Neue Funktionen

- Erweiterter sevDesk-Import für Kunden, Rechnungen und Angebote – einschließlich Kundennummern, Vor- und Nachtexten, internen Notizen sowie vollständigen Positionen mit Menge, Einheit, Preis, Rabatt und Steuer.
- Sichere Wiederholungsimporte: Lokale Änderungen und bewusst gelöschte Positionen bleiben erhalten; mehrdeutige Zuordnungen werden als Konflikt übersprungen.
- Angebote und Rechnungen lassen sich per Doppelklick bearbeiten. Dokumentinhalte werden strukturiert statt in einem einzigen Beschreibungstext gepflegt.
- Rabatte werden bei Angeboten und Projekten als Prozent- oder Eurobetrag unterstützt.
- Aus einem beauftragten Angebot kann direkt ein Projekt mit frei wählbarem Projektnamen erstellt werden; die Projektnummer erscheint anschließend beim Angebot.
- Neue Benutzer erhalten automatisch eine zugeordnete Mitarbeiterressource.

## Bedienung und Darstellung

- Eigenständige Menüs **Einstellungen** und **Mein Profil**.
- Darstellungsgröße mit 90, 100, 115 und 130 Prozent.
- Deutlichere orange Markierung der ausgewählten Tabellenzeile.
- Fortschrittsanzeige beim sevDesk-Import.
- Verbesserte Dialoge, Tabellen, ComboBoxen und scrollbare Meldungen für kleinere Bildschirme und größere Darstellungsstufen.

## Sicherheit und Datenbank

- MariaDB-Verbindungen verwenden TLS mit vollständiger Zertifikats- und Hostnamenprüfung.
- Das Datenbankschema wurde für strukturierte Dokumentinhalte, Kundennummern, Rabatte, Projektverknüpfungen und Benutzerressourcen erweitert.
- Datenübertragungen zwischen SQLite und MariaDB erfolgen transaktional und werden bei Fehlern vollständig zurückgerollt.
- Die eingebettete SQLite-Bibliothek wurde auf eine nicht von GHSA-2m69-gcr7-jv3q betroffene Version aktualisiert.

## Voraussetzungen und Hinweise

- Umstellung auf .NET 10 und Windows 10 Version 2004 (Build 19041) oder neuer.
- Setup und ZIP sind selbstenthaltende Windows-x64-Pakete; eine separate .NET-Installation ist nicht erforderlich.
- Bestehende SQLite- und MariaDB-Datenbanken werden beim Öffnen automatisch erweitert. Vor dem ersten Start wird dennoch ein Backup empfohlen.
- Bestehende MariaDB-Server müssen ein vertrauenswürdiges, zum Hostnamen passendes TLS-Zertifikat bereitstellen.
- Der sevDesk-Import unterstützt derzeit ausschließlich EUR-Belege.

# Changelog

Frühere Einträge werden nicht verändert; neue Releases werden unten ergänzt.

## 1.8.5 – 27.09.2026

- Zuletzt verwendete Vorlage, Drucker und Format pro Arbeitsplatz gespeichert.
- BrainOffice-Paste nach Vorlagenwechsel erneut ermöglicht und um einen
  sitzungsinternen Fallback ergänzt.
- Toolbar auf drei Zeilen verdichtet und störende große Dauerlisten entfernt.
- Kompakte, scrollbar Vorlagenlisten für schnellen Zugriff ergänzt.
- Eindeutige Vorlagennamen sowie Sammelimport und -export abgesichert.
- `228` synthetische Tests, visuelle Prüfung und Starttest bestanden.

## 1.8.6 – 27.09.2026

- Dynamische Platzhalter `%Date%`, `%DateTime%` und `%Time%` ergänzt.
- Einheitliche lokale Ausgabe `dd.MM.yyyy`, `dd.MM.yyyy HH:mmh` und `HH:mmh`.
- `229` synthetische Tests und direkter Starttest der BrainOffice-EXE bestanden.

## 1.8.7 – 27.09.2026

- `Speichern als…` für unabhängige Kopien vorhandener Vorlagen ergänzt.
- Vorhandene Vorlagennamen bleiben eindeutig und werden nicht still überschrieben.
- Sammel-Export und -Import aller Vorlagen als ein ZIP-Paket ergänzt.
- Frühere JSON-Sicherungen bleiben importierbar.
- `232` synthetische Tests, visuelle Prüfung und direkter Starttest bestanden.

## 1.8.8 – 28.09.2026

- Pauschale Sperre beim Speichern unter einem vorhandenen Vorlagennamen entfernt.
- Vorhandene Vorlage kann nach genau einer Warnung bewusst überschrieben oder
  unverändert gelassen werden.
- Bereits eingesetzte Patientendaten werden in derselben Warnung ausgewiesen.
- `232` synthetische Tests, visuelle Prüfung und direkter Starttest bestanden.

## 1.9.0 – 28.09.2026

- BrainOffice-Vertrag `label-template-launch-v1` vollständig ergänzt.
- Lokalen Vorlagenkatalog mit stabilen IDs und Verfügbarkeitsstatus ergänzt.
- Exaktes Öffnen einer angeforderten Vorlage samt Drucker und Format ergänzt.
- Laufende Instanz verarbeitet Schnellaufträge über benutzergebundene Named Pipe.
- Automatischer Druck aus Schnellwahlen strikt abgelehnt.
- Standalone-Start und sämtliche bisherigen manuellen Funktionen beibehalten.
- Vorlagen in updatesicheren Benutzerprofil-Speicher migriert und automatische rotierende Sicherungen ergänzt.
- `237` synthetische Tests, visuelle Prüfung, Standalone-, Katalog-, Erststart- und laufende-Instanz-Tests bestanden.

### Buildkorrektur 28.09.2026 16:04:49

- Die ältere `--patient-json`-Übergabe behält die aktive gespeicherte Vorlage
  nach dem Einsetzen bei und beginnt beim nächsten Patienten wieder mit einer
  frischen Platzhalterbasis.
- Eigenständiger Doppelklick-Start und BrainOffice-Schnellstart erneut getrennt
  ohne Druckauftrag geprüft.
- `239` synthetische Tests bestanden.

## 1.9.1 – 29.09.2026

- Direkte SUPVAN-USB-Erkennung dynamisch und sicher wie BrainOffice Bridge
  `0.3.20` ergänzt.
- Keine fest codierte PID-Liste und keine beliebige HID-Wildcard mehr; akzeptiert
  werden nur VID `0x1820`, Interface `MI_00`, Reportlänge `65/65` sowie
  Hersteller leer oder exakt `SUPVAN`.
- Technische Geräte-ID, Anzeigename `SUPVAN <Nummer> (PID <hex>)`, VID, PID,
  optionale Seriennummer, Hersteller und Produktname ergänzt.
- Eindeutige Geräteauswahl bei mehreren SUPVAN-Geräten ergänzt; kein stiller
  Fallback auf ein anderes konfiguriertes Gerät.
- Lokaler Alias pro Gerät und Persistenz pro Windows-Arbeitsplatz und
  Druckprofil ergänzt.
- Seriennummer wird als stabile Identität verwendet; ohne Seriennummer bleibt
  der Windows-Gerätepfad verbindlich und Portwechsel werden klar gemeldet.
- `Geräte neu suchen` und synthetischer patientenfreier SUPVAN-Test ergänzt.
- Bestehende Windows-, Netzwerk-, TSPL- und ZPL-Druckerpfade sowie
  BrainOffice-No-Autoprint-Vertrag unverändert belassen.
- `253` synthetische Tests und direkter Starttest der BrainOffice-EXE bestanden.

## 1.9.2 – 29.09.2026

- Eigenständigen direkten SUPVAN-T50M-Pro-Treiber in den Etikettenmaster
  integriert; keine Abhängigkeit von BrainOffice Bridge, localhost-HTTP oder
  Windows-Druckerwarteschlange.
- SUPVAN-Geräte werden über `HidSharp` direkt als HID-Geräte gesucht; eigene
  Schaltfläche `SUPVAN-Drucker suchen` ergänzt.
- Echten USB/HID-Druckpfad aus der verifizierten Referenz übernommen:
  48-mm-Druckkopf, Dot-Spiegelung, 4096-Byte-Frames, LZMA-Kompression,
  HID-Schreiben/-Lesen und Statusfehler für Deckel, Rolle leer oder falsch
  eingelegte Rolle.
- Mehrere SUPVAN-Geräte können getrennt benannt und pro Druckprofil mit eigener
  Geräte-ID sowie eigenem Etikettenformat gespeichert werden.
- SUPVAN-Standardformate `40 × 30`, `50 × 30` und `50 × 80 · 90°` ergänzt.
- `Testetikett drucken` druckt patientenfreien synthetischen Text direkt am
  ausgewählten SUPVAN.
- `256` synthetische Tests, SUPVAN-Hardwaretest mit echtem Testetikett und
  direkter Starttest der BrainOffice-EXE bestanden.

## 1.9.3 – 29.09.2026

- Optionale zentrale Etikettenvorlagenverwaltung in den Einstellungen ergänzt.
- Beliebiger gemeinsamer JSON-Pfad, zum Beispiel auf einem NAS, kann als
  zentraler Vorlagenspeicher eingetragen oder neu angelegt werden.
- Lokale und zentrale Vorlagen werden gemeinsam in den Vorlagenlisten angezeigt
  und jeweils als `Lokal` oder `Zentral` gekennzeichnet.
- Neue Vorlagen, Kopien und Importe werden in das gewählte Ziel `Lokal` oder
  `Zentral` gespeichert; vorhandene zentrale Vorlagen werden nicht still in den
  lokalen Store verschoben.
- Löschen und Überschreiben betrifft nur den Speicherort der gewählten Vorlage;
  gleichnamige lokale und zentrale Vorlagen dürfen parallel existieren.
- BrainOffice-Katalogaufrufe lesen lokale und zentrale Vorlagen zusammen ein,
  drucken aber weiterhin niemals automatisch.
- `258` synthetische Tests und Release-Publish des BrainOffice-Builds bestanden.

## 1.9.4 – 02.10.2026

- SUPVAN-Profile werden in den Einstellungen konsequent als `SUPVAN-USB direkt`
  behandelt und nicht mehr als Windows-Drucker/Freigabe validiert.
- Die irreführende Auswahl `Windows-Grafik · DYMO / Brother / SUPVAN` wurde zu
  `Windows-Grafik · DYMO / Brother` korrigiert; SUPVAN hat den eigenen direkten
  USB/HID-Druckweg.
- Auswahl eines SUPVAN-Geräts schaltet automatisch auf den direkten USB-Modus.
- Regressionstest ergänzt: leerer Windows-Druckername blockiert ein direktes
  SUPVAN-USB-Profil nicht mehr.
- `258` synthetische Tests bestanden.

## 1.9.5 – 24.10.2026

- Werkzeugleiste mit eigenen Vorlagenaktionen, Ausrichtungsicons und A-/A+-Reglern überarbeitet.
- Aktive Vorlage und gespeicherter Standarddrucker werden sichtbar angezeigt.
- Speichern aktualisiert die aktive Vorlage ohne erneuten Namensdialog; Speichern als legt eine Kopie an.
- Vorlagenliste auf breiten Fenstern, kompakte Vorlagenleiste mit aufklappbarer Liste auf schmalen Fenstern.
- Zeilenhöhe übernimmt Dropdown-Auswahl erst beim Schließen; freie Eingabe mit Enter bleibt möglich.
- 266 synthetische Tests, visuelle Prüfung bei 1040 und 1660 Pixeln und Starttests beider EXEs bestanden.
- Lokale Installation E:\AI\APPS\TSC-Etikettendruck.exe aktualisiert; vorherige EXE gesichert.

## 1.9.6 – 25.10.2026

- Vorlagenliste auf zehn vollständig sichtbare Zeilen vergrößert; rechte Werkzeuge darunter scrollbar angeordnet.
- Überlauf blockiert manuellen Druck nicht mehr. Verständlicher Hinweis erklärt Zuschnitt und Layoutprüfung.
- Druckgrafik verwendet den fortlaufenden Editor-Ausschnitt statt WPF-Seitenumbrüchen.
- Nichtdruckbare Zeichen über ¶ zuschaltbar; weder Vorlagentext noch Druckgrafik werden verändert.
- 271 Tests sowie visuelle Prüfung und Starttests bestanden; kein Hardwaredruck.
- Installierte APPS-EXE ersetzt und hashgeprüft; Taskleistenverknüpfung verweist bereits darauf.

## 1.9.7 – 25.10.2026

- Innenrand im fortlaufenden Druckviewport korrigiert.
- SUPVAN berücksichtigt Inversdruck und Schwellwert.
- 279 Tests bestanden; 7-mm-Rasterversatz sowie entpackte LZMA-Frames einschließlich inverser Randflächen geprüft.
- Beide fertigen EXEs gestartet und regulär beendet. Physischer Ausdruck noch nicht erneut geprüft.

## 1.9.8 – 26.10.2026

- Fehler mit der synthetischen freien Vorlage reproduziert: Die Druckansicht schnitt „Praxis Codex Test“ rechts ab, während der Editor korrekt umbrach.
- Textansicht vor Anwendung der Dokumentgeometrie initialisiert; Druckraster entspricht nun dem Editorlayout.
- 281 Tests bestanden, neuer Rastervergleich einschließlich oberem Textrand. Inversdruck bleibt erhalten.
- Beide Builds gestartet und regulär beendet; APPS-EXE mit Sicherung und Hashprüfung aktualisiert. Physischer Ausdruck noch offen.

## 1.9.9 – 01.11.2026

- Druckprofile implementieren Änderungsbenachrichtigungen für Name und Verbindungsanzeige. Veraltete Werte im geschlossenen Zieldrucker-Dropdown nach Umbenennen/Gerätewechsel behoben.
- Fünf Regressionstests für Header-Anzeige, Alias und Auswahl bei gleicher PID ergänzt.
- UI-Testeinstellungen in temporären Store isoliert.
- 286 Tests und Starttests beider EXEs bestanden; APPS mit Sicherung ersetzt und Hash geprüft. Kein Druckauftrag, keine Änderung an BrainOffice/HELI-SERVER.


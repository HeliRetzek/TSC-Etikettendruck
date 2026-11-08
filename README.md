# Etikettenstudio

Das Etikettenstudio ist eine Windows-Anwendung zum Gestalten und Drucken kleiner Etiketten. Es wurde mit VB.NET und WPF entwickelt und unterstützt TSC-/TSPL- und Zebra-/ZPL-Drucker sowie ausgewählte Windows-Druckwarteschlangen. Die Oberfläche ist auf Deutsch.

## Funktionen

- WYSIWYG-Editor mit Windows-Schriften, Schriftgröße ab 4 pt, Fett, Kursiv, Ausrichtung, Zeilenhöhe und Trennlinien
- Vorschau im gewählten Etikettenformat; Standardgrößen und eigene Formate
- ein bis drei identische Exemplare sowie Schwarz-auf-Weiß- und Inversdruck
- mehrere benannte Druckerprofile mit IP-Adresse oder Hostname, Port, Druckersprache und Auflösung
- Netzwerkdruck direkt über TCP/RAW, typischerweise Port 9100; TSPL als Bitmap oder ZPL mit `^GFA`
- Verbindungstest, der keinen Druckauftrag sendet
- lokale OCR für ein aus der Zwischenablage kopiertes Bild und Import passender Laborauftrag-Textdateien
- Patientenvorlagen mit Platzhaltern, optionale lokale oder zentrale Vorlagenablage sowie optionaler Druckverlauf
- Unterstützung für direkte SUPVAN-T50M-Pro-USB/HID-Geräte
- optionale Schnittstelle zur Vorlagenauswahl aus BrainOffice; dieser Aufruf löst keinen automatischen Druck aus

## Voraussetzungen

Für die Anwendung wird Windows 10 ab Version 2004 (Build 19041) oder Windows 11 benötigt. Für Entwicklung und Build sind das .NET 10 SDK und die Windows-Desktop-Entwicklungsworkloads erforderlich. NuGet-Abhängigkeiten werden beim Build wiederhergestellt.

Das Git-Repository enthält ausschließlich Quellcode und Projektdateien. Build-Ausgaben, EXE- und DLL-Dateien sowie Daten und lokale Konfiguration sind ausgeschlossen. Ein veröffentlichter **self-contained** Build bündelt die passende .NET-Laufzeit in der erzeugten Anwendung; dadurch braucht der Zielcomputer keine separate .NET-Installation. Ein **framework-dependent** Build ist kleiner, benötigt aber die passende .NET 10 Desktop Runtime auf dem Zielcomputer.

## Start aus dem Quellcode

```powershell
dotnet run --project .\source\TscLabel.App\TscLabel.App.vbproj
```

## Release lokal bauen

Das Buildskript veröffentlicht standardmäßig eine eigenständige x64-Einzeldatei in `builds\win-x64`. `-SkipDeploy` verhindert, dass eine lokale Installation unter `E:\AI\APPS` geändert wird. `-SmokeTest` startet die erzeugte Anwendung kurz ohne Druckauftrag.

```powershell
.\source\build_release.ps1 -SkipDeploy -SmokeTest
```

Ein ARM64-Build kann zusätzlich mit `-AlsoArm64` angefordert werden:

```powershell
.\source\build_release.ps1 -SkipDeploy -AlsoArm64 -SmokeTest
```

Das Skript führt die synthetischen Tests aus und schreibt ein Buildmanifest. Der normale x64-Build ist self-contained und komprimiert in eine Einzeldatei. Zum Erzeugen der kleineren framework-dependent BrainOffice-Fassung dient `-AlsoBrainOfficeDeployment`; diese Fassung setzt .NET 10 Desktop Runtime x64 voraus. Sie wird separat unter `builds\brainoffice-win-x64` abgelegt.

## Drucker einrichten

Lege in den Einstellungen ein Druckerprofil mit einem Namen, der Netzwerkadresse, dem TCP-Port und dem passenden Protokoll an. Für viele TSC-/TSPL- und Zebra-/ZPL-Netzwerkdrucker ist Port 9100 üblich; maßgeblich ist die Konfiguration des Druckers. Wähle die passende Auflösung und das eingelegte Etikettenformat. Mit **Verbindung testen** lässt sich die Erreichbarkeit prüfen, ohne etwas zu drucken. Der Druck beginnt erst über **Etikett drucken**.

Ein Drucker mit Windows-Treiber kann über die unterstützte Windows-Druckwarteschlange verwendet werden. Der direkte SUPVAN-USB/HID-Pfad gilt für unterstützte SUPVAN-Geräte. Verfügbarkeit hängt von Druckermodell, Anschluss, Netzwerk und installierten Windows-Treibern ab.

## Patientenvorlagen, OCR und lokale Daten

Die Anwendung kann Patientendaten in Vorlagenplatzhalter einsetzen. OCR wird mit der in Windows verfügbaren Texterkennung ausgeführt. Das kopierte Bild und der OCR-Rohtext werden von der OCR-Funktion nicht als Dateien gespeichert. Für den eigentlichen Auftrag gelesene oder importierte Patientendaten erscheinen jedoch im Etikett und werden beim bewussten Drucken an den gewählten Drucker übertragen.

Die lokale Konfiguration liegt unter `%LOCALAPPDATA%\SOZO\TSC-Etikettendruck`. Dort befinden sich unter anderem Druckerprofile und Vorlagen. Eine optionale zentrale Vorlagendatei kann in den Einstellungen gewählt werden. Wenn **Im Verlauf speichern** eingeschaltet ist, kann der Druckverlauf echte Etikettentexte und damit Patientendaten enthalten. Behandle diesen Verlauf und exportierte Vorlagen entsprechend vertraulich. Vorlagen und Verlauf werden nicht in dieses Git-Repository aufgenommen.

Die App druckt nicht automatisch beim Start und nicht durch die BrainOffice-Vorlagenschnellwahl. Ein Druckauftrag setzt eine bewusste Bedienung des Druckbuttons voraus.

## Projektstruktur

```text
source/
  TscLabel.App/    Windows-Oberfläche und Anwendungslogik
  TscLabel.Core/   Dokumente, Vorlagen, Druck und Integration
  TscLabel.Tests/  synthetische Regressionstests
  build_release.ps1
```

## Lizenz

Eine Lizenzdatei ist in diesem Repository derzeit nicht enthalten. Bitte frage vor einer Weiterverwendung außerhalb der gesetzlich zulässigen Nutzung beim Projektinhaber nach einer Lizenz.

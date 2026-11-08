Imports System.IO
Imports System.Text
Imports System.Windows
Imports System.Windows.Documents
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports TscLabel.App
Imports TscLabel.Core

Module Program
    Private _passed As Integer

    <STAThread>
    Public Sub Main(args As String())
        Dim tempRoot = Path.Combine(Path.GetTempPath(), "TscLabelTests-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempRoot)
        SettingsStore.TestPathOverride = Path.Combine(tempRoot, "ui-settings.json")
        Try
            TestStandardProfiles()
            TestBrainOfficeClipboardImport()
            Dim data = TestLaborauftragParser(tempRoot)
            TestOffisyOcrParser()
            Dim document = TestPatientTemplate(data)
            TestEmbeddedImages()
            Dim rendered = TestRendering(document)
            TestWysiwygLayout()
            TestTsplJob(rendered)
            TestZplJob(rendered)
            TestPrinterTransports()
            TestSupvanUsbDeviceSelection()
            TestRasterLayout()
            TestSettingsRoundTrip(tempRoot)
            TestBrainOfficeTemplateLaunchContract(tempRoot)
            TestPrintHistory(tempRoot, document)
            TestLabelTemplates(tempRoot)
            Dim renderPath As String = Nothing
            If args.Length >= 2 AndAlso args(0) = "--render" Then renderPath = args(1)
            TestWindowComposition(renderPath)
            If args.Length >= 2 AndAlso args(0) = "--windows-printer-smoke" Then TestWindowsPrinterQueue(args(1))
            If args.Length >= 2 AndAlso args(0) = "--ocr-smoke" Then TestWindowsOcr(args(1))
            If args.Length >= 1 AndAlso args(0) = "--clipboard-ocr-smoke" Then TestClipboardOcr()
            If args.Length >= 1 AndAlso args(0) = "--supvan-hardware-smoke" Then TestSupvanHardwareSmoke()
            Console.WriteLine($"OK: {_passed} synthetische Tests bestanden.")
        Finally
            SettingsStore.TestPathOverride = Nothing
            If Directory.Exists(tempRoot) Then Directory.Delete(tempRoot, True)
        End Try
    End Sub

    Private Sub TestLabelTemplates(tempRoot As String)
        Dim document = LabelDocumentFactory.CreatePatientPlaceholderTemplate()
        document.Blocks.InsertBefore(document.Blocks.FirstBlock, New Paragraph(New Run("FESTER PRAXISTEXT")) With {
            .Margin = New Thickness(0),
            .FontWeight = FontWeights.Bold,
            .FontSize = LabelDocumentFactory.PointsToDip(5)
        })
        Dim background As New WriteableBitmap(12, 6, 96, 96, PixelFormats.Bgra32, Nothing)
        Dim pixels(12 * 6 * 4 - 1) As Byte
        For index = 0 To pixels.Length - 4 Step 4
            pixels(index + 1) = 100
            pixels(index + 2) = 220
            pixels(index + 3) = 255
        Next
        background.WritePixels(New Int32Rect(0, 0, 12, 6), pixels, 12 * 4, 0)
        document.Background = New ImageBrush(background) With {
            .Opacity = 0.3,
            .ViewportUnits = BrushMappingMode.Absolute,
            .Viewport = New Rect(8, 5, 120, 60),
            .Stretch = Stretch.Uniform
        }

        Assert(LabelTemplateEngine.ContainsPlaceholders(document), "Patienten-Platzhalter werden nicht erkannt")
        Dim insuranceParagraph = document.Blocks.OfType(Of Paragraph)().First(Function(item) New TextRange(item.ContentStart, item.ContentEnd).Text.Contains("%Versicherungsnummer%", StringComparison.Ordinal))
        Dim insuranceFontSize = insuranceParagraph.FontSize
        Dim patient As New PatientLabelData With {
            .Surname = "Muster", .FirstName = "Erika", .InsuranceNumber = "1234010180",
            .PostalCode = "1010", .City = "Wien", .Street = "Prüfgasse 1", .Phone = "01 234567"
        }
        Dim replacements = LabelTemplateEngine.ApplyPatientData(document, patient, "Dr. Max Muster", "Testpraxis 01 234")
        Dim text = LabelDocumentFactory.GetPlainText(document)
        Assert(replacements >= 5, "Nicht alle Patienten-Platzhalter werden ersetzt")
        Assert(text.Contains("Muster Erika", StringComparison.Ordinal) AndAlso text.Contains("1234-010180", StringComparison.Ordinal), "Name oder formatierte Versicherungsnummer fehlt nach Platzhalterersetzung")
        Assert(text.Contains("1010 Wien, Prüfgasse 1", StringComparison.Ordinal) AndAlso text.Contains("01 234567", StringComparison.Ordinal), "Adresse oder Telefon fehlt nach Platzhalterersetzung")
        Assert(text.Contains("FESTER PRAXISTEXT", StringComparison.Ordinal) AndAlso text.Contains("Dr. Max Muster | Testpraxis 01 234", StringComparison.Ordinal), "Fester Text oder Arztzeile wurde verändert")
        Assert(Math.Abs(insuranceParagraph.FontSize - insuranceFontSize) < 0.001, "Formatierung des Platzhalters geht beim Einsetzen verloren")
        Assert(TypeOf document.Background Is ImageBrush AndAlso Math.Abs(DirectCast(document.Background, ImageBrush).Opacity - 0.3) < 0.001, "Hintergrundlogo geht bei der Platzhalterersetzung verloren")
        Assert(Not LabelTemplateEngine.ContainsPlaceholders(document), "Ersetzte Vorlage enthält noch Patienten-Platzhalter")

        Dim allPlaceholderDocument = LabelDocumentFactory.CreateBaseDocument()
        allPlaceholderDocument.Blocks.Add(New Paragraph(New Run(String.Join(" | ", LabelTemplateEngine.Placeholders.Select(Function(item) item.Token)))) With {.Margin = New Thickness(0)})
        Dim completePatient As New PatientLabelData With {
            .Title = "Dr.", .Surname = "Muster", .FirstName = "Erika", .Street = "Prüfgasse 1", .PostalCode = "1010", .City = "Wien",
            .Phone = "01 234567", .Email = "synthetisch@example.invalid", .InsuranceNumber = "1234010180", .InsuranceProvider = "ÖGK", .BirthDate = "01.01.1980"
        }
        Assert(LabelTemplateEngine.ApplyPatientData(allPlaceholderDocument, completePatient, "Dr. Testarzt", "Testpraxis", New DateTime(2026, 7, 2, 18, 46, 0)) = LabelTemplateEngine.Placeholders.Count, "Nicht alle neuen Patientenfelder werden ersetzt")
        Assert(Not LabelTemplateEngine.ContainsPlaceholders(allPlaceholderDocument), "Neue Patientenfelder bleiben nach dem Einsetzen stehen")
        Dim allText = LabelDocumentFactory.GetPlainText(allPlaceholderDocument)
        Assert(allText.Contains("Prüfgasse 1", StringComparison.Ordinal) AndAlso allText.Contains("1010", StringComparison.Ordinal) AndAlso allText.Contains("Wien", StringComparison.Ordinal) AndAlso allText.Contains("synthetisch@example.invalid", StringComparison.Ordinal) AndAlso allText.Contains("ÖGK", StringComparison.Ordinal), "Einzelne Adress-, Kontakt- oder Versicherungsfelder fehlen")
        Assert(allText.Contains("02.07.2026", StringComparison.Ordinal) AndAlso allText.Contains("02.07.2026 18:46h", StringComparison.Ordinal) AndAlso allText.Contains("18:46h", StringComparison.Ordinal), "Datum-/Zeit-Platzhalter verwenden nicht das geforderte Format")

        Dim templatePath = Path.Combine(tempRoot, "portable-templates.json")
        Dim entry As New LabelTemplateDefinition With {
            .Id = "template-a", .Name = "SOZO Patientenetikett", .PrinterId = "printer-a",
            .PrinterIds = New List(Of String) From {"printer-a", "printer-b"},
            .PrinterNames = New List(Of String) From {"Zebra 1", "Zebra 2"},
            .ProfileId = "std-50x25", .ProfileName = "Standard 50 x 25",
            .WidthMm = 50, .HeightMm = 25, .DocumentXaml = DocumentImageCodec.Serialize(document)
        }
        LabelTemplateStore.Save({entry}, templatePath)
        Dim loaded = LabelTemplateStore.Load(templatePath)
        Assert(loaded.Count = 1 AndAlso loaded(0).PrinterId = "printer-a" AndAlso loaded(0).Name = entry.Name, "Druckerspezifische Vorlage wird nicht portabel gespeichert")
        Assert(loaded(0).PrinterIds.SequenceEqual({"printer-a", "printer-b"}), "Mehrere geeignete Drucker werden nicht mit der Vorlage gespeichert")
        Dim restored = LabelTemplateStore.RestoreDocument(loaded(0))
        Assert(LabelDocumentFactory.GetPlainText(restored).Contains("FESTER PRAXISTEXT", StringComparison.Ordinal), "Vorlageninhalt lässt sich nicht wiederherstellen")
        Assert(TypeOf restored.Background Is ImageBrush, "Vorlagen-Hintergrundbild geht beim JSON-Roundtrip verloren")
        Assert(LabelTemplateStore.DefaultPath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase), "Vorlagendatei liegt nicht updatesicher im Benutzerprofil")

        Dim duplicateOlder As New LabelTemplateDefinition With {
            .Id = "duplicate-old", .Name = "Blut-Label Zebra", .PrinterId = "printer-a", .DocumentXaml = entry.DocumentXaml,
            .UpdatedAt = DateTimeOffset.Parse("2026-01-01T08:00:00+01:00")
        }
        Dim duplicateNewer As New LabelTemplateDefinition With {
            .Id = "duplicate-new", .Name = "blut-label zebra", .PrinterId = "printer-b", .DocumentXaml = entry.DocumentXaml,
            .UpdatedAt = DateTimeOffset.Parse("2026-02-01T08:00:00+01:00")
        }
        LabelTemplateStore.Save({duplicateOlder, duplicateNewer}, templatePath)
        Dim uniqueTemplates = LabelTemplateStore.Load(templatePath)
        Assert(uniqueTemplates.Count = 2 AndAlso uniqueTemplates.Select(Function(item) item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() = 2, "Technisch unterschiedliche Vorlagen verlieren beim Speichern ihre stabile ID")

        Dim packagePath = Path.Combine(tempRoot, "all-templates.zip")
        LabelTemplateStore.SavePackage({entry}, packagePath)
        Assert(File.Exists(packagePath), "Vorlagen-ZIP wird nicht erzeugt")
        Dim packageTemplates = LabelTemplateStore.LoadPackage(packagePath)
        Assert(packageTemplates.Count = 1 AndAlso packageTemplates(0).Name = entry.Name AndAlso packageTemplates(0).DocumentXaml = entry.DocumentXaml, "Vorlagen-ZIP lässt sich nicht vollständig wieder importieren")

        Dim sharedPath = Path.Combine(tempRoot, "shared-templates.json")
        Dim sharedEntry As New LabelTemplateDefinition With {
            .Id = "shared-template", .Name = "Zentrale Blutvorlage", .PrinterId = "printer-a",
            .ProfileId = "std-supvan-40x30", .ProfileName = "SUPVAN 40 x 30",
            .WidthMm = 40, .HeightMm = 30, .DocumentXaml = entry.DocumentXaml
        }
        LabelTemplateStore.Save({sharedEntry}, sharedPath)
        Dim sharedLoaded = LabelTemplateStore.Load(sharedPath)
        Assert(sharedLoaded.Count = 1 AndAlso sharedLoaded(0).Id = "shared-template", "Zentrale Vorlagendatei wird nicht separat gespeichert oder geladen")
    End Sub

    Private Sub TestBrainOfficeTemplateLaunchContract(tempRoot As String)
        Dim resultPath = Path.Combine(tempRoot, "catalog-result.json")
        Dim listCommand = BrainOfficeLabelCommand.Parse({"--brainoffice-list-templates", "--result-path", resultPath})
        Assert(listCommand.Mode = BrainOfficeLabelCommandMode.ListTemplates AndAlso listCommand.ResultPath = resultPath, "BrainOffice-Katalogaufruf wird nicht erkannt")

        Dim patientPath = Path.Combine(tempRoot, "synthetic-patient.json")
        File.WriteAllText(patientPath, "{}")
        Dim openCommand = BrainOfficeLabelCommand.Parse({"--brainoffice-open-template", "--patient-json", patientPath, "--template-id", "template-stable-id", "--template-name", "Synthese", "--request-id", "request-1", "--result-path", resultPath, "--auto-print", "false"})
        Assert(openCommand.Mode = BrainOfficeLabelCommandMode.OpenTemplate AndAlso openCommand.TemplateId = "template-stable-id" AndAlso Not openCommand.AutoPrint, "Gezielter BrainOffice-Vorlagenaufruf wird nicht vollständig geparst")
        Dim rejectedAutoPrint = False
        Try
            BrainOfficeLabelCommand.Parse({"--brainoffice-open-template", "--patient-json", patientPath, "--template-id", "template-stable-id", "--request-id", "request-2", "--result-path", resultPath, "--auto-print", "true"})
        Catch ex As ArgumentException
            rejectedAutoPrint = ex.Message.Contains("Autodruck", StringComparison.Ordinal)
        End Try
        Assert(rejectedAutoPrint, "BrainOffice-Schnellwahl erlaubt unerlaubten Autodruck")

        Dim result As New BrainOfficeTemplateLaunchResult With {.RequestId = "request-1", .Accepted = True, .TemplateResolved = True, .TemplateId = "template-stable-id", .TemplateName = "Synthese", .PrinterName = "Zebra Test", .Message = "Noch nicht gedruckt."}
        BrainOfficeResultWriter.WriteAtomic(resultPath, result)
        Dim json = File.ReadAllText(resultPath)
        Assert(json.Contains("""accepted"": true", StringComparison.Ordinal) AndAlso json.Contains("""templateId"": ""template-stable-id""", StringComparison.Ordinal), "BrainOffice-Ergebnisdatei wird nicht atomar im v1-Vertrag geschrieben")
        Assert(Not json.Contains(patientPath, StringComparison.OrdinalIgnoreCase), "Ergebnisdatei enthält einen Patientendateipfad")
    End Sub

    Private Sub TestWindowsPrinterQueue(queueName As String)
        Dim printers = WindowsPrinterService.GetInstalledPrinters()
        Assert(printers.Any(Function(printer) String.Equals(printer.FullName, queueName, StringComparison.OrdinalIgnoreCase) OrElse String.Equals(printer.Name, queueName, StringComparison.OrdinalIgnoreCase)), "Windows-Druckerfreigabe wird in der Auswahl nicht gefunden")
        WindowsPrinterService.TestQueue(queueName)
        Assert(True, "Windows-Warteschlange konnte nicht ohne Druckauftrag geöffnet werden")
        Console.WriteLine($"Windows-Drucker-Smoke: Warteschlange ‚{queueName}‘ geöffnet; kein Druckauftrag erzeugt.")
    End Sub

    Private Sub TestSupvanUsbDeviceSelection()
        Dim pid2076 = RawSupvan(&H2076, "\\?\hid#vid_1820&pid_2076&mi_00#port-a", "")
        Dim pid207F = RawSupvan(&H207F, "\\?\hid#vid_1820&pid_207f&mi_00#port-b", "SUPVAN")
        Dim pid2088 = RawSupvan(&H2088, "\\?\hid#vid_1820&pid_2088&mi_00#port-c", "SUPVAN")
        Dim accepted = SupvanUsbService.FilterCompatible({pid2076, pid207F, pid2088})
        Assert(accepted.Count = 3, "Kompatible SUPVAN-PIDs werden nicht dynamisch erkannt")
        Assert(accepted.Any(Function(item) item.Pid = &H2076) AndAlso accepted.Any(Function(item) item.Pid = &H207F) AndAlso accepted.Any(Function(item) item.Pid = &H2088), "PID 2076, 207F oder weitere kompatible PID fehlt")
        Assert(accepted(0).DisplayName.StartsWith("SUPVAN 1 (PID ", StringComparison.Ordinal), "SUPVAN-Anzeigename ist nicht benutzerfreundlich")
        Assert(accepted.All(Function(item) item.Vid = &H1820 AndAlso item.TechnicalId.Contains("vid_1820", StringComparison.OrdinalIgnoreCase)), "VID oder technische ID fehlt")

        Assert(SupvanUsbService.FilterCompatible({RawSupvan(&H2076, "\\?\hid#vid_1234&pid_2076&mi_00#x", "SUPVAN", vid:=&H1234)}).Count = 0, "Falsche VID wird akzeptiert")
        Assert(SupvanUsbService.FilterCompatible({RawSupvan(&H2076, "\\?\hid#vid_1820&pid_2076&mi_01#x", "SUPVAN", interfaceName:="MI_01")}).Count = 0, "Falsches HID-Interface wird akzeptiert")
        Assert(SupvanUsbService.FilterCompatible({RawSupvan(&H2076, "\\?\hid#vid_1820&pid_2076&mi_00#x", "SUPVAN", inputLength:=64)}).Count = 0, "Falsche Input-Reportlaenge wird akzeptiert")
        Assert(SupvanUsbService.FilterCompatible({RawSupvan(&H2076, "\\?\hid#vid_1820&pid_2076&mi_00#x", "SUPVAN", outputLength:=64)}).Count = 0, "Falsche Output-Reportlaenge wird akzeptiert")
        Assert(SupvanUsbService.FilterCompatible({RawSupvan(&H2076, "\\?\hid#vid_1820&pid_2076&mi_00#x", "FREMD")}).Count = 0, "Fremder Hersteller wird akzeptiert")

        Dim one = SupvanUsbService.ResolveSelection(Nothing, SupvanUsbService.FilterCompatible({pid2076}))
        Assert(one.SelectedDevice IsNot Nothing AndAlso Not one.RequiresUserChoice, "Ein einzelnes SUPVAN-Geraet wird nicht automatisch vorausgewaehlt")
        Dim three = SupvanUsbService.ResolveSelection(Nothing, accepted)
        Assert(three.SelectedDevice Is Nothing AndAlso three.RequiresUserChoice, "Mehrere SUPVAN-Geraete erzwingen keine eindeutige Auswahl")

        Dim configuredSerial As New PrinterProfile With {
            .ConnectionMode = PrinterProfile.SupvanUsbConnection,
            .Language = PrinterProfile.SupvanRasterLanguage,
            .SupvanDeviceId = accepted(1).TechnicalId,
            .SupvanSerialNumber = "SN-B",
            .SupvanVid = accepted(1).Vid,
            .SupvanPid = accepted(1).Pid,
            .SupvanAlias = "Blutetiketten"
        }
        Dim serialDevices = SupvanUsbService.FilterCompatible({RawSupvan(&H207F, "\\?\hid#vid_1820&pid_207f&mi_00#new-port", "SUPVAN", serial:="SN-B")})
        Dim resolvedSerial = SupvanUsbService.ResolveSelection(configuredSerial, serialDevices)
        Assert(resolvedSerial.SelectedDevice IsNot Nothing AndAlso resolvedSerial.SelectedDevice.SerialNumber = "SN-B", "Seriennummer wird nicht als stabile Identitaet verwendet")

        Dim configuredPath As New PrinterProfile With {
            .ConnectionMode = PrinterProfile.SupvanUsbConnection,
            .Language = PrinterProfile.SupvanRasterLanguage,
            .SupvanDeviceId = accepted(0).TechnicalId,
            .SupvanDevicePath = accepted(0).DevicePath,
            .SupvanAlias = "Infoetiketten"
        }
        Dim missing = SupvanUsbService.ResolveSelection(configuredPath, SupvanUsbService.FilterCompatible({pid207F}))
        Assert(missing.SelectedDevice Is Nothing AndAlso missing.Message.Contains("Portwechsel", StringComparison.Ordinal), "Fehlendes seriennummerloses Geraet oder Portwechsel wird nicht klar gemeldet")

        Dim roundTripSettings As New AppSettings With {.Printers = New List(Of PrinterProfile) From {configuredPath}, .SelectedPrinterId = configuredPath.Id}
        Dim tempSettings = Path.Combine(Path.GetTempPath(), "supvan-settings-" & Guid.NewGuid().ToString("N") & ".json")
        Try
            SettingsStore.Save(roundTripSettings, tempSettings)
            Dim loaded = SettingsStore.Load(tempSettings)
            Assert(loaded.Printers.Count = 1 AndAlso loaded.Printers(0).UsesSupvanUsb AndAlso loaded.Printers(0).SupvanAlias = "Infoetiketten" AndAlso loaded.Printers(0).SupvanDevicePath = configuredPath.SupvanDevicePath, "SUPVAN-Auswahl oder Alias wird nicht pro Druckprofil gespeichert")
        Finally
            If File.Exists(tempSettings) Then File.Delete(tempSettings)
        End Try

        Dim profiles = LabelProfile.StandardProfiles()
        Assert(profiles.Any(Function(profile) profile.Id = "std-supvan-40x30" AndAlso profile.WidthMm = 40 AndAlso profile.HeightMm = 30), "SUPVAN 40 x 30 mm fehlt")
        Assert(profiles.Any(Function(profile) profile.Id = "std-supvan-50x30" AndAlso profile.WidthMm = 50 AndAlso profile.HeightMm = 30), "SUPVAN 50 x 30 mm fehlt")
        Assert(profiles.Any(Function(profile) profile.Id = "std-supvan-50x80" AndAlso profile.WidthMm = 50 AndAlso profile.HeightMm = 80), "SUPVAN 50 x 80 mm fehlt")
    End Sub

    Private Sub TestSupvanHardwareSmoke()
        Dim devices = SupvanUsbService.EnumerateCompatibleDevices()
        Assert(devices.Count > 0, "Kein kompatibler SUPVAN per USB erkannt")
        Dim index = 0
        For Each device In devices
            index += 1
            Dim printer As New PrinterProfile With {
                .Id = "supvan-hardware-" & index.ToString(Globalization.CultureInfo.InvariantCulture),
                .Name = "SUPVAN Hardwaretest " & index.ToString(Globalization.CultureInfo.InvariantCulture),
                .ConnectionMode = PrinterProfile.SupvanUsbConnection,
                .Language = PrinterProfile.SupvanRasterLanguage,
                .PreferredProfileId = "std-supvan-40x30",
                .MarginMm = 1,
                .SupvanDeviceId = device.TechnicalId,
                .SupvanDevicePath = device.DevicePath,
                .SupvanSerialNumber = device.SerialNumber,
                .SupvanVid = device.Vid,
                .SupvanPid = device.Pid,
                .SupvanManufacturer = device.Manufacturer,
                .SupvanProductName = device.ProductName,
                .SupvanAlias = "Hardwaretest " & index.ToString(Globalization.CultureInfo.InvariantCulture)
            }
            Using timeout As New System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(40))
                Dim result = SupvanUsbService.PrintSyntheticTestAsync(New AppSettings(), printer, timeout.Token).GetAwaiter().GetResult()
                Assert(result.Frames > 0, "SUPVAN-Testdruck erzeugte keine Frames")
            End Using
        Next
        Console.WriteLine($"{devices.Count} SUPVAN-Hardwaretestetikett(e) gedruckt.")
    End Sub

    Private Function RawSupvan(pid As Integer,
                               devicePath As String,
                               manufacturer As String,
                               Optional vid As Integer = &H1820,
                               Optional interfaceName As String = "MI_00",
                               Optional inputLength As Integer = 65,
                               Optional outputLength As Integer = 65,
                               Optional serial As String = "") As SupvanUsbRawDevice
        Return New SupvanUsbRawDevice With {
            .DevicePath = devicePath,
            .Vid = vid,
            .Pid = pid,
            .InterfaceName = interfaceName,
            .InputReportLength = inputLength,
            .OutputReportLength = outputLength,
            .SerialNumber = serial,
            .Manufacturer = manufacturer,
            .ProductName = "T50M Pro"
        }
    End Function

    Private Sub TestPrinterTransports()
        Dim supvanPreset = WindowsPrinterPresetDetector.Detect("SUPVAN T50M Pro", "SUPVAN T50M Series")
        Assert(supvanPreset IsNot Nothing, "SUPVAN T50M Pro wird nicht erkannt")
        Assert(supvanPreset.Language = PrinterProfile.WindowsRasterLanguage AndAlso supvanPreset.Dpi = 203, "SUPVAN wird nicht als 203-dpi-Windows-Bilddruck vorkonfiguriert")
        Assert(supvanPreset.PreferredProfileId = "std-50x30" AndAlso supvanPreset.RasterRotationDegrees = 0, "SUPVAN-Standardformat oder Drehung ist falsch")
        Dim katasymbolPreset = WindowsPrinterPresetDetector.Detect("KATASYMBOL T50M", "USB Printer")
        Assert(katasymbolPreset IsNot Nothing AndAlso katasymbolPreset.Manufacturer.Contains("SUPVAN", StringComparison.Ordinal), "KATASYMBOL-Variante des T50M wird nicht als SUPVAN erkannt")

        Dim zd410Preset = WindowsPrinterPresetDetector.Detect("\\meli-pc\Zebra1", "ZDesigner ZD410-203dpi ZPL")
        Assert(zd410Preset IsNot Nothing AndAlso zd410Preset.Manufacturer = "Zebra ZD410", "ZD410 wird nicht modellspezifisch erkannt")
        Assert(zd410Preset.Language = PrinterProfile.ZplLanguage AndAlso zd410Preset.Dpi = 203 AndAlso zd410Preset.PreferredProfileId = "std-zebra-zd410-50x25", "ZD410 ist nicht auf ZPL, 203 dpi und 50 x 25 vorkonfiguriert")
        Dim detectedSettings = SettingsStore.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") & ".json"))
        Dim detectedCount = SettingsStore.ApplyDetectedWindowsPrinterPresets(detectedSettings, {New WindowsPrinterInfo With {.Name = "Zebra1", .FullName = "\\meli-pc\Zebra1", .DriverName = "ZDesigner ZD410-203dpi ZPL"}})
        Dim detectedZd410 = detectedSettings.Printers.First(Function(item) item.QueueName = "\\meli-pc\Zebra1")
        Assert(detectedCount = 1 AndAlso detectedZd410.Language = PrinterProfile.ZplLanguage AndAlso detectedZd410.PreferredProfileId = "std-zebra-zd410-50x25" AndAlso Not detectedZd410.CutterEnabled, "Installierter ZD410 wird nicht als fertiges Druckerprofil ergänzt")

        Dim zebraShare As New PrinterProfile With {
            .Id = "zebra-share",
            .Name = "Zebra 1",
            .ConnectionMode = PrinterProfile.WindowsQueueConnection,
            .QueueName = "\\praxis-pc\Zebra 1",
            .Language = PrinterProfile.ZplLanguage
        }
        Assert(zebraShare.UsesWindowsQueue AndAlso Not zebraShare.UsesWindowsRaster, "Zebra-Windowsfreigabe wird nicht als RAW-Warteschlange erkannt")
        Assert(zebraShare.ConnectionText.Contains("ZPL · Windows", StringComparison.Ordinal) AndAlso zebraShare.ConnectionText.Contains("Zebra 1", StringComparison.Ordinal), "Zebra-Warteschlange wird unklar beschrieben")

        Dim dymo As New PrinterProfile With {
            .Id = "dymo-share",
            .Name = "DYMO Anmeldung",
            .ConnectionMode = PrinterProfile.WindowsQueueConnection,
            .QueueName = "\\praxis-pc\DYMO LabelWriter 450",
            .Language = PrinterProfile.WindowsRasterLanguage,
            .PreferredProfileId = "std-dymo-99012",
            .RasterRotationDegrees = 90
        }
        Assert(dymo.UsesWindowsQueue AndAlso dymo.UsesWindowsRaster, "DYMO-Bilddruck wird nicht als Windows-Rasterdruck erkannt")
        Assert(dymo.ConnectionText.StartsWith("Windows-Bilddruck · Windows", StringComparison.Ordinal), "DYMO-Druckweg wird unklar beschrieben")

        Dim copied = dymo.Copy()
        Assert(copied.ConnectionMode = dymo.ConnectionMode AndAlso copied.QueueName = dymo.QueueName AndAlso copied.Language = dymo.Language, "Windows-Druckerprofil verliert beim Kopieren den Druckweg")
        Assert(copied.PreferredProfileId = "std-dymo-99012" AndAlso copied.RasterRotationDegrees = 90, "Druckerspezifisches Format oder DYMO-Drehung geht beim Kopieren verloren")
        dymo.SwapLabelOrientation = True
        copied = dymo.Copy()
        Assert(copied.SwapLabelOrientation, "Gespeicherte 90-Grad-Etikettenlage geht beim Kopieren verloren")
    End Sub

    Private Sub TestRasterLayout()
        Dim profile = LabelProfile.StandardProfiles().First(Function(item) item.Id = "std-dymo-99012")
        Dim unrotated = WindowsPrinterService.GetRasterPageSize(profile, 0)
        Dim rotated = WindowsPrinterService.GetRasterPageSize(profile, 90)
        Assert(Math.Abs(unrotated.Width / 96.0 * 25.4 - 89) < 0.01 AndAlso Math.Abs(unrotated.Height / 96.0 * 25.4 - 36) < 0.01, "DYMO-Seite ohne Drehung hat falsche Maße")
        Assert(Math.Abs(rotated.Width / 96.0 * 25.4 - 36) < 0.01 AndAlso Math.Abs(rotated.Height / 96.0 * 25.4 - 89) < 0.01, "90-Grad-DYMO-Seite wird nicht korrekt getauscht")
    End Sub

    Private Sub TestClipboardOcr()
        Assert(Clipboard.ContainsImage(), "Die aktuelle Zwischenablage enthält kein Bild")
        Dim image = Clipboard.GetImage()
        Assert(image IsNot Nothing, "Clipboard.GetImage liefert kein Bitmap")
        Dim rawText = ClipboardOcrService.RecognizeAsync(image).GetAwaiter().GetResult()
        Dim result = OffisyOcrParser.Parse(rawText)
        Assert(result.Data.HasIdentity, "Direkter Clipboard-OCR-Weg erkennt keinen Namen")
        Assert(Not String.IsNullOrWhiteSpace(result.Data.InsuranceNumber) OrElse Not String.IsNullOrWhiteSpace(result.Data.BirthDate), "Direkter Clipboard-OCR-Weg erkennt weder Versicherungsnummer noch Geburtsdatum")
        Console.WriteLine($"Clipboard-OCR-Smoke: {image.PixelWidth} × {image.PixelHeight}, {image.Format}; Kerndaten erkannt, Inhalte nicht ausgegeben oder gespeichert.")
    End Sub

    Private Sub TestWindowsOcr(imagePath As String)
        Dim image As New BitmapImage()
        image.BeginInit()
        image.CacheOption = BitmapCacheOption.OnLoad
        image.UriSource = New Uri(Path.GetFullPath(imagePath), UriKind.Absolute)
        image.EndInit()
        image.Freeze()
        Dim rawText = ClipboardOcrService.RecognizeAsync(image).GetAwaiter().GetResult()
        Dim result = OffisyOcrParser.Parse(rawText)
        Assert(result.Data.HasIdentity, "Windows-OCR erkennt im bereitgestellten Offisy-Bild keinen Namen")
        Assert(Not String.IsNullOrWhiteSpace(result.Data.InsuranceNumber) OrElse Not String.IsNullOrWhiteSpace(result.Data.BirthDate), "Windows-OCR erkennt weder Versicherungsnummer noch Geburtsdatum")
        Assert(Not String.IsNullOrWhiteSpace(result.Data.PostalCode), "Windows-OCR erkennt im bereitgestellten Offisy-Bild keine Postleitzahl")
        Assert(Not String.IsNullOrWhiteSpace(result.DoctorName), "Windows-OCR erkennt im bereitgestellten Offisy-Bild keinen Arzt")
        Dim zeroAlphaImage = CopyWithZeroAlpha(image)
        Dim zeroAlphaText = ClipboardOcrService.RecognizeAsync(zeroAlphaImage).GetAwaiter().GetResult()
        Dim zeroAlphaResult = OffisyOcrParser.Parse(zeroAlphaText)
        Assert(zeroAlphaResult.Data.HasIdentity AndAlso (Not String.IsNullOrWhiteSpace(zeroAlphaResult.Data.InsuranceNumber) OrElse Not String.IsNullOrWhiteSpace(zeroAlphaResult.Data.BirthDate)), "Clipboard-Bitmap mit Alpha=0 wird für OCR fälschlich transparent")
        Console.WriteLine("Windows-OCR-Smoke: Name, Geburts-/Versicherungsdaten, Adresse und Arzt erkannt; Inhalte nicht ausgegeben oder gespeichert.")
    End Sub

    Private Function CopyWithZeroAlpha(source As BitmapSource) As BitmapSource
        Dim converted As New FormatConvertedBitmap(source, PixelFormats.Bgra32, Nothing, 0)
        Dim stride = converted.PixelWidth * 4
        Dim pixels(stride * converted.PixelHeight - 1) As Byte
        converted.CopyPixels(pixels, stride, 0)
        For index = 3 To pixels.Length - 1 Step 4
            pixels(index) = 0
        Next
        Dim result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, converted.DpiX, converted.DpiY, PixelFormats.Bgra32, Nothing, pixels, stride)
        result.Freeze()
        Return result
    End Function

    Private Sub TestStandardProfiles()
        Dim profiles = LabelProfile.StandardProfiles()
        Assert(profiles.Count >= 7, "Standardprofile fehlen")
        Assert(profiles.Any(Function(profile) profile.WidthMm = 50 AndAlso profile.HeightMm = 30), "50 x 30 fehlt")
        Assert(profiles.Any(Function(profile) profile.Id = "std-dymo-99012" AndAlso profile.WidthMm = 89 AndAlso profile.HeightMm = 36), "DYMO 99012 mit 89 x 36 mm fehlt")
        Assert(profiles.Any(Function(profile) profile.Id = "std-25x50"), "Hochformat 25 x 50 für schmale Drucker fehlt")
        Assert(profiles.Any(Function(profile) profile.Id = "std-brother-dk11201" AndAlso profile.WidthMm = 90 AndAlso profile.HeightMm = 29), "Brother DK-11201 mit 90 x 29 mm fehlt")
        Assert(profiles.Any(Function(profile) profile.Id = "std-supvan-50x20" AndAlso profile.WidthMm = 50 AndAlso profile.HeightMm = 20), "SUPVAN T50M 50 x 20 mm fehlt")
        Assert(profiles.Any(Function(profile) profile.Id = "std-zebra-zd410-50x25" AndAlso profile.WidthMm = 50 AndAlso profile.HeightMm = 25), "Zebra ZD410 50 x 25 mm fehlt")
        Dim rotated = profiles.First(Function(profile) profile.Id = "std-50x25").CreateOriented(True)
        Assert(rotated.WidthMm = 25 AndAlso rotated.HeightMm = 50 AndAlso rotated.Name.Contains("90°", StringComparison.Ordinal), "Etikett drehen tauscht Breite und Höhe nicht")
    End Sub

    Private Sub TestBrainOfficeClipboardImport()
        Dim json = "{""schema"":""sozo.brainoffice.patient-label.v1"",""createdAt"":""2026-09-21T10:30:00+02:00"",""patient"":{""id"":""SYN-12345"",""title"":""Dr."",""firstName"":""Alexandra-Äöüß-Langname"",""lastName"":""Hügel"",""birthDate"":""1980-05-17"",""insuranceNumber"":""1234170580"",""insuranceProvider"":""ÖGK"",""street"":""Musterstraße 2"",""postalCode"":""4840"",""city"":""Vöcklabruck"",""phone"":""+43 650 1234567"",""email"":""alexandra@example.invalid""},""print"":{""copies"":2,""profile"":""std-zebra-zd410-50x25"",""autoPrint"":false}}"
        Dim transfer As BrainOfficeClipboardTransfer = Nothing
        Dim errorMessage = ""
        Assert(BrainOfficeClipboardImport.TryParse(json, transfer, errorMessage), "Gültige BrainOffice-Übergabe wird nicht gelesen")
        Assert(transfer.Patient.DisplayName = "Dr. Alexandra-Äöüß-Langname Hügel", "Unicode oder Namensreihenfolge ist falsch")
        Assert(transfer.Patient.BirthDate = "17.05.1980" AndAlso transfer.Patient.InsuranceProvider = "ÖGK", "Geburtsdatum oder Versicherung wird nicht getrennt übernommen")
        Assert(transfer.Copies = 2 AndAlso transfer.Profile = "std-zebra-zd410-50x25" AndAlso Not transfer.AutoPrint, "Druckvorgaben der Übergabe sind falsch")
        Dim text = LabelDocumentFactory.GetPlainText(LabelDocumentFactory.CreatePatientTemplate(transfer.Patient))
        Assert(text.Contains("Alexandra-Äöüß-Langname Hügel", StringComparison.Ordinal) AndAlso text.Contains("1234-170580 · ÖGK", StringComparison.Ordinal), "BrainOffice-Vorschau ist falsch")
        Dim missingJson = "{""schema"":""sozo.brainoffice.patient-label.v1"",""createdAt"":""2026-09-21T10:31:00+02:00"",""patient"":{""id"":""SYN-2"",""firstName"":""Max"",""lastName"":""Muster"",""birthDate"":"""",""insuranceNumber"":""""}}"
        Assert(BrainOfficeClipboardImport.TryParse(missingJson, transfer, errorMessage) AndAlso transfer.Patient.BirthDate = "", "Leere optionale Felder sind nicht sicher")
        Assert(Not BrainOfficeClipboardImport.TryParse("{""schema"":""falsch""}", transfer, errorMessage), "Fremdes Schema wird akzeptiert")
    End Sub

    Private Function TestLaborauftragParser(tempRoot As String) As PatientLabelData
        Dim pathValue = Path.Combine(tempRoot, "Laborauftrag-SYNTHETISCH.txt")
        Dim content = String.Join(vbCrLf, {
            "[OFFISY]",
            "P-Zuname=Muster",
            "P-Vorname=Erika",
            "P-VersNr=1234 010180",
            "P-Gebdat=01.01.1980",
            "P-Plz=1010",
            "P-Ort=Wien",
            "P-Strasse=Prüfstraße 1",
            "P-Telefon=01 234567"
        })
        File.WriteAllText(pathValue, content, Encoding.Latin1)
        Dim result = LaborauftragParser.Parse(pathValue)
        Assert(result.Data.Surname = "Muster", "Zuname falsch")
        Assert(result.Data.Street.Contains("Prüfstraße", StringComparison.Ordinal), "Latin-1-Umlaute falsch")
        Assert(result.Warnings.Count = 0, "Vollständige synthetische Daten erzeugen Warnungen")
        Assert(LaborauftragParser.FindNewest(tempRoot) = pathValue, "Neueste Laborauftrag-Datei nicht gefunden")
        Return result.Data
    End Function

    Private Function TestPatientTemplate(data As PatientLabelData) As FlowDocument
        Dim document = LabelDocumentFactory.CreatePatientTemplate(data)
        Dim text = LabelDocumentFactory.GetPlainText(document)
        Assert(text.Contains("Muster Erika", StringComparison.Ordinal), "Name fehlt in Vorlage")
        Assert(text.Contains("1234-010180", StringComparison.Ordinal), "Versicherungsnummer ist nicht 4+6 formatiert")
        Assert(Not text.Contains("geb.", StringComparison.OrdinalIgnoreCase), "Geburtsdatum wird trotz zehnstelliger SVNR doppelt ausgegeben")
        Assert(text.Contains(LabelDocumentFactory.OfficeLine, StringComparison.Ordinal), "Praxiszeile fehlt")
        Assert(document.Blocks.OfType(Of BlockUIContainer)().Any(), "Trennlinie fehlt in Patientenvorlage")
        Dim patientParagraphs = document.Blocks.OfType(Of Paragraph)().ToList()
        Dim insuranceParagraph = patientParagraphs(1)
        Dim addressParagraph = patientParagraphs(2)
        Dim phoneParagraph = patientParagraphs(3)
        Dim officeParagraph = patientParagraphs.Last()
        Assert(Math.Abs(LabelDocumentFactory.DipToPoints(insuranceParagraph.FontSize) - 9) < 0.01, "Versicherungsnummer ist nicht 9 Punkt groß")
        Assert(Math.Abs(LabelDocumentFactory.DipToPoints(addressParagraph.FontSize) - 7.5) < 0.01, "Adresse ist nicht 7,5 Punkt groß")
        Assert(Math.Abs(LabelDocumentFactory.DipToPoints(phoneParagraph.FontSize) - 7.5) < 0.01, "Telefonnummer ist nicht 7,5 Punkt groß")
        Assert(officeParagraph.FontStyle = FontStyles.Italic, "Praxiszeile ist nicht kursiv")
        Assert(officeParagraph.TextAlignment = TextAlignment.Right, "Praxiszeile ist nicht rechtsbündig")
        Assert(Math.Abs(LabelDocumentFactory.DipToPoints(officeParagraph.FontSize) - 6) < 0.01, "Praxiszeile ist nicht 6 Punkt groß")
        Assert(LabelDocumentFactory.PointsToDip(4) > 5, "4-Punkt-Umrechnung ist ungültig")
        Return document
    End Function

    Private Sub TestEmbeddedImages()
        Dim pixels(10 * 6 * 4 - 1) As Byte
        For index = 0 To pixels.Length - 4 Step 4
            pixels(index) = 20
            pixels(index + 1) = 20
            pixels(index + 2) = 20
            pixels(index + 3) = 255
        Next
        Dim bitmap = BitmapSource.Create(10, 6, 96, 96, PixelFormats.Bgra32, Nothing, pixels, 10 * 4)
        bitmap.Freeze()
        Dim image As New System.Windows.Controls.Image With {.Source = bitmap, .Width = 40, .Opacity = 0.75}
        Dim paragraph As New Paragraph(New Run("Logo "))
        paragraph.Inlines.Add(New InlineUIContainer(image))
        paragraph.Inlines.Add(New Run(" Text"))
        Dim document As New FlowDocument(paragraph)
        document.Background = New ImageBrush(bitmap) With {
            .Opacity = 0.25,
            .ViewportUnits = BrushMappingMode.Absolute,
            .Viewport = New Rect(5, 6, 40, 24),
            .Stretch = Stretch.Fill
        }
        Dim serialized = DocumentImageCodec.Serialize(document)
        Assert(serialized.Contains("SOZO_EMBEDDED_IMAGE_PNG", StringComparison.Ordinal) AndAlso serialized.Contains("SOZO_EMBEDDED_BACKGROUND", StringComparison.Ordinal), "Bilddaten werden nicht in das Dokument eingebettet")
        Dim restored = DocumentImageCodec.Deserialize(serialized)
        Dim restoredInline = DirectCast(DirectCast(restored.Blocks.FirstBlock, Paragraph).Inlines.OfType(Of InlineUIContainer)().First().Child, System.Windows.Controls.Image)
        Assert(restoredInline.Source IsNot Nothing AndAlso DirectCast(restoredInline.Source, BitmapSource).PixelWidth = 10, "Eingebettetes Logo wird nicht wiederhergestellt")
        Dim restoredBrush = TryCast(restored.Background, ImageBrush)
        Assert(restoredBrush IsNot Nothing AndAlso restoredBrush.ImageSource IsNot Nothing AndAlso Math.Abs(restoredBrush.Opacity - 0.25) < 0.01, "Eingebettete Hintergrundgrafik wird nicht wiederhergestellt")
        Dim profile = LabelProfile.StandardProfiles().First(Function(item) item.Id = "std-50x30")
        Dim rendered = LabelRenderer.Render(restored, profile, 203, 1.5, 180)
        Assert(rendered.PackedData.Any(Function(value) value <> 0), "Eingebettetes Bild erzeugt keine Druckpunkte")
    End Sub

    Private Sub TestOffisyOcrParser()
        Dim ocrText = String.Join(vbLf, {
            "Eva Beispiel Dr.med. Max Musterarzt",
            "ADRESSE",
            "Prüfgasse 9",
            "4020 Linz",
            "GEBURTSTAG",
            "02.03.1984 (42 Jahre)",
            "TELEFONNUMMER",
            "+43 699 123456",
            "SOZ. VER.",
            "4321 020384",
            "VERSICHERUNGSTRÄGER",
            "Testkasse"
        })
        Dim result = OffisyOcrParser.Parse(ocrText)
        Assert(result.Data.FirstName = "Eva" AndAlso result.Data.Surname = "Beispiel", "OCR-Name wird nicht aus Offisy-Reihenfolge gedreht")
        Assert(result.DoctorName = "Dr. Max Musterarzt", "OCR-Arztname wird nicht normalisiert")
        Assert(result.Data.Street = "Prüfgasse 9" AndAlso result.Data.PostalCode = "4020" AndAlso result.Data.City = "Linz", "OCR-Adresse falsch")
        Assert(result.Data.BirthDate = "02.03.1984", "OCR-Geburtsdatum falsch")
        Assert(result.Data.Phone.Contains("699", StringComparison.Ordinal), "OCR-Telefonnummer fehlt")
        Assert(result.Data.InsuranceNumber = "4321020384", "OCR-Versicherungsnummer falsch")
        Assert(result.Warnings.Count = 0, "Vollständiger synthetischer OCR-Text erzeugt Warnungen")
        Dim footer = LabelDocumentFactory.BuildPracticeFooter(result.DoctorName, "SOZO Vienna 01-928 4446")
        Assert(footer = "Dr. Max Musterarzt | SOZO Vienna 01-928 4446", "Arzt-/Praxiszeile falsch")
        Dim document = LabelDocumentFactory.CreatePatientTemplate(result.Data, footer)
        Assert(LabelDocumentFactory.GetPlainText(document).Contains(footer, StringComparison.Ordinal), "Arztzeile fehlt im Etikett")
        Dim compactProfile = LabelProfile.StandardProfiles().First(Function(item) item.Id = "std-50x25")
        Assert(LabelRenderer.GetPageCount(document, compactProfile, 1.5) = 1, "OCR-Patientenvorlage passt nicht auf 50 x 25 mm")

        Dim birthOnlyText = String.Join(vbLf, {
            "Anna Testperson Dr.med. Max Musterarzt",
            "ADRESSE", "Prüfweg 4", "1010 Wien",
            "GEBURTSTAG", "4.8.1964 (62 Jahre)",
            "TELEFONNUMMER", "+43 660 123456"
        })
        Dim birthOnlyResult = OffisyOcrParser.Parse(birthOnlyText)
        Assert(String.IsNullOrWhiteSpace(birthOnlyResult.Data.InsuranceNumber), "Fehlende Versicherungsnummer wird aus anderen OCR-Ziffern erfunden")
        Assert(birthOnlyResult.Data.BirthDate = "4.8.1964", "Vorhandenes OCR-Geburtsdatum geht ohne Versicherungsnummer verloren")
        Assert(birthOnlyResult.Warnings.Any(Function(value) value.Contains("Geburtsdatum wird ersatzweise", StringComparison.Ordinal)), "OCR weist nicht auf die Geburtsdatum-Ersatzausgabe hin")
        Dim birthOnlyDocument = LabelDocumentFactory.CreatePatientTemplate(birthOnlyResult.Data)
        Assert(LabelDocumentFactory.GetPlainText(birthOnlyDocument).Contains("geb. 04.08.1964", StringComparison.Ordinal), "Fehlende Versicherungsnummer wird nicht durch das formatierte Geburtsdatum ersetzt")
        Dim placeholderDocument = LabelDocumentFactory.CreatePatientPlaceholderTemplate()
        LabelTemplateEngine.ApplyPatientData(placeholderDocument, birthOnlyResult.Data)
        Assert(LabelDocumentFactory.GetPlainText(placeholderDocument).Contains("geb. 04.08.1964", StringComparison.Ordinal), "Versicherungs-Platzhalter verwendet bei fehlender Nummer nicht das Geburtsdatum")

        Dim partialInsuranceText = String.Join(vbLf, {
            "Berta Prüfperson Dr.med. Max Musterarzt",
            "GEBURTSTAG", "05.06.1972 (54 Jahre)",
            "TELEFONNUMMER", "+43 660 654321",
            "SOZ. VER.", "9876"
        })
        Dim partialInsuranceResult = OffisyOcrParser.Parse(partialInsuranceText)
        Assert(partialInsuranceResult.Data.InsuranceNumber = "9876050672", "Vierstellige Versicherungsnummer wird nicht mit ddMMyy aus dem Geburtsdatum ergänzt")
        Assert(LabelDocumentFactory.FormatInsuranceOrBirthDate(partialInsuranceResult.Data.InsuranceNumber, partialInsuranceResult.Data.BirthDate) = "9876-050672", "Ergänzte zehnstellige Versicherungsnummer wird nicht als vvvv-ddmmyy formatiert")
        Assert(LabelDocumentFactory.FormatBirthDate("19640804") = "04.08.1964", "Kompaktes Geburtsdatum wird nicht nach dd.MM.yyyy formatiert")
    End Sub

    Private Function TestRendering(document As FlowDocument) As LabelRenderResult
        Dim profile = LabelProfile.StandardProfiles().First(Function(item) item.Id = "std-50x30")
        Dim pages = LabelRenderer.GetPageCount(document, profile, 1.5)
        Assert(pages = 1, "Patientenvorlage passt nicht auf 50 x 30 mm")
        Dim rendered = LabelRenderer.Render(document, profile, 203, 1.5, 180)
        Assert(rendered.WidthDots = 400, "50 mm ergeben bei 203 dpi nicht 400 Punkte")
        Assert(rendered.HeightDots = 240, "30 mm ergeben bei 203 dpi nicht 240 Punkte")
        Assert(rendered.WidthBytes = 50, "Bitmap-Zeilenbreite falsch")
        Assert(rendered.PackedData.Length = 12000, "Bitmap-Datenlänge falsch")
        Assert(rendered.PackedData.Any(Function(value) value <> 0), "Gerenderte Bitmap ist leer")
        Return rendered
    End Function

    Private Sub TestWysiwygLayout()
        TestEditorRasterParity()
        Dim profile = LabelProfile.StandardProfiles().First(Function(item) item.Id = "std-50x25")
        Dim cropped = LabelDocumentFactory.CreateBaseDocument()
        cropped.Blocks.Add(New Paragraph(New Run("OBEN")) With {.Margin = New Thickness(0), .FontSize = 12})
        Dim visibleOnly = LabelRenderer.Render(cropped, profile, 203, 1.5, 180)
        Dim noMargin = LabelRenderer.Render(cropped, profile, 203, 0, 180)
        Dim marginSeven = LabelRenderer.Render(cropped, profile, 203, 7, 180)
        Dim origin = InkOrigin(noMargin)
        Dim inset = InkOrigin(marginSeven)
        Console.WriteLine($"Raster origin: {origin}; 7mm: {inset}")
        Assert(Math.Abs(inset.X - origin.X - 7 / 25.4 * 203) <= 2, "7-mm-Innenrand fehlt horizontal in der Druckgrafik")
        Assert(Math.Abs(inset.Y - origin.Y - 7 / 25.4 * 203) <= 2, "7-mm-Innenrand fehlt vertikal in der Druckgrafik")
        Dim normalFrames = SupvanUsbService.EncodeFramesForTest(marginSeven.Preview, True, 180)
        Dim inverseFrames = SupvanUsbService.EncodeFramesForTest(marginSeven.Preview, False, 180)
        Dim sourceRow = 0
        For frameIndex = 0 To normalFrames.Count - 1
            Dim normal = DecodeSupvanFrame(normalFrames(frameIndex))
            Dim inverse = DecodeSupvanFrame(inverseFrames(frameIndex))
            Dim rows = CInt(normal(4)) + 256 * CInt(normal(5))
            Dim complementary = True
            Dim matchesRaster = True
            For row = 0 To rows - 1
                For x = 0 To 383
                    Dim outputDot = 383 - x
                    Dim offset = 14 + row * 48 + outputDot \ 8
                    Dim bit = (normal(offset) And (1 << (outputDot Mod 8))) <> 0
                    Dim sourceX = x + (marginSeven.WidthDots - 384) \ 2
                    Dim expected = (marginSeven.PackedData((sourceRow + row) * marginSeven.WidthBytes + sourceX \ 8) And (1 << (7 - sourceX Mod 8))) <> 0
                    matchesRaster = matchesRaster AndAlso bit = expected
                    complementary = complementary AndAlso (normal(offset) Xor inverse(offset)) = 255
                Next
            Next
            Assert(matchesRaster, "SUPVAN-Frame verschiebt oder verändert Druckpunkte gegenüber der Druckgrafik")
            Assert(complementary, "SUPVAN-Inversdruck invertiert nicht alle Druckpunkte einschließlich Rand")
            sourceRow += rows
        Next
        cropped.Blocks.Add(New Paragraph(New Run("AUSSERHALB")) With {.Margin = New Thickness(0, 300, 0, 0), .FontSize = 12})
        Dim withOverflow = LabelRenderer.Render(cropped, profile, 203, 1.5, 180)
        Assert(visibleOnly.PackedData.SequenceEqual(withOverflow.PackedData), "Inhalt außerhalb der Etikettenfläche verändert oder skaliert die Druckgrafik")
        Dim document = LabelDocumentFactory.CreateBaseDocument()
        Dim first As New Paragraph(New Run("Inventar-2023")) With {
            .Margin = New Thickness(0), .FontSize = LabelDocumentFactory.PointsToDip(24), .FontWeight = FontWeights.Bold,
            .LineHeight = LabelDocumentFactory.PointsToDip(30), .LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        }
        Dim second As New Paragraph(New Run("Notebook 16/1TB")) With {
            .Margin = New Thickness(0), .FontSize = LabelDocumentFactory.PointsToDip(13), .FontWeight = FontWeights.Bold,
            .LineHeight = LabelDocumentFactory.PointsToDip(20), .LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        }
        Dim third As New Paragraph(New Run("Ersatz-Laptop")) With {
            .Margin = New Thickness(0), .FontSize = LabelDocumentFactory.PointsToDip(13), .FontWeight = FontWeights.Bold,
            .LineHeight = LabelDocumentFactory.PointsToDip(20), .LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        }
        document.Blocks.Add(first)
        document.Blocks.Add(second)
        document.Blocks.Add(third)
        Assert(LabelRenderer.GetPageCount(document, profile, 1.5) > 1, "Zu hohe Zeilen werden nicht als sichtbarer Überlauf erkannt")
        first.LineHeight = LabelDocumentFactory.PointsToDip(20)
        second.LineHeight = LabelDocumentFactory.PointsToDip(11)
        third.LineHeight = LabelDocumentFactory.PointsToDip(11)
        Assert(LabelRenderer.GetPageCount(document, profile, 1.5) = 1, "Kompakte Zeilenhöhe wird nicht als passend erkannt")
        Assert(LabelRenderer.GetPageCount(document, profile, 6) > 1, "Großer Innenrand verändert die nutzbare Fläche nicht")
        Assert(LabelRenderer.GetPageCount(document, profile, 0) = 1, "Rand 0 nutzt nicht die maximale Etikettenfläche")

        LabelRenderer.ApplyPageLayout(document, profile, 1.5)
        Assert(Math.Abs(document.PageWidth - profile.WidthMm / 25.4 * 96) < 0.01, "Editor-Seitenbreite stimmt nicht mit Etikettenbreite überein")
        Assert(Math.Abs(document.PageHeight - profile.HeightMm / 25.4 * 96) < 0.01, "Editor-Seitenhöhe stimmt nicht mit Etikettenhöhe überein")
        Assert(Math.Abs(document.PagePadding.Left - 1.5 / 25.4 * 96) < 0.01, "Editor und Druck verwenden nicht denselben Innenrand")

        Dim trailingWhitespace = LabelDocumentFactory.CreateBaseDocument()
        trailingWhitespace.Blocks.Add(New Paragraph(New Run("Sichtbarer Inhalt")) With {.Margin = New Thickness(0), .FontSize = LabelDocumentFactory.PointsToDip(10)})
        For index = 1 To 20
            trailingWhitespace.Blocks.Add(New Paragraph(New Run("")) With {.Margin = New Thickness(0), .FontSize = LabelDocumentFactory.PointsToDip(5), .LineHeight = LabelDocumentFactory.PointsToDip(5), .LineStackingStrategy = LineStackingStrategy.BlockLineHeight})
        Next
        Assert(LabelRenderer.GetPageCount(trailingWhitespace, profile, 1) = 1, "Leere Schlusszeilen lösen fälschlich eine Drucksperre aus")
    End Sub

    Private Sub TestEditorRasterParity()
        Dim profile = LabelProfile.StandardProfiles().First(Function(item) item.Id = "std-50x30")
        Dim editor As New System.Windows.Controls.RichTextBox With {
            .Document = LabelDocumentFactory.CreateFreeTemplate(),
            .Padding = New Thickness(0), .BorderThickness = New Thickness(0),
            .Background = Brushes.White, .Foreground = Brushes.Black,
            .VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Hidden,
            .HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Hidden
        }
        Dim size As New Size(50 / 25.4 * 96, 30 / 25.4 * 96)
        editor.Measure(size)
        editor.Arrange(New Rect(size))
        editor.UpdateLayout()
        LabelRenderer.ApplyPageLayout(editor.Document, profile, 7)
        editor.UpdateLayout()
        ' Normalize the screen to the same rounded 400 x 240 output size, avoiding DPI rounding differences.
        Dim screenVisual As New DrawingVisual()
        Using drawing = screenVisual.RenderOpen()
            drawing.DrawRectangle(New VisualBrush(editor), Nothing, New Rect(0, 0, 400, 240))
        End Using
        Dim screen As New RenderTargetBitmap(400, 240, 96, 96, PixelFormats.Pbgra32)
        screen.Render(screenVisual)
        Dim printed = LabelRenderer.Render(editor.Document, profile, 203, 7, 180)
        Dim screenPixels(400 * 240 * 4 - 1) As Byte
        screen.CopyPixels(screenPixels, 400 * 4, 0)
        Dim differences = 0
        Dim ink = 0
        Dim screenTop = 240
        Dim printTop = 240
        For y = 0 To 239
            For x = 0 To 399
                Dim offset = (y * 400 + x) * 4
                Dim screenBlack = screenPixels(offset + 3) > 32 AndAlso (CInt(screenPixels(offset + 2)) * 299 + CInt(screenPixels(offset + 1)) * 587 + CInt(screenPixels(offset)) * 114) \ 1000 < 180
                Dim printBlack = (printed.PackedData(y * printed.WidthBytes + x \ 8) And (1 << (7 - x Mod 8))) <> 0
                If screenBlack Then
                    ink += 1
                    screenTop = Math.Min(screenTop, y)
                End If
                If printBlack Then printTop = Math.Min(printTop, y)
                If screenBlack <> printBlack Then differences += 1
            Next
        Next
        Assert(ink > 1000 AndAlso differences < ink * 0.03, $"Druckraster weicht vom echten Editorlayout ab: {differences} abweichende Punkte bei {ink} Textpunkten")
        Assert(Math.Abs(screenTop - printTop) <= 1, "Oberer Textrand weicht von der Vorschau ab")
        For Each item In { (Name:="editor-parity-screen", Bitmap:=DirectCast(screen, BitmapSource)), (Name:="editor-parity-print", Bitmap:=DirectCast(printed.Preview, BitmapSource)) }
            Dim encoder As New PngBitmapEncoder()
            encoder.Frames.Add(BitmapFrame.Create(item.Bitmap))
            Using output = File.Create(Path.Combine("builds", item.Name & ".png"))
                encoder.Save(output)
            End Using
        Next
    End Sub

    Private Function InkOrigin(rendered As LabelRenderResult) As Point
        Dim minX = rendered.WidthDots
        Dim minY = rendered.HeightDots
        For y = 0 To rendered.HeightDots - 1
            For x = 0 To rendered.WidthDots - 1
                If (rendered.PackedData(y * rendered.WidthBytes + x \ 8) And (1 << (7 - x Mod 8))) <> 0 Then
                    minX = Math.Min(minX, x)
                    minY = Math.Min(minY, y)
                End If
            Next
        Next
        Return New Point(minX, minY)
    End Function

    Private Function DecodeSupvanFrame(frame As Byte()) As Byte()
        Using input As New MemoryStream(frame, 13, frame.Length - 13)
            Using decoder = SharpCompress.Compressors.LZMA.LzmaStream.Create(frame.Take(5).ToArray(), input, CLng(frame.Length - 13), BitConverter.ToInt64(frame, 5), False)
                Using output As New MemoryStream()
                    decoder.CopyTo(output)
                    Return output.ToArray()
                End Using
            End Using
        End Using
    End Function

    Private Sub TestTsplJob(rendered As LabelRenderResult)
        Dim profile = LabelProfile.StandardProfiles().First(Function(item) item.Id = "std-50x30")
        Dim settings As New AppSettings()
        Dim job = TsplJobBuilder.Build(rendered, profile, settings, 3)
        Dim text = Encoding.Latin1.GetString(job)
        Assert(text.StartsWith("SIZE 50 mm,30 mm", StringComparison.Ordinal), "SIZE-Befehl fehlt")
        Assert(text.Contains("GAP 3 mm,0 mm", StringComparison.Ordinal), "GAP-Befehl fehlt")
        Assert(text.Contains("BITMAP 0,0,50,240,0,", StringComparison.Ordinal), "BITMAP-Kopf falsch")
        Assert(text.Contains("PRINT 1,3", StringComparison.Ordinal), "Kopienzahl falsch")
        Assert(Not text.Contains("SET CUTTER", StringComparison.Ordinal), "Cutter ist unerwartet aktiv")
        settings.CutterEnabled = True
        text = Encoding.Latin1.GetString(TsplJobBuilder.Build(rendered, profile, settings, 2))
        Assert(text.Contains("SET CUTTER BATCH", StringComparison.Ordinal), "Cutter-Batch fehlt")
        Assert(text.Contains("SET CUTTER OFF", StringComparison.Ordinal), "Cutter-Rücksetzung fehlt")

        settings.CutterEnabled = False
        settings.NormalPrint = False
        Dim inverseJob = TsplJobBuilder.Build(rendered, profile, settings, 1)
        settings.NormalPrint = True
        Dim normalJob = TsplJobBuilder.Build(rendered, profile, settings, 1)
        Assert(inverseJob.Length = normalJob.Length, "Normal- und Inversjob haben unterschiedliche Länge")
        Dim changedBytes = 0
        For index = 0 To inverseJob.Length - 1
            If inverseJob(index) <> normalJob(index) Then changedBytes += 1
        Next
        Assert(changedBytes = rendered.PackedData.Length, "Normaldruck invertiert nicht exakt die Bitmapdaten")
    End Sub

    Private Sub TestZplJob(rendered As LabelRenderResult)
        Dim settings As New AppSettings With {.NormalPrint = True}
        Dim printer As New PrinterProfile With {
            .Id = "zebra-test", .Name = "Zebra Labor", .Host = "192.0.2.55", .Port = 9100,
            .Language = PrinterProfile.ZplLanguage, .Dpi = 203
        }
        Dim job = ZplJobBuilder.Build(rendered, settings, printer, 3)
        Dim text = Encoding.ASCII.GetString(job)
        Assert(text.StartsWith("^XA", StringComparison.Ordinal), "ZPL-Startformat fehlt")
        Assert(text.Contains("^PW400", StringComparison.Ordinal) AndAlso text.Contains("^LL240", StringComparison.Ordinal), "ZPL-Etikettenmaße fehlen")
        Assert(text.Contains("^GFA,12000,12000,50,", StringComparison.Ordinal), "ZPL-Grafikfeld ist falsch")
        Assert(text.Contains("^PQ3,0,1,N", StringComparison.Ordinal), "ZPL-Kopienzahl fehlt")
        Assert(text.EndsWith("^XZ" & vbLf, StringComparison.Ordinal), "ZPL-Endformat fehlt")
        Dim normalPayload = ExtractZplGraphic(text)
        Assert(normalPayload.SequenceEqual(rendered.PackedData), "ZPL-Normaldruck verwendet nicht die gerenderte Schwarzbitmap")

        settings.NormalPrint = False
        Dim inversePayload = ExtractZplGraphic(Encoding.ASCII.GetString(ZplJobBuilder.Build(rendered, settings, printer, 1)))
        Assert(inversePayload.Length = normalPayload.Length, "ZPL-Inversdatenlänge falsch")
        Assert(inversePayload.Select(Function(value, index) value = CByte(Not normalPayload(index))).All(Function(matches) matches), "ZPL-Inversdruck invertiert nicht alle Grafikbytes")

        printer.CutterEnabled = True
        text = Encoding.ASCII.GetString(ZplJobBuilder.Build(rendered, settings, printer, 2))
        Assert(text.Contains("^MMC", StringComparison.Ordinal), "ZPL-Cuttermodus fehlt")
        Assert(text.Contains("^XA^MMT^XZ", StringComparison.Ordinal), "ZPL-Cuttermodus wird nicht zurückgesetzt")

        Dim tallData(80 * 1800 - 1) As Byte
        Dim tallRender As New LabelRenderResult(rendered.Preview, tallData, 640, 1800, 80)
        text = Encoding.ASCII.GetString(ZplJobBuilder.Build(tallRender, settings, printer, 1))
        Assert(text.Split("^GFA,", StringSplitOptions.None).Length - 1 = 2, "Große ZPL-Bitmap wird nicht in zulässige Grafikblöcke geteilt")
    End Sub

    Private Function ExtractZplGraphic(zpl As String) As Byte()
        Dim marker = "^GFA,"
        Dim startIndex = zpl.IndexOf(marker, StringComparison.Ordinal)
        If startIndex < 0 Then Throw New InvalidOperationException("ZPL-Grafikmarker fehlt")
        startIndex += marker.Length
        For count = 1 To 3
            startIndex = zpl.IndexOf(","c, startIndex) + 1
            If startIndex <= 0 Then Throw New InvalidOperationException("ZPL-Grafikkopf unvollständig")
        Next
        Dim endIndex = zpl.IndexOf("^FS", startIndex, StringComparison.Ordinal)
        Dim hexData = zpl.Substring(startIndex, endIndex - startIndex)
        Dim result(hexData.Length \ 2 - 1) As Byte
        For index = 0 To result.Length - 1
            result(index) = Convert.ToByte(hexData.Substring(index * 2, 2), 16)
        Next
        Return result
    End Function

    Private Sub TestPrintHistory(tempRoot As String, document As FlowDocument)
        Dim historyPath = Path.Combine(tempRoot, "portable-history.json")
        Dim profile = LabelProfile.StandardProfiles().First(Function(item) item.Id = "std-50x30")
        PrintHistoryStore.Add(document, profile, 2, True, historyPath)
        Dim entries = PrintHistoryStore.Load(historyPath)
        Assert(entries.Count = 1, "Druckverlauf speichert den Eintrag nicht")
        Assert(entries(0).Copies = 2 AndAlso entries(0).NormalPrint, "Druckmodus oder Kopien fehlen im Verlauf")
        Dim restored = PrintHistoryStore.RestoreDocument(entries(0))
        Assert(LabelDocumentFactory.GetPlainText(restored) = LabelDocumentFactory.GetPlainText(document), "Formatierter Verlauf lässt sich nicht wiederherstellen")
        PrintHistoryStore.Clear(historyPath)
        Assert(PrintHistoryStore.Load(historyPath).Count = 0, "Druckverlauf lässt sich nicht leeren")
        Assert(Path.GetDirectoryName(PrintHistoryStore.DefaultPath) = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "Standard-Verlauf liegt nicht im Programmverzeichnis")

        PrintHistoryStore.Add(document, profile, 1, False, "Zebra Labor", PrinterProfile.ZplLanguage, historyPath)
        entries = PrintHistoryStore.Load(historyPath)
        Assert(entries(0).PrinterName = "Zebra Labor" AndAlso entries(0).PrinterLanguage = PrinterProfile.ZplLanguage, "Zieldrucker fehlt im Druckverlauf")
    End Sub

    Private Sub TestSettingsRoundTrip(tempRoot As String)
        Dim settingsPath = Path.Combine(tempRoot, "settings.json")
        Dim settings As New AppSettings With {.PrinterIp = "192.0.2.10", .PrinterPort = 9101, .SelectedProfileId = "std-40x25", .LastUsedTemplateId = "template-workstation", .PracticeLine = "Synthetische Praxis 01 234", .SharedTemplatePath = "\\nas\etiketten\TSC-Etikettendruck-Vorlagen.json", .TemplateSaveTarget = "SHARED"}
        settings.CustomProfiles.Add(New LabelProfile With {.Id = "custom-test", .Name = "Test", .WidthMm = 42, .HeightMm = 24, .GapMm = 2, .IsCustom = True})
        SettingsStore.Save(settings, settingsPath)
        Dim loaded = SettingsStore.Load(settingsPath)
        Assert(loaded.PrinterIp = "192.0.2.10" AndAlso loaded.PrinterPort = 9101, "Einstellungen-Roundtrip falsch")
        Assert(loaded.CustomProfiles.Count = 1, "Eigenes Format nicht gespeichert")
        Assert(loaded.PracticeLine = "Synthetische Praxis 01 234", "Praxiszeile wird nicht gespeichert")
        Assert(loaded.LastUsedTemplateId = "template-workstation", "Zuletzt verwendete Arbeitsplatz-Vorlage wird nicht gespeichert")
        Assert(loaded.SharedTemplatePath = "\\nas\etiketten\TSC-Etikettendruck-Vorlagen.json" AndAlso loaded.TemplateSaveTarget = "SHARED", "Zentraler Vorlagenpfad oder Speicherziel wird nicht gespeichert")
        Assert(loaded.Printers.Count = 1 AndAlso loaded.Printers(0).Host = "192.0.2.10" AndAlso loaded.Printers(0).Language = PrinterProfile.TsplLanguage, "Alte Einzel-Druckereinstellung wird nicht migriert")
        Assert(loaded.NormalPrint AndAlso loaded.PrintModeSemanticsVersion = 2, "Alte Druckmodus-Semantik wird nicht einmalig auf Normaldruck migriert")

        loaded.NormalPrint = False
        SettingsStore.Save(loaded, settingsPath)
        loaded = SettingsStore.Load(settingsPath)
        Assert(Not loaded.NormalPrint AndAlso loaded.PrintModeSemanticsVersion = 2, "Bewusst aktivierter Inversdruck bleibt nach der Migration nicht gespeichert")

        settings.Printers = New List(Of PrinterProfile) From {
            New PrinterProfile With {.Id = "tsc-a", .Name = "TSC Anmeldung", .Host = "192.0.2.20", .Port = 9100, .Language = PrinterProfile.TsplLanguage, .MarginMm = 1.2},
            New PrinterProfile With {.Id = "zebra-b", .Name = "Zebra Labor", .Host = "192.0.2.21", .Port = 9100, .Language = PrinterProfile.ZplLanguage, .Dpi = 300, .MarginMm = 0.8},
            New PrinterProfile With {.Id = "zebra-share", .Name = "Zebra 1", .ConnectionMode = PrinterProfile.WindowsQueueConnection, .QueueName = "\\praxis-pc\Zebra 1", .Language = PrinterProfile.ZplLanguage},
            New PrinterProfile With {.Id = "dymo-share", .Name = "DYMO Anmeldung", .ConnectionMode = PrinterProfile.WindowsQueueConnection, .QueueName = "\\praxis-pc\DYMO", .Language = PrinterProfile.WindowsRasterLanguage},
            New PrinterProfile With {.Id = "brother-home", .Name = "Brother QL-820NWB Zuhause", .ConnectionMode = PrinterProfile.WindowsQueueConnection, .QueueName = "Brother QL-820NWB", .Language = PrinterProfile.WindowsRasterLanguage},
            New PrinterProfile With {.Id = "supvan-home", .Name = "SUPVAN T50M Pro", .ConnectionMode = PrinterProfile.WindowsQueueConnection, .QueueName = "SUPVAN T50M Pro", .Language = PrinterProfile.WindowsRasterLanguage}
        }
        settings.SelectedPrinterId = "zebra-b"
        SettingsStore.Save(settings, settingsPath)
        loaded = SettingsStore.Load(settingsPath)
        Assert(loaded.Printers.Count = 6, "Mehrere Druckerprofile werden nicht gespeichert")
        Assert(loaded.SelectedPrinterId = "zebra-b" AndAlso loaded.Printers(1).Language = PrinterProfile.ZplLanguage AndAlso loaded.Printers(1).Dpi = 300, "Zieldrucker, ZPL oder DPI gehen beim Laden verloren")
        Assert(loaded.Printers(2).UsesWindowsQueue AndAlso loaded.Printers(2).QueueName = "\\praxis-pc\Zebra 1", "Zebra-Windowsfreigabe geht beim Laden verloren")
        Assert(loaded.Printers(3).UsesWindowsRaster AndAlso loaded.Printers(3).QueueName = "\\praxis-pc\DYMO", "DYMO-Windows-Bilddruck geht beim Laden verloren")
        Assert(loaded.Printers(3).PreferredProfileId = "std-dymo-99012" AndAlso loaded.Printers(3).RasterRotationDegrees = 90, "Altes DYMO-Profil wird nicht automatisch auf 99012 und 90 Grad migriert")
        Assert(loaded.Printers(4).PreferredProfileId = "std-brother-dk11201" AndAlso loaded.Printers(4).RasterRotationDegrees = 90, "Brother QL-820NWB wird nicht automatisch auf DK-11201 und 90 Grad migriert")
        Assert(loaded.Printers(5).PreferredProfileId = "std-50x30" AndAlso loaded.Printers(5).Dpi = 203 AndAlso loaded.Printers(5).RasterRotationDegrees = 0, "SUPVAN T50M wird nicht automatisch auf Windows-Bilddruck mit 203 dpi und 50 x 30 mm migriert")
    End Sub

    Private Sub TestPrinterDisplayBinding(window As MainWindow)
        Dim vienna As New PrinterProfile With {.Name = "SUPVAN Wien", .ConnectionMode = PrinterProfile.SupvanUsbConnection, .SupvanPid = &H207F, .SupvanDeviceId = "path-wien"}
        Dim other As New PrinterProfile With {.Name = "SUPVAN Reserve", .ConnectionMode = PrinterProfile.SupvanUsbConnection, .SupvanPid = &H207F, .SupvanDeviceId = "path-reserve"}
        Dim combo As New System.Windows.Controls.ComboBox With {.Width = 420, .Height = 40, .ItemTemplate = DirectCast(window.FindResource("PrinterItemTemplate"), DataTemplate), .ItemsSource = New PrinterProfile() {vienna, other}, .SelectedItem = vienna}
        Dim header = DirectCast(combo.ItemTemplate.LoadContent(), FrameworkElement)
        header.SetBinding(FrameworkElement.DataContextProperty, New System.Windows.Data.Binding("SelectedItem") With {.Source = combo})
        header.Measure(New Size(420, 40))
        header.Arrange(New Rect(0, 0, 420, 40))
        combo.Measure(New Size(420, 40))
        combo.Arrange(New Rect(0, 0, 420, 40))
        combo.Dispatcher.Invoke(Sub()
                                End Sub, System.Windows.Threading.DispatcherPriority.ApplicationIdle)
        combo.UpdateLayout()
        Assert(VisualText(header).Contains("SUPVAN Wien"), "Ausgewählter Drucker fehlt in der Header-Vorlage")
        vienna.Name = "SUPVAN Vöcklabruck"
        vienna.SupvanDeviceId = "path-voecklabruck"
        combo.Items.Refresh()
        combo.UpdateLayout()
        Assert(VisualText(header).Contains("SUPVAN Vöcklabruck") AndAlso Not VisualText(header).Contains("SUPVAN Wien"), "Header behält alten Druckernamen nach Umbenennen")
        Assert(VisualText(header).Contains("path-voecklabruck"), "Header behält alte USB-Gerätekennung")
        combo.SelectedItem = other
        combo.UpdateLayout()
        Assert(VisualText(header).Contains("SUPVAN Reserve") AndAlso combo.SelectedItem Is other, "Auswahl zweier Geräte mit gleicher PID wird nicht übernommen")
        combo.SelectedItem = vienna
        vienna.SupvanAlias = "Blutetiketten"
        combo.UpdateLayout()
        Assert(VisualText(header).Contains("SUPVAN Vöcklabruck") AndAlso VisualText(header).Contains("Blutetiketten"), "Erneute Auswahl oder Aliasänderung bleibt unsichtbar")
    End Sub

    Private Function VisualText(parent As DependencyObject) As String
        Dim text = TryCast(parent, System.Windows.Controls.TextBlock)
        Dim result = If(text?.Text, "")
        For index = 0 To VisualTreeHelper.GetChildrenCount(parent) - 1
            result &= " " & VisualText(VisualTreeHelper.GetChild(parent, index))
        Next
        Return result
    End Function

    Private Sub TestWindowComposition(renderPath As String)
        Const uiWidth As Integer = 1240
        Const uiHeight As Integer = 740
        Dim window As New MainWindow With {.Width = uiWidth, .Height = uiHeight}
        Dim root = DirectCast(window.Content, FrameworkElement)
        root.Measure(New Size(uiWidth, uiHeight))
        root.Arrange(New Rect(0, 0, uiWidth, uiHeight))
        root.UpdateLayout()
        Assert(window.FindName("LabelEditor") IsNot Nothing, "Etiketteneditor fehlt")
        Assert(window.FindName("PrintButton") IsNot Nothing, "Druckbutton fehlt")
        Assert(window.FindName("SettingsProfileList") IsNot Nothing, "Etikettengrößen-Editor fehlt")
        Assert(window.FindName("LineHeightCombo") IsNot Nothing, "Zeilenhöhen-Auswahl fehlt")
        Dim inverseCheck = DirectCast(window.FindName("InversePrintCheck"), System.Windows.Controls.CheckBox)
        Assert(inverseCheck IsNot Nothing AndAlso Convert.ToString(inverseCheck.Content) = "Invers: weißer Text auf schwarzem Hintergrund", "Verständlicher Invers-Schalter fehlt")
        Assert(window.FindName("HistoryList") IsNot Nothing, "Druckverlauf fehlt")
        Assert(window.FindName("ClipboardOcrButton") IsNot Nothing, "Clipboard-OCR-Button fehlt")
        Assert(window.FindName("PracticeLineBox") IsNot Nothing, "Einstellbare Praxiszeile fehlt")
        Assert(window.FindName("TargetPrinterCombo") IsNot Nothing, "Zieldrucker-Auswahl im Header fehlt")
        Assert(window.FindName("SettingsPrinterList") IsNot Nothing, "Druckermanager fehlt")
        Assert(window.FindName("PrinterLanguageCombo") IsNot Nothing, "TSPL-/ZPL-Auswahl fehlt")
        Assert(window.FindName("PrinterDpiCombo") IsNot Nothing, "Drucker-DPI-Auswahl fehlt")
        Assert(window.FindName("PrinterConnectionModeCombo") IsNot Nothing, "Druckweg-Auswahl fehlt")
        Assert(window.FindName("SupvanDeviceCombo") IsNot Nothing, "Direkte SUPVAN-USB-Geräteauswahl fehlt")
        Assert(window.FindName("PrinterProfileCombo") IsNot Nothing, "Druckerspezifisches Standard-Etikett fehlt")
        Assert(window.FindName("SwapLabelOrientationCheck") IsNot Nothing, "Sichtbares Drehen des Etikettenformats fehlt")
        Assert(window.FindName("RasterRotationCombo") IsNot Nothing, "DYMO-Treiberdrehung fehlt")
        Assert(window.FindName("QuickMarginCombo") IsNot Nothing, "Direkter Innenrand-Regler fehlt")
        Assert(window.FindName("DecreaseFontSizeButton") IsNot Nothing AndAlso window.FindName("IncreaseFontSizeButton") IsNot Nothing, "Minus/Plus für Schriftgröße fehlt")
        Dim fontSizeInput = DirectCast(window.FindName("FontSizeCombo"), System.Windows.Controls.ComboBox)
        Assert(fontSizeInput.IsEditable, "Schriftgröße kann nicht direkt als Punktwert eingegeben werden")
        Assert(window.FindName("InsertImageButton") IsNot Nothing AndAlso window.FindName("ImageModeCombo") IsNot Nothing, "Logo-/Bildwerkzeuge fehlen")
        Assert(window.FindName("ImageWidthBox") IsNot Nothing AndAlso window.FindName("ImageAlignmentCombo") IsNot Nothing AndAlso window.FindName("ImageOpacityCombo") IsNot Nothing, "Bildgröße, Ausrichtung oder Deckkraft fehlen")
        Assert(window.FindName("ImageFocusToggle") IsNot Nothing AndAlso window.FindName("PreviousImageButton") IsNot Nothing AndAlso window.FindName("NextImageButton") IsNot Nothing, "Dauerhafter Bildfokus oder Mehrbild-Auswahl fehlt")
        Assert(window.FindName("ImageStepBox") IsNot Nothing AndAlso window.FindName("ImageOffsetText") IsNot Nothing, "Punktgenaue Bildpositionierung fehlt")
        Assert(window.FindName("DecreaseMarginButton") IsNot Nothing AndAlso window.FindName("IncreaseMarginButton") IsNot Nothing, "Minus/Plus für Innenrand fehlt")
        Assert(window.FindName("TemplatePanel") IsNot Nothing AndAlso window.FindName("TemplatesCompactButton") IsNot Nothing, "Responsive Vorlagenverwaltung fehlt")
        Assert(window.FindName("SaveTemplateAsButton") IsNot Nothing AndAlso Convert.ToString(DirectCast(window.FindName("SaveTemplateAsButton"), System.Windows.Controls.Button).Content).Contains("Speichern als", StringComparison.Ordinal), "Sichtbares Speichern-als für Vorlagen fehlt")
        Assert(window.FindName("TemplateList") IsNot Nothing AndAlso window.FindName("CompactTemplateList") IsNot Nothing, "Druckerspezifische Vorlagenliste fehlt")
        Assert(window.FindName("CompactTemplatePasteButton") IsNot Nothing AndAlso Not DirectCast(window.FindName("CompactTemplatePasteButton"), FrameworkElement).IsEnabled, "Vorlagen-Paste ist vor Copy fälschlich aktiv")
        Dim opacityInput = DirectCast(window.FindName("ImageOpacityCombo"), System.Windows.Controls.ComboBox)
        Assert(opacityInput.IsEditable, "Bild-Deckkraft kann nicht direkt als Prozentwert eingegeben werden")
        Dim headerBuild = DirectCast(window.FindName("HeaderBuildText"), System.Windows.Controls.TextBlock)
        Assert(Not String.IsNullOrWhiteSpace(headerBuild.Text) AndAlso System.Text.RegularExpressions.Regex.IsMatch(headerBuild.Text, "^v\d+\.\d+\.\d+ · (Build|Entwicklungsbuild)"), "Sichtbare Versions- und Buildkennung fehlt im Header")
        Assert(window.WindowState = WindowState.Maximized, "Programm startet nicht maximiert")
        Dim editor = DirectCast(window.FindName("LabelEditor"), System.Windows.Controls.RichTextBox)
        Assert(editor.IsEnabled AndAlso editor.IsHitTestVisible AndAlso editor.Focusable AndAlso Not editor.IsReadOnly AndAlso editor.IsDocumentEnabled, "Etiketteneditor ist nicht vollständig editierbar")
        Assert(Not System.Windows.Controls.SpellCheck.GetIsEnabled(editor), "Rechtschreibprüfung ist im Etiketteneditor noch aktiv")
        Assert(editor.Padding = New Thickness(0), "Editor verwendet einen vom Drucklayout abweichenden Zusatzrand")
        Dim labelViewbox = DirectCast(window.FindName("LabelViewbox"), System.Windows.Controls.Viewbox)
        Assert(labelViewbox.HorizontalAlignment = HorizontalAlignment.Stretch AndAlso labelViewbox.VerticalAlignment = VerticalAlignment.Stretch, "Etikettenvorschau skaliert nicht proportional in den verfügbaren Platz")
        Dim originalText = LabelDocumentFactory.GetPlainText(editor.Document)
        editor.AppendText(" EDITIERTEST")
        Assert(LabelDocumentFactory.GetPlainText(editor.Document).Length > originalText.Length, "Etiketteneditor nimmt keinen Text an")

        Dim privateFlags = Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic
        Dim supvanEditorDevice = RawSupvan(&H207F, "\\?\hid#vid_1820&pid_207f&mi_00#ui", "SUPVAN")
        Dim supvanDevice = SupvanUsbService.FilterCompatible({supvanEditorDevice}).First()
        Dim supvanDevices = DirectCast(GetType(MainWindow).GetField("_supvanDevices", privateFlags).GetValue(window), ObjectModel.ObservableCollection(Of SupvanUsbDevice))
        supvanDevices.Clear()
        supvanDevices.Add(supvanDevice)
        Dim printerNameBox = DirectCast(window.FindName("PrinterNameBox"), System.Windows.Controls.TextBox)
        Dim queueBox = DirectCast(window.FindName("PrinterQueueBox"), System.Windows.Controls.TextBox)
        Dim supvanUsbCombo = DirectCast(window.FindName("SupvanDeviceCombo"), System.Windows.Controls.ComboBox)
        Dim supvanLanguageCombo = DirectCast(window.FindName("PrinterLanguageCombo"), System.Windows.Controls.ComboBox)
        Dim supvanConnectionCombo = DirectCast(window.FindName("PrinterConnectionModeCombo"), System.Windows.Controls.ComboBox)
        printerNameBox.Text = "Supvan Heli Wien"
        queueBox.Text = ""
        supvanUsbCombo.SelectedItem = supvanDevice
        supvanLanguageCombo.SelectedItem = supvanLanguageCombo.Items.OfType(Of System.Windows.Controls.ComboBoxItem)().First(Function(item) Convert.ToString(item.Tag, Globalization.CultureInfo.InvariantCulture) = PrinterProfile.WindowsRasterLanguage)
        Assert(Convert.ToString(DirectCast(supvanConnectionCombo.SelectedItem, System.Windows.Controls.ComboBoxItem).Tag, Globalization.CultureInfo.InvariantCulture) = PrinterProfile.SupvanUsbConnection, "SUPVAN-Profil bleibt fälschlich im Windows-Druckweg")
        supvanDevices.Clear()
        supvanDevices.Add(supvanDevice)
        supvanUsbCombo.SelectedItem = supvanDevice
        Dim readPrinter = DirectCast(GetType(MainWindow).GetMethod("ReadPrinterEditor", privateFlags).Invoke(window, New Object() {""}), PrinterProfile)
        Assert(readPrinter.UsesSupvanUsb AndAlso readPrinter.Language = PrinterProfile.SupvanRasterLanguage AndAlso String.IsNullOrWhiteSpace(readPrinter.QueueName), "SUPVAN-USB-Profil verlangt noch einen Windows-Druckernamen")
        GetType(MainWindow).GetMethod("UpdateResponsiveTemplateLayout", privateFlags).Invoke(window, Nothing)
        Assert(DirectCast(window.FindName("TemplatePanel"), FrameworkElement).Visibility = Visibility.Visible AndAlso DirectCast(window.FindName("TemplateList"), FrameworkElement).ActualHeight >= 244, "Vorlagenliste bietet nicht zehn sichtbare Zeilen")

        Const wideWidth As Integer = 1660
        Dim wideWindow As New MainWindow With {.Width = wideWidth, .Height = uiHeight, .WindowState = WindowState.Normal}
        Dim wideRoot = DirectCast(wideWindow.Content, FrameworkElement)
        wideRoot.Measure(New Size(wideWidth, uiHeight))
        wideRoot.Arrange(New Rect(0, 0, wideWidth, uiHeight))
        wideRoot.UpdateLayout()
        GetType(MainWindow).GetMethod("UpdateResponsiveTemplateLayout", privateFlags).Invoke(wideWindow, Nothing)
        Assert(DirectCast(wideWindow.FindName("TemplatePanel"), FrameworkElement).Visibility = Visibility.Visible AndAlso DirectCast(wideWindow.FindName("TemplatesCompactButton"), FrameworkElement).Visibility = Visibility.Visible, "Kompakte Schnellliste für Vorlagen fehlt auf breiten Fenstern")
        Dim widePrinter = DirectCast(wideWindow.FindName("TargetPrinterCombo"), System.Windows.Controls.ComboBox).SelectedItem
        Dim widePrinterId = DirectCast(widePrinter, PrinterProfile).Id
        Dim placeholderXaml = DocumentImageCodec.Serialize(LabelDocumentFactory.CreatePatientPlaceholderTemplate())
        Dim syntheticTemplates As New List(Of LabelTemplateDefinition) From {
            New LabelTemplateDefinition With {.Id = "wide-a", .Name = "Patientenetikett mit Logo", .PrinterId = widePrinterId, .ProfileId = "std-50x25", .ProfileName = "Standard 50 x 25", .WidthMm = 50, .HeightMm = 25, .DocumentXaml = placeholderXaml},
            New LabelTemplateDefinition With {.Id = "wide-b", .Name = "Blutröhrchen kompakt", .PrinterId = widePrinterId, .ProfileId = "std-38x25", .ProfileName = "Röhrchen 38 x 25", .WidthMm = 38, .HeightMm = 25, .DocumentXaml = placeholderXaml},
            New LabelTemplateDefinition With {.Id = "other-printer", .Name = "Fremder Drucker", .PrinterId = "nicht-aktiv", .ProfileId = "std-50x30", .DocumentXaml = placeholderXaml}
        }
        GetType(MainWindow).GetField("_allTemplates", privateFlags).SetValue(wideWindow, syntheticTemplates)
        GetType(MainWindow).GetMethod("RefreshTemplatesForPrinter", privateFlags).Invoke(wideWindow, New Object() {"wide-a"})
        Dim visibleTemplates = DirectCast(GetType(MainWindow).GetField("_visibleTemplates", privateFlags).GetValue(wideWindow), ObjectModel.ObservableCollection(Of LabelTemplateDefinition))
        Assert(visibleTemplates.Count = 3, "Globale Vorlagenliste zeigt nicht alle Druckervorlagen")
        For index = 4 To 14
            syntheticTemplates.Add(New LabelTemplateDefinition With {.Id = "list-" & index, .Name = "Testvorlage " & index.ToString("00"), .PrinterId = widePrinterId, .DocumentXaml = placeholderXaml})
        Next
        GetType(MainWindow).GetMethod("RefreshTemplatesForPrinter", privateFlags).Invoke(wideWindow, New Object() {"wide-a"})
        wideRoot.UpdateLayout()
        Dim tenthItem = TryCast(DirectCast(wideWindow.FindName("TemplateList"), System.Windows.Controls.ListBox).ItemContainerGenerator.ContainerFromIndex(9), FrameworkElement)
        Assert(tenthItem IsNot Nothing AndAlso tenthItem.TransformToAncestor(DirectCast(wideWindow.FindName("TemplateList"), FrameworkElement)).Transform(New Point(0, tenthItem.ActualHeight)).Y <= 244, "Zehntes Label ist nicht voll sichtbar")
        GetType(MainWindow).GetMethod("CopyTemplateClick", privateFlags).Invoke(wideWindow, New Object() {Nothing, New RoutedEventArgs()})
        Assert(DirectCast(wideWindow.FindName("CompactTemplatePasteButton"), FrameworkElement).IsEnabled, "Internes Paste wird nach Copy nicht freigeschaltet")
        Dim wideProfileCombo = DirectCast(wideWindow.FindName("ProfileCombo"), System.Windows.Controls.ComboBox)
        Dim targetProfile = wideProfileCombo.Items.OfType(Of LabelProfile)().First(Function(item) item.Id = "std-50x30")
        wideProfileCombo.SelectedItem = targetProfile
        GetType(MainWindow).GetMethod("PasteTemplateClick", privateFlags).Invoke(wideWindow, New Object() {Nothing, New RoutedEventArgs()})
        Assert(wideProfileCombo.SelectedItem Is targetProfile, "Vorlagen-Paste überschreibt das aktuelle Ziel-Etikettenformat")
        Assert(LabelTemplateEngine.ContainsPlaceholders(DirectCast(wideWindow.FindName("LabelEditor"), System.Windows.Controls.RichTextBox).Document), "Vorlageninhalt wird beim programminternen Paste nicht übernommen")
        Dim designToggle = DirectCast(wideWindow.FindName("TemplateDesignToggle"), System.Windows.Controls.Primitives.ToggleButton)
        designToggle.IsChecked = True
        GetType(MainWindow).GetMethod("TemplateDesignModeChanged", privateFlags).Invoke(wideWindow, New Object() {designToggle, New RoutedEventArgs()})
        Assert(DirectCast(wideWindow.FindName("TemplateDesignPanel"), FrameworkElement).Visibility = Visibility.Visible AndAlso DirectCast(wideWindow.FindName("PatientImportPanel"), FrameworkElement).Visibility = Visibility.Collapsed, "Vorlagenmodus zeigt nicht die Platzhalterpalette")
        Dim compactDesignList = DirectCast(wideWindow.FindName("TemplateDesignList"), FrameworkElement)
        Assert(compactDesignList.Visibility = Visibility.Visible, "Kompakte Vorlagenliste ist im Vorlagenmodus nicht sichtbar")
        Assert(DirectCast(wideWindow.FindName("PlaceholderItemsControl"), System.Windows.Controls.ItemsControl).Items.Count = LabelTemplateEngine.Placeholders.Count, "Platzhalterpalette ist nicht vollständig befüllt")
        Assert(DirectCast(wideWindow.FindName("TemplateSaveBar"), FrameworkElement).Visibility = Visibility.Visible AndAlso DirectCast(wideWindow.FindName("PrintBar"), FrameworkElement).Visibility = Visibility.Collapsed, "Vorlagenmodus ersetzt die Druckleiste nicht sicher durch Speichern")
        wideRoot.Measure(New Size(wideWidth, uiHeight))
        wideRoot.Arrange(New Rect(0, 0, wideWidth, uiHeight))
        wideRoot.UpdateLayout()
        Dim toolbarCard = DirectCast(wideWindow.FindName("EditorToolbarCard"), FrameworkElement)
        If Not String.IsNullOrWhiteSpace(renderPath) Then SaveVisual(toolbarCard, Path.Combine(Path.GetDirectoryName(renderPath), "toolbar-detail.png"))
        Assert(toolbarCard.ActualHeight <= 175, $"Werkzeugleiste ist vertikal zu hoch ({toolbarCard.ActualHeight:0.#} px)")
        Dim designLabelStage = DirectCast(wideWindow.FindName("LabelStage"), FrameworkElement)
        Assert(designLabelStage.ActualHeight >= 150, $"Etikettenvorschau wird im Vorlagenmodus verdrängt ({designLabelStage.ActualHeight:0.#} px)")
        Assert(DirectCast(wideWindow.FindName("PlaceholderItemsControl"), FrameworkElement).ActualHeight > 0, "Platzhalterbuttons sind im Vorlagenmodus nicht sichtbar")
        Dim designEditor = DirectCast(wideWindow.FindName("LabelEditor"), System.Windows.Controls.RichTextBox)
        designEditor.Document = LabelDocumentFactory.CreateBaseDocument()
        designEditor.CaretPosition = designEditor.Document.ContentStart
        GetType(MainWindow).GetMethod("InsertPlaceholderClick", privateFlags).Invoke(wideWindow, New Object() {New System.Windows.Controls.Button With {.Tag = "%PLZ%"}, New RoutedEventArgs()})
        Assert(LabelDocumentFactory.GetPlainText(designEditor.Document).Contains("%PLZ%", StringComparison.Ordinal), "Platzhalterbutton fügt nicht an der Cursorposition ein")
        If Not String.IsNullOrWhiteSpace(renderPath) Then
            Dim templateModePath = Path.Combine(Path.GetDirectoryName(renderPath), Path.GetFileNameWithoutExtension(renderPath) & "-template-mode.png")
            wideRoot.UpdateLayout()
            SaveVisual(wideRoot, templateModePath)
        End If
        GetType(MainWindow).GetField("_activeTemplate", privateFlags).SetValue(wideWindow, syntheticTemplates(0))
        Dim firstTemplatePatient As New PatientLabelData With {.Surname = "Erstpatient", .FirstName = "Eva", .InsuranceNumber = "1234010180", .PostalCode = "1010", .City = "Wien", .Street = "Testweg 1", .Phone = "01 111"}
        Dim secondTemplatePatient As New PatientLabelData With {.Surname = "Zweitpatient", .FirstName = "Zara", .InsuranceNumber = "5678020282", .PostalCode = "4020", .City = "Linz", .Street = "Musterweg 2", .Phone = "02 222"}
        GetType(MainWindow).GetMethod("ApplyPatientDataToCurrentTemplate", privateFlags).Invoke(wideWindow, New Object() {firstTemplatePatient, "Dr. Test"})
        GetType(MainWindow).GetMethod("ApplyPatientDataToCurrentTemplate", privateFlags).Invoke(wideWindow, New Object() {secondTemplatePatient, "Dr. Test"})
        Dim secondPatientText = LabelDocumentFactory.GetPlainText(DirectCast(wideWindow.FindName("LabelEditor"), System.Windows.Controls.RichTextBox).Document)
        Assert(secondPatientText.Contains("Zweitpatient Zara", StringComparison.Ordinal) AndAlso Not secondPatientText.Contains("Erstpatient", StringComparison.Ordinal), "Gespeicherte Platzhalterbasis wird beim nächsten Patienten nicht frisch wiederhergestellt")
        Dim storedTemplateMethod = GetType(MainWindow).GetMethod("ApplyPatientDataToStoredTemplate", privateFlags)
        GetType(MainWindow).GetField("_activeTemplate", privateFlags).SetValue(wideWindow, syntheticTemplates(0))
        storedTemplateMethod.Invoke(wideWindow, New Object() {firstTemplatePatient})
        storedTemplateMethod.Invoke(wideWindow, New Object() {secondTemplatePatient})
        Dim legacySecondPatientText = LabelDocumentFactory.GetPlainText(DirectCast(wideWindow.FindName("LabelEditor"), System.Windows.Controls.RichTextBox).Document)
        Dim preservedTemplate = DirectCast(GetType(MainWindow).GetField("_activeTemplate", privateFlags).GetValue(wideWindow), LabelTemplateDefinition)
        Assert(legacySecondPatientText.Contains("Zweitpatient Zara", StringComparison.Ordinal) AndAlso Not legacySecondPatientText.Contains("Erstpatient", StringComparison.Ordinal), "Ältere BrainOffice-Übergabe verwendet beim zweiten Patienten keine frische Platzhalterbasis")
        Assert(preservedTemplate IsNot Nothing AndAlso String.Equals(preservedTemplate.Id, syntheticTemplates(0).Id, StringComparison.Ordinal), "Ältere BrainOffice-Übergabe verliert die aktive Etikettenvorlage")
        Dim printerCollection = DirectCast(GetType(MainWindow).GetField("_printers", privateFlags).GetValue(window), ObjectModel.ObservableCollection(Of PrinterProfile))
        Dim originalTarget = DirectCast(window.FindName("TargetPrinterCombo"), System.Windows.Controls.ComboBox).SelectedItem
        Dim availableProfiles = DirectCast(window.FindName("ProfileCombo"), System.Windows.Controls.ComboBox).Items.OfType(Of LabelProfile)().ToList()
        Dim firstPrinterProfile = availableProfiles.First(Function(profile) profile.Id = "std-50x25")
        Dim secondPrinterProfile = availableProfiles.First(Function(profile) profile.Id = "std-dymo-99012")
        Dim profilePrinterA As New PrinterProfile With {.Id = "ui-profile-a-" & Guid.NewGuid().ToString("N"), .Name = "Testdrucker A", .Host = "192.0.2.31", .PreferredProfileId = firstPrinterProfile.Id}
        Dim profilePrinterB As New PrinterProfile With {.Id = "ui-profile-b-" & Guid.NewGuid().ToString("N"), .Name = "Testdrucker B", .Host = "192.0.2.32", .PreferredProfileId = secondPrinterProfile.Id}
        printerCollection.Add(profilePrinterA)
        printerCollection.Add(profilePrinterB)
        Dim targetPrinterCombo = DirectCast(window.FindName("TargetPrinterCombo"), System.Windows.Controls.ComboBox)
        TestPrinterDisplayBinding(window)
        Dim settingsPrinterList = DirectCast(window.FindName("SettingsPrinterList"), System.Windows.Controls.ListBox)
        Dim settingsProfileList = DirectCast(window.FindName("SettingsProfileList"), System.Windows.Controls.ListBox)
        Dim printerTemplate As New LabelTemplateDefinition With {.Id = "ui-printer-template", .Name = "Vorlage mit Standarddrucker", .PrinterId = profilePrinterA.Id, .PrinterName = profilePrinterA.Name, .ProfileId = firstPrinterProfile.Id, .DocumentXaml = placeholderXaml}
        targetPrinterCombo.SelectedItem = profilePrinterB
        DirectCast(GetType(MainWindow).GetField("_allTemplates", privateFlags).GetValue(window), List(Of LabelTemplateDefinition)).Add(printerTemplate)
        GetType(MainWindow).GetMethod("RefreshTemplatesForPrinter", privateFlags).Invoke(window, New Object() {printerTemplate.Id})
        GetType(MainWindow).GetMethod("SetSelectedTemplate", privateFlags).Invoke(window, New Object() {printerTemplate})
        GetType(MainWindow).GetMethod("LoadSelectedTemplate", privateFlags).Invoke(window, Nothing)
        Assert(targetPrinterCombo.SelectedItem Is profilePrinterA, "Vorlage stellt ihren gespeicherten Standarddrucker nicht ein: " & DirectCast(targetPrinterCombo.SelectedItem, PrinterProfile).Id & " / " & If(DirectCast(GetType(MainWindow).GetField("_activeTemplate", privateFlags).GetValue(window), LabelTemplateDefinition)?.Id, "none"))
        Assert(DirectCast(window.FindName("ActiveTemplateText"), System.Windows.Controls.TextBlock).Text.Contains(printerTemplate.Name, StringComparison.Ordinal), "Aktiver Vorlagenname wird nicht angezeigt")
        Assert(window.FindName("SaveActiveTemplateButton") IsNot Nothing, "Direkter Speichern-Button fehlt")
        targetPrinterCombo.SelectedItem = profilePrinterB
        Assert(targetPrinterCombo.SelectedItem Is profilePrinterB AndAlso ReferenceEquals(GetType(MainWindow).GetField("_activeTemplate", privateFlags).GetValue(window), printerTemplate), "Manueller Druckerwechsel verliert die aktive Vorlage")
        targetPrinterCombo.SelectedItem = profilePrinterA
        settingsPrinterList.SelectedItem = profilePrinterA
        settingsProfileList.SelectedItem = secondPrinterProfile
        Assert(profilePrinterA.PreferredProfileId = secondPrinterProfile.Id AndAlso DirectCast(window.FindName("PrinterProfileCombo"), System.Windows.Controls.ComboBox).SelectedItem Is secondPrinterProfile, "Im Größen-Editor gewähltes Standardetikett wird nicht am Drucker gespeichert")
        settingsPrinterList.SelectedItem = profilePrinterB
        settingsProfileList.SelectedItem = firstPrinterProfile
        targetPrinterCombo.SelectedItem = profilePrinterA
        Assert(DirectCast(window.FindName("ProfileCombo"), System.Windows.Controls.ComboBox).SelectedItem Is secondPrinterProfile, "Beim Wechsel zu Drucker A wird dessen Etikettengröße nicht automatisch gewählt")
        targetPrinterCombo.SelectedItem = profilePrinterB
        Assert(DirectCast(window.FindName("ProfileCombo"), System.Windows.Controls.ComboBox).SelectedItem Is firstPrinterProfile, "Beim Wechsel zu Drucker B wird dessen Etikettengröße nicht automatisch gewählt")
        targetPrinterCombo.SelectedItem = originalTarget
        printerCollection.Remove(profilePrinterA)
        printerCollection.Remove(profilePrinterB)
        GetType(MainWindow).GetMethod("SyncPrintersToSettings", privateFlags).Invoke(window, Nothing)

        ' Entspricht einem über Strg+V eingefügten Bild: Das Image besitzt keinen
        ' beim Dateiimport registrierten Einzel-Handler und besitzt wie ein echter
        ' WPF-Clipboard-Paste zunächst einen festen großen Breiten-/Höhenrahmen.
        Dim pastedBitmap As New WriteableBitmap(160, 80, 96, 96, PixelFormats.Bgra32, Nothing)
        Dim pastedImage As New System.Windows.Controls.Image With {.Source = pastedBitmap, .Width = 1200, .Height = 480, .Stretch = Stretch.Uniform}
        Dim pastedParagraph As New Paragraph()
        pastedParagraph.Inlines.Add(New InlineUIContainer(pastedImage))
        editor.Document.Blocks.Add(pastedParagraph)
        root.UpdateLayout()
        Dim imageMouseArgs As New System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) With {
            .RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent,
            .Source = pastedImage
        }
        GetType(MainWindow).GetMethod("LabelEditorPreviewMouseLeftButtonDown", privateFlags).Invoke(window, New Object() {editor, imageMouseArgs})
        Dim selectedImage = DirectCast(GetType(MainWindow).GetField("_selectedImage", privateFlags).GetValue(window), System.Windows.Controls.Image)
        Assert(selectedImage Is pastedImage AndAlso Not Double.IsNaN(pastedImage.Width), "Per Strg+V eingefügtes Logo wird beim Anklicken nicht dauerhaft ausgewählt")
        Dim imageWidthBox = DirectCast(window.FindName("ImageWidthBox"), System.Windows.Controls.TextBox)
        Assert(Math.Abs(pastedImage.Width - (100.0 / 25.4 * 96.0)) < 0.01 AndAlso imageWidthBox.Text = "100", "Sehr großes Clipboard-Bild wird nicht auf 100 mm Ausgangsbreite normalisiert")
        imageWidthBox.Text = "28"
        DirectCast(window.FindName("ApplyImageButton"), System.Windows.Controls.Button).Focus()
        GetType(MainWindow).GetMethod("ApplyImageSettingsClick", privateFlags).Invoke(window, New Object() {Nothing, New RoutedEventArgs()})
        Assert(Math.Abs(pastedImage.Width - (28.0 / 25.4 * 96.0)) < 0.01, "Logoauswahl geht beim Wechsel vom Editor zum Größen-Button verloren")
        Assert(Double.IsNaN(pastedImage.Height), "Beim Skalieren bleibt der ursprüngliche hohe Clipboard-Bildrahmen bestehen")

        Dim imageFocusToggle = DirectCast(window.FindName("ImageFocusToggle"), System.Windows.Controls.Primitives.ToggleButton)
        Assert(imageFocusToggle.IsChecked.GetValueOrDefault() AndAlso Convert.ToString(imageFocusToggle.Content).Contains("Bild 1/1", StringComparison.Ordinal), "Ausgewähltes Bild wird im dauerhaften Bildfokus nicht eindeutig angezeigt")
        Dim imageStepBox = DirectCast(window.FindName("ImageStepBox"), System.Windows.Controls.TextBox)
        imageStepBox.Text = "2"
        GetType(MainWindow).GetMethod("MoveImageClick", privateFlags).Invoke(window, New Object() {New System.Windows.Controls.Button With {.Tag = "RIGHT"}, New RoutedEventArgs()})
        GetType(MainWindow).GetMethod("MoveImageClick", privateFlags).Invoke(window, New Object() {New System.Windows.Controls.Button With {.Tag = "DOWN"}, New RoutedEventArgs()})
        Dim foregroundTransform = DirectCast(pastedImage.RenderTransform, TranslateTransform)
        Assert(Math.Abs(LabelDocumentFactory.DipToPoints(foregroundTransform.X) - 2) < 0.01 AndAlso Math.Abs(LabelDocumentFactory.DipToPoints(foregroundTransform.Y) - 2) < 0.01, "Bild wird nicht punktgenau horizontal und vertikal verschoben")
        Dim widthBeforeNudge = pastedImage.Width
        GetType(MainWindow).GetMethod("NudgeImageSizeClick", privateFlags).Invoke(window, New Object() {window.FindName("IncreaseImageSizeButton"), New RoutedEventArgs()})
        Assert(Math.Abs(LabelDocumentFactory.DipToPoints(pastedImage.Width - widthBeforeNudge) - 2) < 0.01, "Bildgröße wird nicht in der gewählten Punkt-Schrittweite verändert")
        editor.Focus()
        Assert(imageFocusToggle.IsChecked.GetValueOrDefault(), "Bildfokus geht beim Zurückwechseln in die Textbearbeitung verloren")
        Dim positionedClone = LabelRenderer.CloneDocument(editor.Document)
        Dim clonedPositionedImage = DocumentImageCodec.EnumerateImages(positionedClone).First(Function(item) TypeOf item.RenderTransform Is TranslateTransform)
        Dim clonedTransform = DirectCast(clonedPositionedImage.RenderTransform, TranslateTransform)
        Assert(Math.Abs(clonedTransform.X - foregroundTransform.X) < 0.01 AndAlso Math.Abs(clonedTransform.Y - foregroundTransform.Y) < 0.01, "Bildposition geht im Druck-/Verlaufsdokument verloren")

        Dim secondImage As New System.Windows.Controls.Image With {.Source = pastedBitmap, .Width = 90, .Stretch = Stretch.Uniform}
        pastedParagraph.Inlines.Add(New InlineUIContainer(secondImage))
        root.UpdateLayout()
        GetType(MainWindow).GetMethod("NextImageClick", privateFlags).Invoke(window, New Object() {Nothing, New RoutedEventArgs()})
        Assert(GetType(MainWindow).GetField("_selectedImage", privateFlags).GetValue(window) Is secondImage AndAlso Convert.ToString(imageFocusToggle.Content).Contains("2/2", StringComparison.Ordinal), "Mehrere Bilder lassen sich nicht mit Weiter einzeln auswählen")
        GetType(MainWindow).GetMethod("PreviousImageClick", privateFlags).Invoke(window, New Object() {Nothing, New RoutedEventArgs()})
        Assert(GetType(MainWindow).GetField("_selectedImage", privateFlags).GetValue(window) Is pastedImage, "Mehrere Bilder lassen sich nicht mit Zurück einzeln auswählen")

        Dim imageModeCombo = DirectCast(window.FindName("ImageModeCombo"), System.Windows.Controls.ComboBox)
        imageWidthBox.Text = "180"
        opacityInput.Text = "65 %"
        imageModeCombo.SelectedItem = imageModeCombo.Items.OfType(Of System.Windows.Controls.ComboBoxItem)().First(Function(item) Convert.ToString(item.Tag) = "BACKGROUND")
        GetType(MainWindow).GetMethod("ApplyImageSettingsClick", privateFlags).Invoke(window, New Object() {Nothing, New RoutedEventArgs()})
        Assert(TypeOf editor.Document.Background Is ImageBrush, "Markiertes Clipboard-Logo wird nicht in eine Hintergrundgrafik umgewandelt")
        Dim oversizedBackground = DirectCast(editor.Document.Background, ImageBrush)
        Assert(Math.Abs(oversizedBackground.Viewport.Width - (180.0 / 25.4 * 96.0)) < 0.01 AndAlso oversizedBackground.Viewport.X < 0, "Übergroßer Hintergrund wird nicht über den Etikettenrand hinaus positioniert und beschnitten")
        Assert(Math.Abs(oversizedBackground.Opacity - 0.65) < 0.001, "Frei eingegebene Hintergrund-Deckkraft wird nicht angewendet")
        Assert(GetType(MainWindow).GetField("_selectedImage", privateFlags).GetValue(window) Is Nothing, "In den Hintergrund verschobenes Logo bleibt fälschlich als Textelement markiert")
        Assert(Convert.ToString(imageFocusToggle.Content).Contains("Hintergrund", StringComparison.Ordinal), "Hintergrundgrafik wird im Bildfokus nicht eindeutig angezeigt")
        Assert(DirectCast(window.FindName("BackgroundSelectionBorder"), FrameworkElement).Visibility = Visibility.Visible, "Fokussierte Hintergrundgrafik erhält keinen sichtbaren Auswahlrahmen")
        Dim viewportBeforeMove = oversizedBackground.Viewport
        GetType(MainWindow).GetMethod("MoveImageClick", privateFlags).Invoke(window, New Object() {New System.Windows.Controls.Button With {.Tag = "LEFT"}, New RoutedEventArgs()})
        GetType(MainWindow).GetMethod("MoveImageClick", privateFlags).Invoke(window, New Object() {New System.Windows.Controls.Button With {.Tag = "UP"}, New RoutedEventArgs()})
        Assert(Math.Abs(LabelDocumentFactory.DipToPoints(oversizedBackground.Viewport.X - viewportBeforeMove.X) + 2) < 0.01 AndAlso Math.Abs(LabelDocumentFactory.DipToPoints(oversizedBackground.Viewport.Y - viewportBeforeMove.Y) + 2) < 0.01, "Hintergrundgrafik wird nicht punktgenau verschoben")
        Dim backgroundWidthBeforeNudge = oversizedBackground.Viewport.Width
        GetType(MainWindow).GetMethod("NudgeImageSizeClick", privateFlags).Invoke(window, New Object() {window.FindName("DecreaseImageSizeButton"), New RoutedEventArgs()})
        Assert(Math.Abs(LabelDocumentFactory.DipToPoints(backgroundWidthBeforeNudge - oversizedBackground.Viewport.Width) - 2) < 0.01, "Hintergrundgröße wird nicht in der gewählten Punkt-Schrittweite verändert")

        imageModeCombo.SelectedItem = imageModeCombo.Items.OfType(Of System.Windows.Controls.ComboBoxItem)().First(Function(item) Convert.ToString(item.Tag) = "BLOCK")
        GetType(MainWindow).GetMethod("ApplyImageSettingsClick", privateFlags).Invoke(window, New Object() {Nothing, New RoutedEventArgs()})
        Dim restoredImage = DirectCast(GetType(MainWindow).GetField("_selectedImage", privateFlags).GetValue(window), System.Windows.Controls.Image)
        Assert(restoredImage IsNot Nothing AndAlso TypeOf LogicalTreeHelper.GetParent(restoredImage) Is BlockUIContainer, "Hintergrundgrafik lässt sich nicht wieder als eigene Bildzeile einsetzen")
        Assert(Not TypeOf editor.Document.Background Is ImageBrush, "Hintergrund bleibt nach dem Zurückverschieben doppelt erhalten")

        Dim selectionData As New PatientLabelData With {
            .Surname = "Auswahltest", .FirstName = "Erika", .InsuranceNumber = "1234010180",
            .PostalCode = "1010", .City = "Wien", .Street = "Prüfgasse 1", .Phone = "01 234567"
        }
        editor.Document = LabelDocumentFactory.CreatePatientTemplate(selectionData, "Dr. Max Muster | Testpraxis")
        Dim selectionParagraphs = editor.Document.Blocks.OfType(Of Paragraph)().ToList()
        For iteration = 1 To 40
            Dim firstParagraph = selectionParagraphs(iteration Mod selectionParagraphs.Count)
            Dim lastParagraph = selectionParagraphs((iteration + 2) Mod selectionParagraphs.Count)
            If firstParagraph.ContentStart.CompareTo(lastParagraph.ContentEnd) <= 0 Then
                editor.Selection.Select(firstParagraph.ContentStart, lastParagraph.ContentEnd)
            Else
                editor.Selection.Select(lastParagraph.ContentStart, firstParagraph.ContentEnd)
            End If
        Next
        editor.Selection.Select(editor.Document.ContentStart, editor.Document.ContentEnd)
        Dim lineHeightCombo = DirectCast(window.FindName("LineHeightCombo"), System.Windows.Controls.ComboBox)
        lineHeightCombo.SelectedItem = 8
        Assert(selectionParagraphs.All(Function(paragraph) Math.Abs(LabelDocumentFactory.DipToPoints(paragraph.LineHeight) - 8) < 0.01 AndAlso paragraph.LineStackingStrategy = LineStackingStrategy.BlockLineHeight), "Zeilenhöhe wird nicht sicher absatzweise angewendet")
        editor.Document = LabelDocumentFactory.CreateFreeTemplate()
        root.UpdateLayout()
        Dim printButton = DirectCast(window.FindName("PrintButton"), FrameworkElement)
        Dim printBar = DirectCast(window.FindName("PrintBar"), FrameworkElement)
        Dim printSummary = DirectCast(window.FindName("PrintSummaryText"), FrameworkElement)
        Dim buttonPosition = printButton.TransformToAncestor(root).Transform(New Point(0, 0))
        Assert(printButton.ActualHeight >= 45 AndAlso buttonPosition.Y + printButton.ActualHeight <= uiHeight - 40, "Druckbutton ist bei Mindesthöhe nicht vollständig sichtbar")
        Assert(printButton.ActualHeight <= 70, "Druckbutton wächst durch den Druckerstatus unkontrolliert in die Höhe")
        Assert(printBar.ActualHeight <= 130, "Druckleiste wird bei Mindesthöhe zu hoch")
        Assert(printSummary.ActualWidth >= 300, "Druckerstatus wird in eine unlesbar schmale Spalte gedrängt")

        Const compactWidth As Integer = 1040
        Dim compactWindow As New MainWindow With {.Width = compactWidth, .Height = uiHeight}
        GetType(MainWindow).GetMethod("UpdateResponsiveTemplateLayout", privateFlags).Invoke(compactWindow, Nothing)
        Dim compactRoot = DirectCast(compactWindow.Content, FrameworkElement)
        compactRoot.Measure(New Size(compactWidth, uiHeight))
        compactRoot.Arrange(New Rect(0, 0, compactWidth, uiHeight))
        compactRoot.UpdateLayout()
        Dim compactPrintButton = DirectCast(compactWindow.FindName("PrintButton"), FrameworkElement)
        Dim compactPrintBar = DirectCast(compactWindow.FindName("PrintBar"), FrameworkElement)
        Dim compactPrintSummary = DirectCast(compactWindow.FindName("PrintSummaryText"), FrameworkElement)
        Dim compactLabelStage = DirectCast(compactWindow.FindName("LabelStage"), FrameworkElement)
        If Not String.IsNullOrWhiteSpace(renderPath) Then SaveVisual(compactRoot, Path.Combine(Path.GetDirectoryName(renderPath), "toolbar-compact-detail.png"))
        Assert(compactPrintButton.ActualHeight <= 70, "Druckbutton wächst bei 1040 × 740 unkontrolliert")
        Assert(compactPrintBar.ActualHeight <= 130, "Druckleiste ist bei 1040 × 740 zu hoch")
        Assert(compactPrintSummary.ActualWidth >= 300, "Druckerstatus ist bei 1040 × 740 nicht lesbar")
        Assert(compactLabelStage.ActualHeight >= 140, $"Etikettenvorschau wird bei 1040 × 740 von der Druckleiste verdrängt ({compactLabelStage.ActualHeight:0.#} px)")

        Dim overflowWindow As New MainWindow With {.Width = compactWidth, .Height = uiHeight, .WindowState = WindowState.Normal}
        GetType(MainWindow).GetMethod("UpdateResponsiveTemplateLayout", privateFlags).Invoke(overflowWindow, Nothing)
        Dim overflowEditor = DirectCast(overflowWindow.FindName("LabelEditor"), System.Windows.Controls.RichTextBox)
        Dim overflowDocument = LabelDocumentFactory.CreateBaseDocument()
        For index = 1 To 8
            overflowDocument.Blocks.Add(New Paragraph(New Run("Synthetische Überlaufzeile " & index.ToString())) With {
                .Margin = New Thickness(0), .FontSize = LabelDocumentFactory.PointsToDip(18),
                .LineHeight = LabelDocumentFactory.PointsToDip(20), .LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            })
        Next
        overflowEditor.Document = overflowDocument
        Dim overflowRoot = DirectCast(overflowWindow.Content, FrameworkElement)
        overflowRoot.Measure(New Size(compactWidth, uiHeight))
        overflowRoot.Arrange(New Rect(0, 0, compactWidth, uiHeight))
        overflowRoot.UpdateLayout()
        Dim flags = Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic
        GetType(MainWindow).GetMethod("UpdateLabelDimensions", flags).Invoke(overflowWindow, Nothing)
        GetType(MainWindow).GetMethod("FitTimerTick", flags).Invoke(overflowWindow, New Object() {Nothing, EventArgs.Empty})
        overflowRoot.UpdateLayout()
        Dim overflowPrintButton = DirectCast(overflowWindow.FindName("PrintButton"), System.Windows.Controls.Button)
        Assert(overflowPrintButton.IsEnabled AndAlso Convert.ToString(overflowPrintButton.Content) = "Trotz Überlauf drucken", "Überlauf darf manuellen Druck nicht sperren")
        Assert(DirectCast(overflowWindow.FindName("ContentFitText"), System.Windows.Controls.TextBlock).Text.Contains("Überlauf", StringComparison.Ordinal), "Überlaufhinweis ist nicht direkt sichtbar")
        Dim originalMarkup = DocumentImageCodec.Serialize(overflowEditor.Document)
        Dim testProfile = LabelProfile.StandardProfiles().First(Function(item) item.Id = "std-50x30")
        Dim beforeMarks = LabelRenderer.Render(overflowEditor.Document, testProfile, 203, 1.5, 180)
        DirectCast(overflowWindow.FindName("FormattingMarksToggle"), System.Windows.Controls.Primitives.ToggleButton).IsChecked = True
        overflowRoot.UpdateLayout()
        Dim afterMarks = LabelRenderer.Render(overflowEditor.Document, testProfile, 203, 1.5, 180)
        Assert(beforeMarks.PackedData.SequenceEqual(afterMarks.PackedData) AndAlso DocumentImageCodec.Serialize(overflowEditor.Document) = originalMarkup, "Formatierungszeichen verändern Dokument oder Druckgrafik")
        Assert(afterMarks.WidthDots = 400 AndAlso afterMarks.HeightDots = 240 AndAlso afterMarks.PackedData.Any(Function(value) value <> 0), "Überlaufgrafik ist leer oder überschreitet das Etikettenformat")
        Dim marksEditor As New System.Windows.Controls.RichTextBox With {.Width = 240, .Height = 120, .Padding = New Thickness(0), .BorderThickness = New Thickness(0)}
        Dim marksParagraph As New Paragraph(New Run("A B" & vbTab & "C"))
        marksParagraph.Inlines.Add(New LineBreak())
        marksParagraph.Inlines.Add(New Run("D"))
        marksEditor.Document = New FlowDocument(marksParagraph)
        Dim marksOverlay As New FormattingMarksOverlay With {.Editor = marksEditor, .IsHitTestVisible = False}
        Dim marksGrid As New System.Windows.Controls.Grid With {.Width = 240, .Height = 120}
        marksGrid.Children.Add(marksEditor)
        marksGrid.Children.Add(marksOverlay)
        marksGrid.Measure(New Size(240, 120))
        marksGrid.Arrange(New Rect(0, 0, 240, 120))
        marksGrid.UpdateLayout()
        Dim marksBitmap As New RenderTargetBitmap(240, 120, 96, 96, PixelFormats.Pbgra32)
        marksBitmap.Render(marksGrid)
        Assert(marksOverlay.RenderedMarkCount >= 4, "Leerzeichen, Tab, Zeilenumbruch und Absatzende werden nicht sichtbar gezeichnet")
        If Not String.IsNullOrWhiteSpace(renderPath) Then SaveVisual(marksGrid, Path.Combine(Path.GetDirectoryName(renderPath), "formatting-marks.png"))

        If Not String.IsNullOrWhiteSpace(renderPath) Then
            Dim directoryPath = Path.GetDirectoryName(renderPath)
            If Not String.IsNullOrWhiteSpace(directoryPath) Then Directory.CreateDirectory(directoryPath)
            SaveVisual(root, renderPath)
            Dim templatesPath = Path.Combine(Path.GetDirectoryName(renderPath), Path.GetFileNameWithoutExtension(renderPath) & "-templates.png")
            SaveVisual(wideRoot, templatesPath)
            Dim compactPath = Path.Combine(Path.GetDirectoryName(renderPath), Path.GetFileNameWithoutExtension(renderPath) & "-compact.png")
            SaveVisual(compactRoot, compactPath)
            Dim overflowPath = Path.Combine(Path.GetDirectoryName(renderPath), Path.GetFileNameWithoutExtension(renderPath) & "-overflow.png")
            SaveVisual(overflowRoot, overflowPath)
            Dim settingsWindow As New MainWindow With {.Width = uiWidth, .Height = uiHeight}
            Dim tabs = DirectCast(settingsWindow.FindName("RootTabs"), System.Windows.Controls.TabControl)
            tabs.SelectedIndex = 1
            Dim visualPrinters As New List(Of PrinterProfile)()
            For index = 1 To 8
                visualPrinters.Add(New PrinterProfile With {
                    .Id = "visual-" & index.ToString(),
                    .Name = If(index = 3, "Zebra Probenannahme – sehr langer beschreibender Druckername", "Drucker " & index.ToString()),
                    .Host = "etikettendrucker-" & index.ToString() & ".praxis.intern",
                    .Port = 9100,
                    .Language = If(index Mod 2 = 0, PrinterProfile.ZplLanguage, PrinterProfile.TsplLanguage)
                })
            Next
            DirectCast(settingsWindow.FindName("SettingsPrinterList"), System.Windows.Controls.ListBox).ItemsSource = visualPrinters
            Dim settingsRoot = DirectCast(settingsWindow.Content, FrameworkElement)
            settingsRoot.Measure(New Size(uiWidth, uiHeight))
            settingsRoot.Arrange(New Rect(0, 0, uiWidth, uiHeight))
            settingsRoot.UpdateLayout()
            Dim connectionCombo = DirectCast(settingsWindow.FindName("PrinterConnectionModeCombo"), System.Windows.Controls.ComboBox)
            DirectCast(settingsWindow.FindName("PrinterNameBox"), System.Windows.Controls.TextBox).Text = "Synthetischer Windows-Drucker"
            DirectCast(settingsWindow.FindName("SupvanAliasBox"), System.Windows.Controls.TextBox).Text = ""
            DirectCast(settingsWindow.FindName("SupvanDeviceCombo"), System.Windows.Controls.ComboBox).SelectedItem = Nothing
            connectionCombo.SelectedItem = connectionCombo.Items.OfType(Of System.Windows.Controls.ComboBoxItem)().First(Function(item) Convert.ToString(item.Tag) = PrinterProfile.WindowsQueueConnection)
            Dim languageCombo = DirectCast(settingsWindow.FindName("PrinterLanguageCombo"), System.Windows.Controls.ComboBox)
            languageCombo.SelectedItem = languageCombo.Items.OfType(Of System.Windows.Controls.ComboBoxItem)().First(Function(item) Convert.ToString(item.Tag) = PrinterProfile.WindowsRasterLanguage)
            settingsRoot.UpdateLayout()
            Assert(DirectCast(settingsWindow.FindName("WindowsQueuePanel"), FrameworkElement).Visibility = Visibility.Visible, "Windows-Warteschlangenfeld wird nicht eingeblendet")
            Assert(DirectCast(settingsWindow.FindName("TcpConnectionPanel"), FrameworkElement).Visibility = Visibility.Collapsed, "TCP-Felder bleiben beim Windows-Druckweg sichtbar")
            Assert(Not DirectCast(settingsWindow.FindName("CutterCheck"), System.Windows.Controls.CheckBox).IsEnabled, "Cutter bleibt beim DYMO-Bilddruck aktiv")
            Dim settingsPath = Path.Combine(Path.GetDirectoryName(renderPath), Path.GetFileNameWithoutExtension(renderPath) & "-settings.png")
            SaveVisual(settingsRoot, settingsPath)
            settingsWindow.Close()

            Dim pickerWindow As New WindowsPrinterPickerWindow("\\meli-pc\Zebra1") With {.Width = 720, .Height = 520}
            Dim pickerRoot = DirectCast(pickerWindow.Content, FrameworkElement)
            pickerRoot.Measure(New Size(720, 520))
            pickerRoot.Arrange(New Rect(0, 0, 720, 520))
            pickerRoot.UpdateLayout()
            Dim pickerPath = Path.Combine(Path.GetDirectoryName(renderPath), Path.GetFileNameWithoutExtension(renderPath) & "-printer-picker.png")
            SaveVisual(pickerRoot, pickerPath)
            pickerWindow.Close()

            Dim historyWindow As New MainWindow With {.Width = uiWidth, .Height = uiHeight}
            Dim historyTabs = DirectCast(historyWindow.FindName("RootTabs"), System.Windows.Controls.TabControl)
            historyTabs.SelectedIndex = 2
            Dim historyRoot = DirectCast(historyWindow.Content, FrameworkElement)
            historyRoot.Measure(New Size(uiWidth, uiHeight))
            historyRoot.Arrange(New Rect(0, 0, uiWidth, uiHeight))
            historyRoot.UpdateLayout()
            Dim historyImagePath = Path.Combine(Path.GetDirectoryName(renderPath), Path.GetFileNameWithoutExtension(renderPath) & "-history.png")
            SaveVisual(historyRoot, historyImagePath)
            historyWindow.Close()

            Dim patientWindow As New MainWindow With {.Width = uiWidth, .Height = uiHeight}
            Dim patientProfileCombo = DirectCast(patientWindow.FindName("ProfileCombo"), System.Windows.Controls.ComboBox)
            patientProfileCombo.SelectedItem = DirectCast(patientProfileCombo.ItemsSource, IEnumerable(Of LabelProfile)).First(Function(profile) profile.Id = "std-50x25")
            Dim patientEditor = DirectCast(patientWindow.FindName("LabelEditor"), System.Windows.Controls.RichTextBox)
            patientEditor.Document = LabelDocumentFactory.CreatePatientTemplate(selectionData, "Dr. Max Muster | SOZO Vienna 01-928 4446")
            Dim patientRoot = DirectCast(patientWindow.Content, FrameworkElement)
            patientRoot.Measure(New Size(uiWidth, uiHeight))
            patientRoot.Arrange(New Rect(0, 0, uiWidth, uiHeight))
            patientRoot.UpdateLayout()
            Dim patientImagePath = Path.Combine(Path.GetDirectoryName(renderPath), Path.GetFileNameWithoutExtension(renderPath) & "-patient.png")
            SaveVisual(patientRoot, patientImagePath)
            patientWindow.Close()

            Dim focusWindow As New MainWindow With {.Width = uiWidth, .Height = uiHeight}
            Dim focusEditor = DirectCast(focusWindow.FindName("LabelEditor"), System.Windows.Controls.RichTextBox)
            Dim focusDocument = LabelDocumentFactory.CreateBaseDocument()
            focusDocument.Blocks.Add(New Paragraph(New Run("Text bleibt gleichzeitig editierbar")) With {.Margin = New Thickness(0), .FontSize = LabelDocumentFactory.PointsToDip(9)})
            Dim focusBitmap As New WriteableBitmap(120, 48, 96, 96, PixelFormats.Bgra32, Nothing)
            Dim focusPixels(120 * 48 * 4 - 1) As Byte
            For pixelIndex = 0 To 120 * 48 - 1
                focusPixels(pixelIndex * 4 + 1) = 122
                focusPixels(pixelIndex * 4 + 2) = 240
                focusPixels(pixelIndex * 4 + 3) = 255
            Next
            focusBitmap.WritePixels(New Int32Rect(0, 0, 120, 48), focusPixels, 120 * 4, 0)
            Dim focusImage As New System.Windows.Controls.Image With {.Source = focusBitmap, .Width = 150, .Stretch = Stretch.Uniform}
            Dim focusParagraph As New Paragraph() With {.Margin = New Thickness(0)}
            focusParagraph.Inlines.Add(New InlineUIContainer(focusImage))
            focusDocument.Blocks.Add(focusParagraph)
            focusEditor.Document = focusDocument
            Dim focusRoot = DirectCast(focusWindow.Content, FrameworkElement)
            focusRoot.Measure(New Size(uiWidth, uiHeight))
            focusRoot.Arrange(New Rect(0, 0, uiWidth, uiHeight))
            focusRoot.UpdateLayout()
            GetType(MainWindow).GetMethod("SelectDocumentImage", privateFlags).Invoke(focusWindow, New Object() {focusImage})
            focusRoot.Dispatcher.Invoke(Sub() focusRoot.UpdateLayout(), System.Windows.Threading.DispatcherPriority.ApplicationIdle)
            Assert(GetType(MainWindow).GetField("_selectedImageAdorner", privateFlags).GetValue(focusWindow) IsNot Nothing, "Orangefarbener Auswahlrahmen wird für das fokussierte Bild nicht angezeigt")
            Dim focusImagePath = Path.Combine(Path.GetDirectoryName(renderPath), Path.GetFileNameWithoutExtension(renderPath) & "-image-focus.png")
            SaveVisual(focusRoot, focusImagePath)
            focusWindow.Close()
        End If
        overflowWindow.Close()
        wideWindow.Close()
        compactWindow.Close()
        window.Close()
    End Sub

    Private Sub SaveVisual(root As FrameworkElement, pathValue As String)
        Dim width = Math.Max(1, CInt(root.ActualWidth))
        Dim height = Math.Max(1, CInt(root.ActualHeight))
        Dim bitmap As New RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32)
        bitmap.Render(root)
        Dim encoder As New PngBitmapEncoder()
        encoder.Frames.Add(BitmapFrame.Create(bitmap))
        Using stream = File.Create(pathValue)
            encoder.Save(stream)
        End Using
    End Sub

    Private Sub Assert(condition As Boolean, message As String)
        If Not condition Then Throw New InvalidOperationException("TEST FEHLGESCHLAGEN: " & message)
        _passed += 1
    End Sub
End Module

Imports System.IO
Imports System.Text.Json

Namespace TscLabel.Core
    Public NotInheritable Class SettingsStore
        Friend Shared Property TestPathOverride As String
        Private Shared ReadOnly JsonOptions As New JsonSerializerOptions With {
            .WriteIndented = True,
            .PropertyNameCaseInsensitive = True
        }

        Public Shared ReadOnly Property DefaultPath As String
            Get
                If Not String.IsNullOrWhiteSpace(TestPathOverride) Then Return TestPathOverride
                Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SOZO", "TSC-Etikettendruck", "settings.json")
            End Get
        End Property

        Public Shared Function Load(Optional pathValue As String = Nothing) As AppSettings
            Dim actualPath = If(String.IsNullOrWhiteSpace(pathValue), DefaultPath, pathValue)
            If Not File.Exists(actualPath) Then Return Normalize(New AppSettings())

            Try
                Dim loaded = JsonSerializer.Deserialize(Of AppSettings)(File.ReadAllText(actualPath), JsonOptions)
                Return Normalize(If(loaded, New AppSettings()))
            Catch
                Return Normalize(New AppSettings())
            End Try
        End Function

        Public Shared Sub Save(settings As AppSettings, Optional pathValue As String = Nothing)
            Dim actualPath = If(String.IsNullOrWhiteSpace(pathValue), DefaultPath, pathValue)
            Dim settingsDirectory = Path.GetDirectoryName(actualPath)
            If Not String.IsNullOrWhiteSpace(settingsDirectory) Then System.IO.Directory.CreateDirectory(settingsDirectory)
            Dim temporaryPath = actualPath & ".tmp"
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Normalize(settings), JsonOptions))
            File.Move(temporaryPath, actualPath, True)
        End Sub

        Public Shared Function ApplyDetectedWindowsPrinterPresets(settings As AppSettings,
                                                                  installedPrinters As IEnumerable(Of WindowsPrinterInfo)) As Integer
            If settings Is Nothing OrElse installedPrinters Is Nothing Then Return 0
            settings.Printers = If(settings.Printers, New List(Of PrinterProfile)())
            settings.LastUsedTemplateId = If(settings.LastUsedTemplateId, "").Trim()
            settings.SharedTemplatePath = If(settings.SharedTemplatePath, "").Trim()
            settings.TemplateSaveTarget = If(settings.TemplateSaveTarget, "").Trim().ToUpperInvariant()
            If settings.TemplateSaveTarget <> "SHARED" Then settings.TemplateSaveTarget = "LOCAL"
            Dim changes = 0
            For Each installed In installedPrinters.Where(Function(item) item IsNot Nothing)
                Dim identity = installed.Name & " " & installed.FullName & " " & installed.DriverName
                If Not WindowsPrinterPresetDetector.IsZebraZd410(identity) Then Continue For
                Dim preset = WindowsPrinterPresetDetector.Detect(installed.Name, installed.DriverName)
                If preset Is Nothing Then Continue For
                Dim queueName = If(String.IsNullOrWhiteSpace(installed.FullName), installed.Name, installed.FullName).Trim()
                If queueName = "" Then Continue For
                Dim existing = settings.Printers.FirstOrDefault(Function(printer)
                    Return String.Equals(printer.QueueName, queueName, StringComparison.OrdinalIgnoreCase) OrElse
                           String.Equals(printer.QueueName, installed.Name, StringComparison.OrdinalIgnoreCase)
                End Function)
                If existing Is Nothing Then
                    settings.Printers.Add(New PrinterProfile With {
                        .Id = "printer-zebra-zd410-" & Guid.NewGuid().ToString("N"),
                        .Name = "Zebra ZD410 · 50 × 25",
                        .ConnectionMode = PrinterProfile.WindowsQueueConnection,
                        .QueueName = queueName,
                        .Language = PrinterProfile.ZplLanguage,
                        .Dpi = preset.Dpi,
                        .Density = 8,
                        .Speed = 4,
                        .MarginMm = 1,
                        .PreferredProfileId = "std-zebra-zd410-50x25",
                        .CutterEnabled = False
                    })
                    changes += 1
                End If
            Next
            Return changes
        End Function

        Private Shared Function Normalize(settings As AppSettings) As AppSettings
            If settings.PrintModeSemanticsVersion < 2 Then
                ' Ab Version 2 bedeutet eine nicht gesetzte UI-Checkbox immer den sicheren
                ' Standard: schwarzer Text auf weissem Etikett. Alte Einstellungen hatten
                ' die umgekehrte Checkbox-Semantik und werden genau einmal normalisiert.
                settings.NormalPrint = True
                settings.PrintModeSemanticsVersion = 2
            End If
            settings.PrinterIp = If(settings.PrinterIp, "").Trim()
            If settings.PrinterIp = "" Then settings.PrinterIp = "192.168.1.76"
            settings.PrinterPort = Math.Clamp(settings.PrinterPort, 1, 65535)
            If settings.Dpi <> 203 AndAlso settings.Dpi <> 300 Then settings.Dpi = 203
            settings.Density = Math.Clamp(settings.Density, 0, 15)
            settings.Speed = Math.Clamp(settings.Speed, 1, 8)
            settings.MarginMm = Math.Clamp(settings.MarginMm, 0, 8)
            settings.CustomProfiles = If(settings.CustomProfiles, New List(Of LabelProfile)())
            settings.CustomProfiles = settings.CustomProfiles.Where(Function(p) p IsNot Nothing AndAlso p.WidthMm >= 10 AndAlso p.WidthMm <= 110 AndAlso p.HeightMm >= 10 AndAlso p.HeightMm <= 300).ToList()
            settings.Printers = If(settings.Printers, New List(Of PrinterProfile)())
            settings.Printers = settings.Printers.Where(Function(printer) printer IsNot Nothing).ToList()
            If settings.Printers.Count = 0 Then
                settings.Printers.Add(New PrinterProfile With {
                    .Id = "printer-migrated-tsc",
                    .Name = "TSC Etikettendrucker",
                    .Host = settings.PrinterIp,
                    .Port = settings.PrinterPort,
                    .Language = PrinterProfile.TsplLanguage,
                    .Dpi = settings.Dpi,
                    .Density = settings.Density,
                    .Speed = settings.Speed,
                    .MarginMm = settings.MarginMm,
                    .CutterEnabled = settings.CutterEnabled
                })
            End If
            Dim usedIds As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim validProfileIds As New HashSet(Of String)(LabelProfile.StandardProfiles().Select(Function(profile) profile.Id), StringComparer.OrdinalIgnoreCase)
            For Each profile In settings.CustomProfiles
                validProfileIds.Add(profile.Id)
            Next
            For Each printer In settings.Printers
                printer.Id = If(printer.Id, "").Trim()
                If printer.Id = "" OrElse Not usedIds.Add(printer.Id) Then
                    printer.Id = "printer-" & Guid.NewGuid().ToString("N")
                    usedIds.Add(printer.Id)
                End If
                printer.Name = If(printer.Name, "").Trim()
                If printer.Name = "" Then printer.Name = "Etikettendrucker"
                printer.ConnectionMode = If(
                    String.Equals(printer.ConnectionMode, PrinterProfile.WindowsQueueConnection, StringComparison.OrdinalIgnoreCase),
                    PrinterProfile.WindowsQueueConnection,
                    If(String.Equals(printer.ConnectionMode, PrinterProfile.SupvanUsbConnection, StringComparison.OrdinalIgnoreCase),
                       PrinterProfile.SupvanUsbConnection,
                       PrinterProfile.TcpRawConnection))
                printer.Host = If(printer.Host, "").Trim()
                If printer.Host = "" Then printer.Host = "192.168.1.76"
                printer.Port = Math.Clamp(printer.Port, 1, 65535)
                printer.QueueName = If(printer.QueueName, "").Trim()
                If printer.UsesSupvanUsb Then
                    printer.Language = PrinterProfile.SupvanRasterLanguage
                    printer.Dpi = 203
                    printer.CutterEnabled = False
                    printer.RasterRotationDegrees = 0
                    printer.SupvanDeviceId = If(printer.SupvanDeviceId, "").Trim()
                    printer.SupvanDevicePath = If(printer.SupvanDevicePath, "").Trim()
                    printer.SupvanSerialNumber = If(printer.SupvanSerialNumber, "").Trim()
                    printer.SupvanAlias = If(printer.SupvanAlias, "").Trim()
                    printer.SupvanManufacturer = If(printer.SupvanManufacturer, "").Trim()
                    printer.SupvanProductName = If(printer.SupvanProductName, "").Trim()
                ElseIf String.Equals(printer.Language, PrinterProfile.WindowsRasterLanguage, StringComparison.OrdinalIgnoreCase) Then
                    printer.Language = PrinterProfile.WindowsRasterLanguage
                    printer.ConnectionMode = PrinterProfile.WindowsQueueConnection
                ElseIf String.Equals(printer.Language, PrinterProfile.SupvanRasterLanguage, StringComparison.OrdinalIgnoreCase) Then
                    printer.Language = PrinterProfile.SupvanRasterLanguage
                    printer.ConnectionMode = PrinterProfile.SupvanUsbConnection
                ElseIf String.Equals(printer.Language, PrinterProfile.ZplLanguage, StringComparison.OrdinalIgnoreCase) Then
                    printer.Language = PrinterProfile.ZplLanguage
                Else
                    printer.Language = PrinterProfile.TsplLanguage
                End If
                If printer.Dpi <> 203 AndAlso printer.Dpi <> 300 Then printer.Dpi = 203
                printer.Density = Math.Clamp(printer.Density, 0, 15)
                printer.Speed = Math.Clamp(printer.Speed, 1, 8)
                printer.MarginMm = Math.Clamp(printer.MarginMm, 0, 8)
                printer.PreferredProfileId = If(printer.PreferredProfileId, "").Trim()
                If printer.PreferredProfileId = "" Then
                    Dim printerIdentity = printer.Name & " " & printer.QueueName
                    If printer.UsesWindowsRaster AndAlso WindowsPrinterPresetDetector.IsSupvan(printerIdentity) Then
                        printer.PreferredProfileId = "std-50x30"
                        printer.Dpi = 203
                        printer.RasterRotationDegrees = 0
                    ElseIf printer.UsesWindowsRaster AndAlso (printerIdentity.Contains("BROTHER", StringComparison.OrdinalIgnoreCase) OrElse printerIdentity.Contains("QL-820", StringComparison.OrdinalIgnoreCase) OrElse printerIdentity.Contains("QL820", StringComparison.OrdinalIgnoreCase)) Then
                        printer.PreferredProfileId = "std-brother-dk11201"
                        printer.RasterRotationDegrees = 90
                    ElseIf printer.UsesWindowsRaster AndAlso printerIdentity.Contains("DYMO", StringComparison.OrdinalIgnoreCase) Then
                        printer.PreferredProfileId = "std-dymo-99012"
                        printer.RasterRotationDegrees = 90
                    ElseIf validProfileIds.Contains(settings.SelectedProfileId) Then
                        printer.PreferredProfileId = settings.SelectedProfileId
                    Else
                        printer.PreferredProfileId = "std-50x25"
                    End If
                ElseIf Not validProfileIds.Contains(printer.PreferredProfileId) Then
                    printer.PreferredProfileId = "std-50x25"
                End If
                If printer.RasterRotationDegrees <> 0 AndAlso printer.RasterRotationDegrees <> 90 AndAlso printer.RasterRotationDegrees <> 180 AndAlso printer.RasterRotationDegrees <> 270 Then
                    printer.RasterRotationDegrees = 0
                End If
            Next
            If Not settings.Printers.Any(Function(printer) String.Equals(printer.Id, settings.SelectedPrinterId, StringComparison.OrdinalIgnoreCase)) Then
                settings.SelectedPrinterId = settings.Printers(0).Id
            End If
            Dim selectedPrinter = settings.Printers.First(Function(printer) String.Equals(printer.Id, settings.SelectedPrinterId, StringComparison.OrdinalIgnoreCase))
            settings.PrinterIp = selectedPrinter.Host
            settings.PrinterPort = selectedPrinter.Port
            settings.Dpi = selectedPrinter.Dpi
            settings.Density = selectedPrinter.Density
            settings.Speed = selectedPrinter.Speed
            settings.MarginMm = selectedPrinter.MarginMm
            settings.CutterEnabled = selectedPrinter.CutterEnabled
            If String.IsNullOrWhiteSpace(settings.DownloadDirectory) Then
                settings.DownloadDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            End If
            Return settings
        End Function
    End Class
End Namespace

Imports System.Collections.Concurrent
Imports System.IO
Imports System.Threading
Imports System.Windows
Imports System.Windows.Documents
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports HidSharp
Imports SharpCompress.Compressors.LZMA

Namespace TscLabel.Core
    Public NotInheritable Class SupvanUsbRawDevice
        Public Property DevicePath As String = ""
        Public Property Vid As Integer
        Public Property Pid As Integer
        Public Property InterfaceName As String = ""
        Public Property InputReportLength As Integer
        Public Property OutputReportLength As Integer
        Public Property SerialNumber As String = ""
        Public Property Manufacturer As String = ""
        Public Property ProductName As String = ""
    End Class

    Public NotInheritable Class SupvanUsbDevice
        Public Property TechnicalId As String = ""
        Public Property DisplayName As String = ""
        Public Property DevicePath As String = ""
        Public Property Vid As Integer
        Public Property Pid As Integer
        Public Property SerialNumber As String = ""
        Public Property Manufacturer As String = ""
        Public Property ProductName As String = ""

        Public ReadOnly Property StableIdentity As String
            Get
                If Not String.IsNullOrWhiteSpace(SerialNumber) Then Return $"serial:{Vid:X4}:{Pid:X4}:{SerialNumber.Trim()}"
                Return $"path:{DevicePath}"
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return DisplayName
        End Function
    End Class

    Public NotInheritable Class SupvanUsbSelectionResult
        Public Sub New(selectedDeviceValue As SupvanUsbDevice,
                       availableDevicesValue As IReadOnlyList(Of SupvanUsbDevice),
                       requiresUserChoiceValue As Boolean,
                       messageValue As String)
            SelectedDevice = selectedDeviceValue
            AvailableDevices = availableDevicesValue
            RequiresUserChoice = requiresUserChoiceValue
            Message = messageValue
        End Sub

        Public ReadOnly Property SelectedDevice As SupvanUsbDevice
        Public ReadOnly Property AvailableDevices As IReadOnlyList(Of SupvanUsbDevice)
        Public ReadOnly Property RequiresUserChoice As Boolean
        Public ReadOnly Property Message As String
    End Class

    Public NotInheritable Class SupvanPrintResult
        Public Sub New(widthMmValue As Integer, heightMmValue As Integer, rotationValue As Integer, framesValue As Integer)
            WidthMm = widthMmValue
            HeightMm = heightMmValue
            Rotation = rotationValue
            Frames = framesValue
        End Sub

        Public ReadOnly Property WidthMm As Integer
        Public ReadOnly Property HeightMm As Integer
        Public ReadOnly Property Rotation As Integer
        Public ReadOnly Property Frames As Integer
    End Class

    Public NotInheritable Class SupvanUsbService
        Public Const SupvanVid As Integer = &H1820
        Private Const HidReportLength As Integer = 65
        Private Const RequiredInterface As String = "MI_00"
        Private Const DotsPerMillimeter As Integer = 8
        Private Const PrintheadWidthDots As Integer = 48 * DotsPerMillimeter
        Private Shared ReadOnly PrintLocks As New ConcurrentDictionary(Of String, SemaphoreSlim)(StringComparer.OrdinalIgnoreCase)

        Private Sub New()
        End Sub

        Public Shared Function FilterCompatible(rawDevices As IEnumerable(Of SupvanUsbRawDevice)) As IReadOnlyList(Of SupvanUsbDevice)
            If rawDevices Is Nothing Then Return Array.Empty(Of SupvanUsbDevice)()
            Dim accepted = rawDevices.
                Where(Function(item) IsCompatible(item)).
                GroupBy(Function(item) item.DevicePath, StringComparer.OrdinalIgnoreCase).
                Select(Function(group) group.First()).
                OrderBy(Function(item) item.DevicePath, StringComparer.OrdinalIgnoreCase).
                ToList()

            Dim result As New List(Of SupvanUsbDevice)()
            For index = 0 To accepted.Count - 1
                Dim raw = accepted(index)
                result.Add(New SupvanUsbDevice With {
                    .TechnicalId = raw.DevicePath,
                    .DisplayName = $"SUPVAN {index + 1} (PID {raw.Pid:X4})",
                    .DevicePath = If(raw.DevicePath, "").Trim(),
                    .Vid = raw.Vid,
                    .Pid = raw.Pid,
                    .SerialNumber = If(raw.SerialNumber, "").Trim(),
                    .Manufacturer = If(raw.Manufacturer, "").Trim(),
                    .ProductName = If(raw.ProductName, "").Trim()
                })
            Next
            Return result
        End Function

        Public Shared Function ResolveSelection(configured As PrinterProfile,
                                                availableDevices As IReadOnlyList(Of SupvanUsbDevice)) As SupvanUsbSelectionResult
            Dim devices = If(availableDevices, Array.Empty(Of SupvanUsbDevice)())
            If configured Is Nothing OrElse String.IsNullOrWhiteSpace(configured.SupvanDeviceId) Then
                If devices.Count = 1 Then Return New SupvanUsbSelectionResult(devices(0), devices, False, "Ein SUPVAN-USB-Gerät erkannt und automatisch vorausgewählt.")
                If devices.Count = 0 Then Return New SupvanUsbSelectionResult(Nothing, devices, False, "Kein kompatibles SUPVAN-USB-Gerät angeschlossen.")
                Return New SupvanUsbSelectionResult(Nothing, devices, True, "Mehrere SUPVAN-USB-Geräte erkannt. Bitte eindeutig auswählen.")
            End If

            Dim selected = devices.FirstOrDefault(Function(item) SameConfiguredDevice(configured, item))
            If selected IsNot Nothing Then Return New SupvanUsbSelectionResult(selected, devices, False, "Konfiguriertes SUPVAN-USB-Gerät ist angeschlossen.")

            Dim identityText = If(String.IsNullOrWhiteSpace(configured.SupvanSerialNumber),
                                  "Der gespeicherte Windows-Gerätepfad wurde nicht gefunden. Nach einem USB-Portwechsel bitte das Gerät neu auswählen.",
                                  "Die gespeicherte Seriennummer wurde nicht gefunden.")
            Return New SupvanUsbSelectionResult(Nothing, devices, False, "Das konfigurierte SUPVAN-USB-Gerät ist nicht angeschlossen. " & identityText)
        End Function

        Public Shared Function EnumerateCompatibleDevices() As IReadOnlyList(Of SupvanUsbDevice)
            Return FilterCompatible(EnumerateRawHidDevices())
        End Function

        Public Shared Sub TestSelectedDevice(printer As PrinterProfile)
            If printer Is Nothing Then Throw New ArgumentNullException(NameOf(printer))
            Dim result = ResolveSelection(printer, EnumerateCompatibleDevices())
            If result.SelectedDevice Is Nothing Then Throw New InvalidOperationException(result.Message)
        End Sub

        Public Shared Async Function PrintSyntheticTestAsync(settings As AppSettings, printer As PrinterProfile, cancellationToken As CancellationToken) As Task(Of SupvanPrintResult)
            Dim document = LabelDocumentFactory.CreateBaseDocument()
            document.Blocks.Clear()
            document.Blocks.Add(New Paragraph(New Run("SUPVAN TEST")) With {.FontSize = LabelDocumentFactory.PointsToDip(11), .FontWeight = FontWeights.Bold, .Margin = New Thickness(0)})
            document.Blocks.Add(New Paragraph(New Run(DateTimeOffset.Now.ToLocalTime().ToString("dd.MM.yyyy HH:mm"))) With {.FontSize = LabelDocumentFactory.PointsToDip(7), .Margin = New Thickness(0)})
            Dim profile = ResolveSupvanProfile(printer)
            Return Await PrintAsync(document, profile, settings, printer, 1, cancellationToken)
        End Function

        Public Shared Async Function PrintAsync(document As FlowDocument,
                                               profile As LabelProfile,
                                               settings As AppSettings,
                                               printer As PrinterProfile,
                                               copies As Integer,
                                               cancellationToken As CancellationToken) As Task(Of SupvanPrintResult)
            If document Is Nothing Then Throw New ArgumentNullException(NameOf(document))
            If profile Is Nothing Then Throw New ArgumentNullException(NameOf(profile))
            If settings Is Nothing Then Throw New ArgumentNullException(NameOf(settings))
            If printer Is Nothing Then Throw New ArgumentNullException(NameOf(printer))
            ValidateGeometry(profile)

            Dim selection = ResolveSelection(printer, EnumerateCompatibleDevices())
            Dim selected = selection.SelectedDevice
            If selected Is Nothing Then Throw New InvalidOperationException(selection.Message)

            Dim semaphore = PrintLocks.GetOrAdd(selected.DevicePath, Function(key) New SemaphoreSlim(1, 1))
            Await semaphore.WaitAsync(cancellationToken)
            Try
                Dim renderProfile = profile.CreateOriented(profile.WidthMm = 50 AndAlso profile.HeightMm = 80)
                Dim rendered = LabelRenderer.Render(document, renderProfile, 203, printer.MarginMm, settings.Threshold)
                Dim frames = EncodeFrames(rendered.Preview, settings.NormalPrint, settings.Threshold)
                Dim device = FindHidDevice(selected.DevicePath)
                If device Is Nothing Then Throw New InvalidOperationException("Das konfigurierte SUPVAN-USB-Gerät ist nicht angeschlossen.")
                Using stream = device.Open()
                    stream.ReadTimeout = 2500
                    stream.WriteTimeout = 2500
                    For copyIndex = 1 To Math.Clamp(copies, 1, 99)
                        Await PrintFramesAsync(stream, device, frames, cancellationToken)
                    Next
                End Using
                Return New SupvanPrintResult(CInt(Math.Round(profile.WidthMm)), CInt(Math.Round(profile.HeightMm)), If(profile.WidthMm = 50 AndAlso profile.HeightMm = 80, 90, 0), frames.Count)
            Catch ex As TimeoutException
                Throw New InvalidOperationException("Der SUPVAN T50M Pro antwortet nicht. Bitte USB-Verbindung, Etikettenrolle und geschlossenen Deckel prüfen.", ex)
            Finally
                semaphore.Release()
            End Try
        End Function

        Friend Shared Function EncodeFramesForTest(source As BitmapSource, Optional normalPrint As Boolean = True, Optional threshold As Byte = 128) As IReadOnlyList(Of Byte())
            Return EncodeFrames(source, normalPrint, threshold)
        End Function

        Private Shared Function ResolveSupvanProfile(printer As PrinterProfile) As LabelProfile
            Dim profileId = If(printer?.PreferredProfileId, "")
            Dim profile = LabelProfile.StandardProfiles().FirstOrDefault(Function(item) String.Equals(item.Id, profileId, StringComparison.OrdinalIgnoreCase))
            If profile IsNot Nothing Then Return profile
            Return LabelProfile.StandardProfiles().First(Function(item) item.Id = "std-supvan-40x30")
        End Function

        Private Shared Sub ValidateGeometry(profile As LabelProfile)
            Dim width = CInt(Math.Round(profile.WidthMm))
            Dim height = CInt(Math.Round(profile.HeightMm))
            If width = 40 AndAlso height = 30 Then Return
            If width = 50 AndAlso height = 30 Then Return
            If width = 50 AndAlso height = 80 Then Return
            Throw New ArgumentException("SUPVAN unterstützt 40 x 30 mm, 50 x 30 mm oder 50 x 80 mm mit 90 Grad Drehung.")
        End Sub

        Private Shared Function IsCompatible(item As SupvanUsbRawDevice) As Boolean
            If item Is Nothing Then Return False
            If item.Vid <> SupvanVid Then Return False
            If item.InputReportLength <> HidReportLength OrElse item.OutputReportLength <> HidReportLength Then Return False
            If Not If(item.DevicePath, "").Contains("&mi_00#", StringComparison.OrdinalIgnoreCase) AndAlso
               Not If(item.InterfaceName, "").Contains(RequiredInterface, StringComparison.OrdinalIgnoreCase) Then Return False
            Dim manufacturer = If(item.Manufacturer, "").Trim()
            If manufacturer.Length > 0 AndAlso Not String.Equals(manufacturer, "SUPVAN", StringComparison.OrdinalIgnoreCase) Then Return False
            Return True
        End Function

        Private Shared Function SameConfiguredDevice(configured As PrinterProfile, device As SupvanUsbDevice) As Boolean
            If Not String.IsNullOrWhiteSpace(configured.SupvanSerialNumber) Then
                Return String.Equals(configured.SupvanSerialNumber, device.SerialNumber, StringComparison.OrdinalIgnoreCase) AndAlso
                       configured.SupvanVid = device.Vid AndAlso configured.SupvanPid = device.Pid
            End If
            Return String.Equals(configured.SupvanDevicePath, device.DevicePath, StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(configured.SupvanDeviceId, device.DevicePath, StringComparison.OrdinalIgnoreCase)
        End Function

        Private Shared Function EnumerateRawHidDevices() As IEnumerable(Of SupvanUsbRawDevice)
            Return DeviceList.Local.GetHidDevices(SupvanVid).
                Select(Function(device) New SupvanUsbRawDevice With {
                    .DevicePath = device.DevicePath,
                    .Vid = device.VendorID,
                    .Pid = device.ProductID,
                    .InterfaceName = ExtractInterfaceName(device.DevicePath),
                    .InputReportLength = device.GetMaxInputReportLength(),
                    .OutputReportLength = device.GetMaxOutputReportLength(),
                    .SerialNumber = ReadText(Function() device.GetSerialNumber()),
                    .Manufacturer = ReadText(Function() device.GetManufacturer()),
                    .ProductName = ReadText(Function() device.GetProductName())
                })
        End Function

        Private Shared Function FindHidDevice(devicePath As String) As HidDevice
            Return DeviceList.Local.GetHidDevices(SupvanVid).
                FirstOrDefault(Function(device) String.Equals(device.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase))
        End Function

        Private Shared Function ExtractInterfaceName(devicePath As String) As String
            Dim match = Global.System.Text.RegularExpressions.Regex.Match(If(devicePath, ""), "mi_[0-9a-f]{2}", Global.System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            Return If(match.Success, match.Value.ToUpperInvariant(), "")
        End Function

        Private Shared Function ReadText(read As Func(Of String)) As String
            Try
                Return If(read(), "").Trim()
            Catch
                Return ""
            End Try
        End Function

        Private Shared Function EncodeFrames(bitmap As BitmapSource, normalPrint As Boolean, threshold As Byte) As List(Of Byte())
            Dim source = ToGrayPixels(bitmap)
            Dim bytesPerRow = PrintheadWidthDots \ 8
            Dim sourceStart = Math.Max(0, (source.Width - PrintheadWidthDots) \ 2)
            Dim destinationStart = Math.Max(0, (PrintheadWidthDots - source.Width) \ 2)
            Dim dotsToCopy = Math.Min(source.Width, PrintheadWidthDots)
            Dim rowsPerFrame = (4096 - 22) \ bytesPerRow
            Dim frames As New List(Of Byte())()

            For firstRow = 0 To source.Height - 1 Step rowsPerFrame
                Dim rowCount = Math.Min(rowsPerFrame, source.Height - firstRow)
                Dim raw(4095) As Byte
                Dim isFirst = firstRow = 0
                Dim isLast = firstRow + rowCount >= source.Height
                raw(2) = CByte(If(isFirst, 2, 0) Or If(isLast, 4 Or 8, 0))
                raw(3) = CByte((1 << 6) Or (4 << 2))
                raw(4) = CByte(rowCount And &HFF)
                raw(5) = CByte((rowCount >> 8) And &HFF)
                raw(6) = CByte(bytesPerRow)
                raw(8) = 1
                raw(10) = 1
                For row = 0 To rowCount - 1
                    For x = 0 To dotsToCopy - 1
                        If source.IsBlack(sourceStart + x, firstRow + row, threshold) Xor Not normalPrint Then
                            Dim outputDot = PrintheadWidthDots - 1 - (destinationStart + x)
                            raw(14 + (row * bytesPerRow) + (outputDot \ 8)) = raw(14 + (row * bytesPerRow) + (outputDot \ 8)) Or CByte(1 << (outputDot Mod 8))
                        End If
                    Next
                Next
                Dim usedLength = 14 + (rowCount * bytesPerRow)
                Dim checksum = 0
                For index = 2 To 13
                    checksum += raw(index)
                Next
                For boundary = 255 To usedLength - 1 Step 256
                    checksum += raw(boundary)
                Next
                raw(0) = CByte(checksum And &HFF)
                raw(1) = CByte((checksum >> 8) And &HFF)
                frames.Add(CompressLzma(raw))
            Next
            Return frames
        End Function

        Private Shared Function ToGrayPixels(bitmap As BitmapSource) As SupvanPixelSource
            Dim converted As New FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, Nothing, 0)
            Dim stride = converted.PixelWidth * 4
            Dim pixels(stride * converted.PixelHeight - 1) As Byte
            converted.CopyPixels(pixels, stride, 0)
            Return New SupvanPixelSource(converted.PixelWidth, converted.PixelHeight, stride, pixels)
        End Function

        Private Shared Function CompressLzma(source As Byte()) As Byte()
            Using payload As New MemoryStream()
                Dim properties As New LzmaEncoderProperties(False, 8192)
                Dim header As Byte()
                Using encoder = LzmaStream.Create(properties, False, payload)
                    header = encoder.Properties
                    encoder.Write(source, 0, source.Length)
                End Using
                Using output As New MemoryStream()
                    output.Write(header, 0, header.Length)
                    Dim lengthBytes = BitConverter.GetBytes(CLng(source.Length))
                    output.Write(lengthBytes, 0, lengthBytes.Length)
                    payload.WriteTo(output)
                    Return output.ToArray()
                End Using
            End Using
        End Function

        Private Shared Async Function PrintFramesAsync(stream As HidStream, device As HidDevice, frames As IReadOnlyList(Of Byte()), cancellationToken As CancellationToken) As Task
            Await SendCommandAsync(stream, device, 51, 0, cancellationToken)
            Await SendCommandAsync(stream, device, 53, 4, cancellationToken)
            Await SendCommandAsync(stream, device, 18, 0, cancellationToken)
            Await PollAsync(stream, device, Function(response) Not Bit(response, 3, 2), "Drucker bleibt beschäftigt.", cancellationToken)
            Await SendCommandAsync(stream, device, 19, 0, cancellationToken)
            Await PollAsync(stream, device, Function(response) Bit(response, 4, 6), "Drucker startet nicht.", cancellationToken)
            For Each frame In frames
                Await PollAsync(stream, device, Function(response) Not Bit(response, 2, 0), "Druckpuffer bleibt voll.", cancellationToken)
                Await SendCommandAsync(stream, device, 92, frame.Length, cancellationToken)
                Await TransferAsync(stream, device, frame, cancellationToken)
                Await SendCommandAsync(stream, device, 16, frame.Length, cancellationToken, 60)
            Next
            Await PollAsync(stream, device, Function(response) Not Bit(response, 4, 6), "Druckauftrag wird nicht abgeschlossen.", cancellationToken, 80, 100)
        End Function

        Private Shared Function Bit(response As Byte(), index As Integer, bitIndex As Integer) As Boolean
            Return response.Length > index AndAlso (response(index) And (1 << bitIndex)) <> 0
        End Function

        Private Shared Async Function PollAsync(stream As HidStream,
                                                device As HidDevice,
                                                ready As Func(Of Byte(), Boolean),
                                                timeoutMessage As String,
                                                cancellationToken As CancellationToken,
                                                Optional attempts As Integer = 40,
                                                Optional delayMs As Integer = 100) As Task
            For attempt = 0 To attempts - 1
                Dim status = Await SendCommandAsync(stream, device, 17, 0, cancellationToken)
                ThrowForDeviceError(status)
                If ready(status) Then Return
                Await Task.Delay(delayMs, cancellationToken)
            Next
            Throw New TimeoutException(timeoutMessage)
        End Function

        Friend Shared Sub ThrowForDeviceError(response As Byte())
            If response Is Nothing OrElse response.Length < 9 Then Return
            If Bit(response, 3, 4) Then Throw New InvalidOperationException("SUPVAN-Deckel ist offen.")
            If Bit(response, 3, 5) Then Throw New InvalidOperationException("SUPVAN-Etikettenrolle ist leer.")
            If Bit(response, 3, 7) Then Throw New InvalidOperationException("SUPVAN-Etikettenrolle ist nicht korrekt eingesetzt.")
        End Sub

        Private Shared Function SendCommandAsync(stream As HidStream,
                                                 device As HidDevice,
                                                 command As Integer,
                                                 length As Integer,
                                                 cancellationToken As CancellationToken,
                                                 Optional speed As Integer? = Nothing) As Task(Of Byte())
            Dim bytes(If(speed.HasValue, 9, 7)) As Byte
            bytes(0) = &HC0
            bytes(1) = &H40
            bytes(2) = CByte((length >> 8) And &HFF)
            bytes(3) = CByte(length And &HFF)
            bytes(4) = CByte(command)
            bytes(6) = 8
            If speed.HasValue Then
                bytes(8) = CByte((speed.Value >> 8) And &HFF)
                bytes(9) = CByte(speed.Value And &HFF)
            End If
            Return TransferAsync(stream, device, bytes, cancellationToken)
        End Function

        Private Shared Async Function TransferAsync(stream As HidStream, device As HidDevice, payload As Byte(), cancellationToken As CancellationToken) As Task(Of Byte())
            Dim reportLength = device.GetMaxOutputReportLength()
            Dim packetPayloadLength = reportLength - 1
            For offset = 0 To payload.Length - 1 Step packetPayloadLength
                cancellationToken.ThrowIfCancellationRequested()
                Dim report(reportLength - 1) As Byte
                Array.Copy(payload, offset, report, 1, Math.Min(packetPayloadLength, payload.Length - offset))
                Await stream.WriteAsync(report, 0, report.Length, cancellationToken)
            Next
            Dim response(device.GetMaxInputReportLength() - 1) As Byte
            Dim count = Await stream.ReadAsync(response, 0, response.Length, cancellationToken)
            Return response.Take(count).ToArray()
        End Function

        Private NotInheritable Class SupvanPixelSource
            Public Sub New(widthValue As Integer, heightValue As Integer, strideValue As Integer, pixelsValue As Byte())
                Width = widthValue
                Height = heightValue
                Stride = strideValue
                Pixels = pixelsValue
            End Sub

            Public ReadOnly Property Width As Integer
            Public ReadOnly Property Height As Integer
            Private ReadOnly Property Stride As Integer
            Private ReadOnly Property Pixels As Byte()

            Public Function IsBlack(x As Integer, y As Integer, threshold As Byte) As Boolean
                Dim offset = (y * Stride) + (x * 4)
                Dim blue = CInt(Pixels(offset))
                Dim green = CInt(Pixels(offset + 1))
                Dim red = CInt(Pixels(offset + 2))
                Return ((red * 299 + green * 587 + blue * 114) \ 1000) < CInt(threshold)
            End Function
        End Class
    End Class
End Namespace


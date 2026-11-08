Imports System.Globalization
Imports System.IO
Imports System.Net.Sockets
Imports System.Text
Imports System.Windows.Documents

Namespace TscLabel.Core
    Public NotInheritable Class TsplJobBuilder
        Public Shared Function Build(rendered As LabelRenderResult, profile As LabelProfile, settings As AppSettings, copies As Integer) As Byte()
            Dim legacyPrinter As New PrinterProfile With {
                .Host = settings.PrinterIp,
                .Port = settings.PrinterPort,
                .Dpi = settings.Dpi,
                .Density = settings.Density,
                .Speed = settings.Speed,
                .MarginMm = settings.MarginMm,
                .CutterEnabled = settings.CutterEnabled,
                .Language = PrinterProfile.TsplLanguage
            }
            Return Build(rendered, profile, settings, legacyPrinter, copies)
        End Function

        Public Shared Function Build(rendered As LabelRenderResult, profile As LabelProfile, settings As AppSettings, printer As PrinterProfile, copies As Integer) As Byte()
            If rendered Is Nothing Then Throw New ArgumentNullException(NameOf(rendered))
            If profile Is Nothing Then Throw New ArgumentNullException(NameOf(profile))
            If settings Is Nothing Then Throw New ArgumentNullException(NameOf(settings))
            If printer Is Nothing Then Throw New ArgumentNullException(NameOf(printer))
            copies = Math.Clamp(copies, 1, 999)

            Dim invariant = CultureInfo.InvariantCulture
            Dim header As New StringBuilder()
            header.Append("SIZE ").Append(profile.WidthMm.ToString("0.##", invariant)).Append(" mm,").Append(profile.HeightMm.ToString("0.##", invariant)).Append(" mm").Append(vbCrLf)
            header.Append("GAP ").Append(profile.GapMm.ToString("0.##", invariant)).Append(" mm,0 mm").Append(vbCrLf)
            header.Append("DIRECTION 1").Append(vbCrLf)
            header.Append("REFERENCE 0,0").Append(vbCrLf)
            header.Append("DENSITY ").Append(printer.Density.ToString(invariant)).Append(vbCrLf)
            header.Append("SPEED ").Append(printer.Speed.ToString(invariant)).Append(vbCrLf)
            If printer.CutterEnabled Then header.Append("SET CUTTER BATCH").Append(vbCrLf)
            header.Append("CLS").Append(vbCrLf)
            header.Append("BITMAP 0,0,").Append(rendered.WidthBytes.ToString(invariant)).Append(",").Append(rendered.HeightDots.ToString(invariant)).Append(",0,")

            Dim footer As New StringBuilder(vbCrLf)
            footer.Append("PRINT 1,").Append(copies.ToString(invariant)).Append(vbCrLf)
            If printer.CutterEnabled Then footer.Append("SET CUTTER OFF").Append(vbCrLf)

            Dim headerBytes = Encoding.ASCII.GetBytes(header.ToString())
            Dim footerBytes = Encoding.ASCII.GetBytes(footer.ToString())
            Dim bitmapData = PrepareBitmapData(rendered.PackedData, settings.NormalPrint)
            Dim job(headerBytes.Length + bitmapData.Length + footerBytes.Length - 1) As Byte
            Buffer.BlockCopy(headerBytes, 0, job, 0, headerBytes.Length)
            Buffer.BlockCopy(bitmapData, 0, job, headerBytes.Length, bitmapData.Length)
            Buffer.BlockCopy(footerBytes, 0, job, headerBytes.Length + bitmapData.Length, footerBytes.Length)
            Return job
        End Function

        Private Shared Function PrepareBitmapData(source As Byte(), normalPrint As Boolean) As Byte()
            Dim result = DirectCast(source.Clone(), Byte())
            If normalPrint Then
                For index = 0 To result.Length - 1
                    result(index) = CByte(Not result(index))
                Next
            End If
            Return result
        End Function
    End Class

    Public NotInheritable Class ZplJobBuilder
        Public Shared Function Build(rendered As LabelRenderResult, settings As AppSettings, printer As PrinterProfile, copies As Integer) As Byte()
            If rendered Is Nothing Then Throw New ArgumentNullException(NameOf(rendered))
            If settings Is Nothing Then Throw New ArgumentNullException(NameOf(settings))
            If printer Is Nothing Then Throw New ArgumentNullException(NameOf(printer))
            copies = Math.Clamp(copies, 1, 999)

            ' In ZPL bedeutet ein gesetztes Grafikbit einen schwarzen Druckpunkt. Die bestehende
            ' TSC-Inverslogik ist druckersprachenspezifisch und wird hier deshalb gespiegelt.
            Dim bitmapData = DirectCast(rendered.PackedData.Clone(), Byte())
            If Not settings.NormalPrint Then
                For index = 0 To bitmapData.Length - 1
                    bitmapData(index) = CByte(Not bitmapData(index))
                Next
            End If

            Dim zpl As New StringBuilder(bitmapData.Length * 2 + 220)
            zpl.Append("^XA").Append(vbLf)
            zpl.Append("^PW").Append(rendered.WidthDots.ToString(CultureInfo.InvariantCulture)).Append(vbLf)
            zpl.Append("^LL").Append(rendered.HeightDots.ToString(CultureInfo.InvariantCulture)).Append(vbLf)
            zpl.Append("^LH0,0").Append(vbLf)
            If printer.CutterEnabled Then zpl.Append("^MMC").Append(vbLf)
            Const maximumGraphicBytes As Integer = 99999
            Dim maximumRows = Math.Max(1, maximumGraphicBytes \ rendered.WidthBytes)
            Dim rowOffset = 0
            While rowOffset < rendered.HeightDots
                Dim rows = Math.Min(maximumRows, rendered.HeightDots - rowOffset)
                Dim chunkLength = rows * rendered.WidthBytes
                Dim dataOffset = rowOffset * rendered.WidthBytes
                zpl.Append("^FO0,").Append(rowOffset.ToString(CultureInfo.InvariantCulture)).Append("^GFA,")
                zpl.Append(chunkLength.ToString(CultureInfo.InvariantCulture)).Append(",")
                zpl.Append(chunkLength.ToString(CultureInfo.InvariantCulture)).Append(",")
                zpl.Append(rendered.WidthBytes.ToString(CultureInfo.InvariantCulture)).Append(",")
                For index = dataOffset To dataOffset + chunkLength - 1
                    zpl.Append(bitmapData(index).ToString("X2", CultureInfo.InvariantCulture))
                Next
                zpl.Append("^FS").Append(vbLf)
                rowOffset += rows
            End While
            zpl.Append("^PQ").Append(copies.ToString(CultureInfo.InvariantCulture)).Append(",0,1,N").Append(vbLf)
            zpl.Append("^XZ").Append(vbLf)
            If printer.CutterEnabled Then zpl.Append("^XA^MMT^XZ").Append(vbLf)
            Return Encoding.ASCII.GetBytes(zpl.ToString())
        End Function
    End Class

    Public NotInheritable Class RawLabelPrinter
        Public Shared Async Function TestConnectionAsync(printer As PrinterProfile, cancellationToken As Threading.CancellationToken) As Task
            If printer Is Nothing Then Throw New ArgumentNullException(NameOf(printer))
            If printer.UsesSupvanUsb Then
                cancellationToken.ThrowIfCancellationRequested()
                Await Task.Run(Sub() SupvanUsbService.TestSelectedDevice(printer), cancellationToken)
                Return
            End If
            If printer.UsesWindowsQueue Then
                cancellationToken.ThrowIfCancellationRequested()
                Await Task.Run(Sub() WindowsPrinterService.TestQueue(printer.QueueName), cancellationToken)
                Return
            End If
            Using client As New TcpClient()
                Await client.ConnectAsync(printer.Host, printer.Port, cancellationToken)
            End Using
        End Function

        Public Shared Async Function PrintAsync(document As FlowDocument, profile As LabelProfile, settings As AppSettings, printer As PrinterProfile, copies As Integer, cancellationToken As Threading.CancellationToken) As Task(Of LabelRenderResult)
            If printer Is Nothing Then Throw New ArgumentNullException(NameOf(printer))
            Dim rendered = LabelRenderer.Render(document, profile, printer.Dpi, printer.MarginMm, settings.Threshold)
            If printer.UsesSupvanUsb Then
                Await SupvanUsbService.PrintAsync(document, profile, settings, printer, copies, cancellationToken)
                Return rendered
            End If
            If printer.UsesWindowsRaster Then
                cancellationToken.ThrowIfCancellationRequested()
                Return WindowsPrinterService.PrintRaster(rendered, profile, settings, printer, copies)
            End If
            Dim job As Byte()
            If String.Equals(printer.Language, PrinterProfile.ZplLanguage, StringComparison.OrdinalIgnoreCase) Then
                job = ZplJobBuilder.Build(rendered, settings, printer, copies)
            Else
                job = TsplJobBuilder.Build(rendered, profile, settings, printer, copies)
            End If

            If printer.UsesWindowsQueue Then
                Await WindowsPrinterService.SendRawAsync(printer.QueueName, job, $"Etikettenstudio · {printer.Name}", cancellationToken)
            Else
                Using client As New TcpClient()
                    Await client.ConnectAsync(printer.Host, printer.Port, cancellationToken)
                    Using stream = client.GetStream()
                        Await stream.WriteAsync(job.AsMemory(), cancellationToken)
                        Await stream.FlushAsync(cancellationToken)
                    End Using
                End Using
            End If
            Return rendered
        End Function
    End Class

    Public NotInheritable Class TscRawPrinter
        Public Shared Async Function TestConnectionAsync(host As String, port As Integer, cancellationToken As Threading.CancellationToken) As Task
            Using client As New TcpClient()
                Await client.ConnectAsync(host, port, cancellationToken)
            End Using
        End Function

        Public Shared Async Function PrintAsync(document As FlowDocument, profile As LabelProfile, settings As AppSettings, copies As Integer, cancellationToken As Threading.CancellationToken) As Task(Of LabelRenderResult)
            Dim legacyPrinter As New PrinterProfile With {
                .Host = settings.PrinterIp, .Port = settings.PrinterPort, .Language = PrinterProfile.TsplLanguage,
                .Dpi = settings.Dpi, .Density = settings.Density, .Speed = settings.Speed,
                .MarginMm = settings.MarginMm, .CutterEnabled = settings.CutterEnabled
            }
            Return Await RawLabelPrinter.PrintAsync(document, profile, settings, legacyPrinter, copies, cancellationToken)
        End Function
    End Class
End Namespace

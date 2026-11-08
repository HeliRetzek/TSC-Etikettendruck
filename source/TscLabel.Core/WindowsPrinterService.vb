Imports System.ComponentModel
Imports System.IO
Imports System.Printing
Imports System.Runtime.InteropServices
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Documents
Imports System.Windows.Markup
Imports System.Windows.Media
Imports System.Windows.Media.Imaging

Namespace TscLabel.Core
    Public NotInheritable Class WindowsPrinterInfo
        Public Property Name As String = ""
        Public Property FullName As String = ""
        Public Property DriverName As String = ""
        Public Property IsDefault As Boolean

        Public Overrides Function ToString() As String
            Return If(IsDefault, $"{FullName}  ·  Standarddrucker", FullName)
        End Function
    End Class

    Public NotInheritable Class WindowsPrinterService
        Public Shared Function GetInstalledPrinters() As IReadOnlyList(Of WindowsPrinterInfo)
            Dim result As New List(Of WindowsPrinterInfo)()
            Using server As New LocalPrintServer()
                Dim defaultQueueName = ""
                Try
                    defaultQueueName = server.DefaultPrintQueue?.FullName
                Catch
                    ' Ein fehlender Standarddrucker darf die Liste nicht verhindern.
                End Try

                Dim queueTypes = New EnumeratedPrintQueueTypes() {
                    EnumeratedPrintQueueTypes.Local,
                    EnumeratedPrintQueueTypes.Connections
                }
                For Each queue In server.GetPrintQueues(queueTypes)
                    Try
                        Dim fullName = If(String.IsNullOrWhiteSpace(queue.FullName), queue.Name, queue.FullName)
                        result.Add(New WindowsPrinterInfo With {
                            .Name = queue.Name,
                            .FullName = fullName,
                            .DriverName = If(queue.QueueDriver?.Name, ""),
                            .IsDefault = String.Equals(fullName, defaultQueueName, StringComparison.OrdinalIgnoreCase)
                        })
                    Catch
                        ' Fehlerhafte/offline Warteschlangen werden beim Auflisten uebersprungen.
                    End Try
                Next
            End Using
            Return result.OrderByDescending(Function(item) item.IsDefault).
                ThenBy(Function(item) item.FullName, StringComparer.CurrentCultureIgnoreCase).ToList()
        End Function

        Public Shared Sub TestQueue(queueName As String)
            WindowsRawSpooler.TestQueue(queueName)
        End Sub

        Public Shared Async Function SendRawAsync(queueName As String, data As Byte(), documentName As String, cancellationToken As System.Threading.CancellationToken) As Task
            cancellationToken.ThrowIfCancellationRequested()
            Await Task.Run(Sub() WindowsRawSpooler.Send(queueName, data, documentName, cancellationToken), cancellationToken)
        End Function

        Public Shared Function PrintRaster(rendered As LabelRenderResult, profile As LabelProfile, settings As AppSettings, printer As PrinterProfile, copies As Integer) As LabelRenderResult
            If rendered Is Nothing Then Throw New ArgumentNullException(NameOf(rendered))
            If profile Is Nothing Then Throw New ArgumentNullException(NameOf(profile))
            If settings Is Nothing Then Throw New ArgumentNullException(NameOf(settings))
            If printer Is Nothing Then Throw New ArgumentNullException(NameOf(printer))
            If String.IsNullOrWhiteSpace(printer.QueueName) Then Throw New InvalidOperationException("Der Windows-Druckername fehlt.")

            Using server As New LocalPrintServer()
                Using queue = FindQueue(server, printer.QueueName)
                    Dim writer = PrintQueue.CreateXpsDocumentWriter(queue)
                    Dim document As New FixedDocument()
                    Dim pageSize = GetRasterPageSize(profile, printer.RasterRotationDegrees)
                    Dim pageWidth = pageSize.Width
                    Dim pageHeight = pageSize.Height
                    document.DocumentPaginator.PageSize = pageSize
                    Dim bitmap = If(settings.NormalPrint, rendered.Preview, InvertBitmap(rendered.Preview))
                    bitmap = RotateBitmap(bitmap, printer.RasterRotationDegrees)

                    For copyIndex = 1 To Math.Clamp(copies, 1, 999)
                        Dim page As New FixedPage With {
                            .Width = pageWidth,
                            .Height = pageHeight,
                            .Background = Brushes.White
                        }
                        Dim image As New Image With {
                            .Source = bitmap,
                            .Width = pageWidth,
                            .Height = pageHeight,
                            .Stretch = Stretch.Fill
                        }
                        FixedPage.SetLeft(image, 0)
                        FixedPage.SetTop(image, 0)
                        page.Children.Add(image)
                        Dim pageContent As New PageContent()
                        DirectCast(pageContent, IAddChild).AddChild(page)
                        document.Pages.Add(pageContent)
                    Next

                    Dim ticket = If(queue.DefaultPrintTicket?.Clone(), New PrintTicket())
                    ticket.PageOrientation = If(pageWidth <= pageHeight, PageOrientation.Portrait, PageOrientation.Landscape)
                    ticket.PageMediaSize = New PageMediaSize(pageWidth, pageHeight)
                    ticket.PageResolution = New PageResolution(printer.Dpi, printer.Dpi)
                    writer.Write(document, ticket)
                End Using
            End Using
            Return rendered
        End Function

        Public Shared Function GetRasterPageSize(profile As LabelProfile, rotationDegrees As Integer) As Size
            If profile Is Nothing Then Throw New ArgumentNullException(NameOf(profile))
            Dim width = profile.WidthMm / 25.4 * 96.0
            Dim height = profile.HeightMm / 25.4 * 96.0
            Dim rotation = NormalizeRotation(rotationDegrees)
            If rotation = 90 OrElse rotation = 270 Then Return New Size(height, width)
            Return New Size(width, height)
        End Function

        Private Shared Function NormalizeRotation(rotationDegrees As Integer) As Integer
            Select Case rotationDegrees
                Case 90, 180, 270
                    Return rotationDegrees
                Case Else
                    Return 0
            End Select
        End Function

        Private Shared Function RotateBitmap(source As BitmapSource, rotationDegrees As Integer) As BitmapSource
            Dim rotation = NormalizeRotation(rotationDegrees)
            If rotation = 0 Then Return source
            Dim transformed As New TransformedBitmap(source, New RotateTransform(rotation))
            transformed.Freeze()
            Return transformed
        End Function

        Private Shared Function FindQueue(server As LocalPrintServer, queueName As String) As PrintQueue
            Dim queueTypes = New EnumeratedPrintQueueTypes() {
                EnumeratedPrintQueueTypes.Local,
                EnumeratedPrintQueueTypes.Connections
            }
            For Each queue In server.GetPrintQueues(queueTypes)
                If String.Equals(queue.FullName, queueName, StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(queue.Name, queueName, StringComparison.OrdinalIgnoreCase) Then
                    Return queue
                End If
                queue.Dispose()
            Next
            Throw New InvalidOperationException($"Der Windows-Drucker ‚{queueName}‘ ist auf diesem Computer nicht installiert oder verbunden.")
        End Function

        Private Shared Function InvertBitmap(source As BitmapSource) As BitmapSource
            Dim converted As New FormatConvertedBitmap(source, PixelFormats.Pbgra32, Nothing, 0)
            Dim stride = converted.PixelWidth * 4
            Dim pixels(stride * converted.PixelHeight - 1) As Byte
            converted.CopyPixels(pixels, stride, 0)
            For index = 0 To pixels.Length - 4 Step 4
                pixels(index) = CByte(255 - pixels(index))
                pixels(index + 1) = CByte(255 - pixels(index + 1))
                pixels(index + 2) = CByte(255 - pixels(index + 2))
            Next
            Dim result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Pbgra32, Nothing, pixels, stride)
            result.Freeze()
            Return result
        End Function
    End Class

    Friend NotInheritable Class WindowsRawSpooler
        <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
        Private Structure DocInfo1
            <MarshalAs(UnmanagedType.LPWStr)>
            Public DocumentName As String
            <MarshalAs(UnmanagedType.LPWStr)>
            Public OutputFile As String
            <MarshalAs(UnmanagedType.LPWStr)>
            Public DataType As String
        End Structure

        <DllImport("winspool.drv", EntryPoint:="OpenPrinterW", SetLastError:=True, CharSet:=CharSet.Unicode)>
        Private Shared Function OpenPrinter(printerName As String, ByRef printerHandle As IntPtr, defaults As IntPtr) As Boolean
        End Function

        <DllImport("winspool.drv", SetLastError:=True)>
        Private Shared Function ClosePrinter(printerHandle As IntPtr) As Boolean
        End Function

        <DllImport("winspool.drv", EntryPoint:="StartDocPrinterW", SetLastError:=True, CharSet:=CharSet.Unicode)>
        Private Shared Function StartDocPrinter(printerHandle As IntPtr, level As Integer, ByRef documentInfo As DocInfo1) As Integer
        End Function

        <DllImport("winspool.drv", SetLastError:=True)>
        Private Shared Function EndDocPrinter(printerHandle As IntPtr) As Boolean
        End Function

        <DllImport("winspool.drv", SetLastError:=True)>
        Private Shared Function StartPagePrinter(printerHandle As IntPtr) As Boolean
        End Function

        <DllImport("winspool.drv", SetLastError:=True)>
        Private Shared Function EndPagePrinter(printerHandle As IntPtr) As Boolean
        End Function

        <DllImport("winspool.drv", SetLastError:=True)>
        Private Shared Function WritePrinter(printerHandle As IntPtr, data As Byte(), count As Integer, ByRef written As Integer) As Boolean
        End Function

        Public Shared Sub TestQueue(queueName As String)
            Dim handle As IntPtr = IntPtr.Zero
            If String.IsNullOrWhiteSpace(queueName) Then Throw New InvalidOperationException("Der Windows-Druckername fehlt.")
            If Not OpenPrinter(queueName, handle, IntPtr.Zero) Then ThrowLastWin32Error($"Windows-Drucker ‚{queueName}‘ konnte nicht geöffnet werden.")
            ClosePrinter(handle)
        End Sub

        Public Shared Sub Send(queueName As String, data As Byte(), documentName As String, cancellationToken As System.Threading.CancellationToken)
            If data Is Nothing OrElse data.Length = 0 Then Throw New InvalidOperationException("Der RAW-Druckauftrag ist leer.")
            Dim handle As IntPtr = IntPtr.Zero
            Dim documentStarted = False
            Dim pageStarted = False
            Try
                If Not OpenPrinter(queueName, handle, IntPtr.Zero) Then ThrowLastWin32Error($"Windows-Drucker ‚{queueName}‘ konnte nicht geöffnet werden.")
                cancellationToken.ThrowIfCancellationRequested()
                Dim info As New DocInfo1 With {
                    .DocumentName = If(String.IsNullOrWhiteSpace(documentName), "Etikettenstudio RAW", documentName),
                    .OutputFile = Nothing,
                    .DataType = "RAW"
                }
                If StartDocPrinter(handle, 1, info) = 0 Then ThrowLastWin32Error("Windows konnte den RAW-Druckauftrag nicht beginnen.")
                documentStarted = True
                If Not StartPagePrinter(handle) Then ThrowLastWin32Error("Windows konnte die Druckseite nicht beginnen.")
                pageStarted = True
                Dim written As Integer
                If Not WritePrinter(handle, data, data.Length, written) Then ThrowLastWin32Error("Windows konnte die RAW-Daten nicht an die Warteschlange schreiben.")
                If written <> data.Length Then Throw New IOException($"Windows hat nur {written} von {data.Length} RAW-Bytes übernommen.")
            Finally
                If pageStarted Then EndPagePrinter(handle)
                If documentStarted Then EndDocPrinter(handle)
                If handle <> IntPtr.Zero Then ClosePrinter(handle)
            End Try
        End Sub

        Private Shared Sub ThrowLastWin32Error(message As String)
            Throw New Win32Exception(Marshal.GetLastWin32Error(), message)
        End Sub
    End Class
End Namespace

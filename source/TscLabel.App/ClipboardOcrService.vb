Imports System.IO
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports Windows.Graphics.Imaging
Imports Windows.Media.Ocr
Imports Windows.Storage.Streams

Namespace TscLabel.App
    Public NotInheritable Class ClipboardOcrService
        Public Shared Async Function RecognizeAsync(image As BitmapSource) As Task(Of String)
            If image Is Nothing Then Throw New ArgumentNullException(NameOf(image))
            Dim engine = OcrEngine.TryCreateFromUserProfileLanguages()
            If engine Is Nothing Then Throw New InvalidOperationException("Für die eingestellte Windows-Sprache ist keine OCR-Unterstützung installiert.")

            ' CF_DIB/Clipboard-Bitmaps enthalten häufig einen bedeutungslosen Alpha-Kanal
            ' (alle Pixel Alpha=0), obwohl die RGB-Pixel sichtbar und korrekt sind. PNG würde
            ' daraus ein transparentes, für OCR leeres Bild machen. Bgr32 verwirft Alpha bewusst.
            Dim opaqueImage = CreateOpaqueBgr32(image)

            Using encoded As New MemoryStream()
                Dim encoder As New PngBitmapEncoder()
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(opaqueImage))
                encoder.Save(encoded)
                encoded.Position = 0

                Using randomStream As New InMemoryRandomAccessStream()
                    Using output = randomStream.GetOutputStreamAt(0)
                        Await encoded.CopyToAsync(output.AsStreamForWrite())
                        Await output.FlushAsync()
                    End Using
                    randomStream.Seek(0)
                    Dim decoder = Await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(randomStream)
                    Using softwareBitmap = Await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore)
                        Dim result = Await engine.RecognizeAsync(softwareBitmap)
                        Return String.Join(Environment.NewLine, result.Lines.Select(Function(line) line.Text))
                    End Using
                End Using
            End Using
        End Function

        Private Shared Function CreateOpaqueBgr32(source As BitmapSource) As BitmapSource
            Dim bgra As BitmapSource = source
            If source.Format <> PixelFormats.Bgra32 Then
                bgra = New FormatConvertedBitmap(source, PixelFormats.Bgra32, Nothing, 0)
            End If

            Dim stride = source.PixelWidth * 4
            Dim pixels(stride * source.PixelHeight - 1) As Byte
            bgra.CopyPixels(pixels, stride, 0)
            Dim dpiX = If(source.DpiX > 0, source.DpiX, 96.0)
            Dim dpiY = If(source.DpiY > 0, source.DpiY, 96.0)
            Dim opaque = BitmapSource.Create(source.PixelWidth, source.PixelHeight, dpiX, dpiY, PixelFormats.Bgr32, Nothing, pixels, stride)
            opaque.Freeze()
            Return opaque
        End Function
    End Class
End Namespace

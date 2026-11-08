Imports System.IO
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Documents
Imports System.Windows.Markup
Imports System.Windows.Media
Imports System.Windows.Media.Imaging

Namespace TscLabel.Core
    Public NotInheritable Class LabelRenderer
        Public Shared Function GetPageCount(document As FlowDocument, profile As LabelProfile, marginMm As Double) As Integer
            If document Is Nothing Then Return 0
            Dim renderDocument = CloneDocument(document)
            RemoveTrailingEmptyParagraphs(renderDocument)
            ApplyPageLayout(renderDocument, profile, marginMm)
            Dim widthDip = renderDocument.PageWidth
            Dim heightDip = renderDocument.PageHeight
            Dim paginator = DirectCast(renderDocument, IDocumentPaginatorSource).DocumentPaginator
            paginator.PageSize = New Size(widthDip, heightDip)
            paginator.ComputePageCount()
            If paginator.PageCount <= 1 Then Return paginator.PageCount

            ' Ein ueberlaufender leerer Schlussabsatz ist kein abgeschnittener Druckinhalt.
            ' Gezaehlt wird deshalb die letzte Seite, auf der tatsaechlich Druckpunkte liegen.
            Dim lastVisiblePage = 0
            For pageIndex = 0 To paginator.PageCount - 1
                If PageContainsInk(paginator.GetPage(pageIndex), widthDip, heightDip) Then lastVisiblePage = pageIndex + 1
            Next
            Return Math.Max(1, lastVisiblePage)
        End Function

        Public Shared Sub ApplyPageLayout(document As FlowDocument, profile As LabelProfile, marginMm As Double)
            If document Is Nothing Then Throw New ArgumentNullException(NameOf(document))
            If profile Is Nothing Then Throw New ArgumentNullException(NameOf(profile))
            Dim widthDip = profile.WidthMm / 25.4 * 96.0
            Dim heightDip = profile.HeightMm / 25.4 * 96.0
            Dim marginDip = Math.Max(0, marginMm) / 25.4 * 96.0
            document.PageWidth = widthDip
            document.PageHeight = heightDip
            document.PagePadding = New Thickness(marginDip)
            document.ColumnWidth = Math.Max(1, widthDip - 2 * marginDip)
            document.ColumnGap = 0
        End Sub

        Public Shared Function Render(document As FlowDocument, profile As LabelProfile, dpi As Integer, marginMm As Double, threshold As Byte) As LabelRenderResult
            If document Is Nothing Then Throw New ArgumentNullException(NameOf(document))
            If profile Is Nothing Then Throw New ArgumentNullException(NameOf(profile))
            If dpi <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(dpi))

            Dim widthDots = Math.Max(1, CInt(Math.Round(profile.WidthMm / 25.4 * dpi)))
            Dim heightDots = Math.Max(1, CInt(Math.Round(profile.HeightMm / 25.4 * dpi)))
            Dim widthBytes = CInt(Math.Ceiling(widthDots / 8.0))
            Dim widthDip = profile.WidthMm / 25.4 * 96.0
            Dim heightDip = profile.HeightMm / 25.4 * 96.0

            Dim renderDocument = CloneDocument(document)
            ApplyPageLayout(renderDocument, profile, marginMm)

            ' Match the continuous editor viewport and crop at the label edge instead of paginating paragraphs.
            Dim viewport As New RichTextBox With {
                .Document = renderDocument, .Width = widthDip, .Height = heightDip,
                .Padding = New Thickness(0), .BorderThickness = New Thickness(0),
                .Background = Brushes.White, .Foreground = Brushes.Black,
                .IsReadOnly = True, .IsDocumentEnabled = True, .Focusable = False,
                .VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                .HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                .ClipToBounds = True
            }
            viewport.Measure(New Size(widthDip, heightDip))
            viewport.Arrange(New Rect(0, 0, widthDip, heightDip))
            viewport.UpdateLayout()
            ' The text view must exist before applying document geometry, as in the live editor.
            ApplyPageLayout(renderDocument, profile, marginMm)
            viewport.UpdateLayout()

            Dim visual As New DrawingVisual()
            Using drawing = visual.RenderOpen()
                drawing.DrawRectangle(Brushes.White, Nothing, New Rect(0, 0, widthDots, heightDots))
                drawing.PushTransform(New ScaleTransform(widthDots / widthDip, heightDots / heightDip))
                drawing.PushClip(New RectangleGeometry(New Rect(0, 0, widthDip, heightDip)))
                drawing.DrawRectangle(New VisualBrush(viewport) With {.Stretch = Stretch.Fill}, Nothing, New Rect(0, 0, widthDip, heightDip))
                drawing.Pop()
                drawing.Pop()
            End Using

            Dim bitmap As New RenderTargetBitmap(widthDots, heightDots, 96, 96, PixelFormats.Pbgra32)
            bitmap.Render(visual)
            bitmap.Freeze()

            Dim stride = widthDots * 4
            Dim pixels(stride * heightDots - 1) As Byte
            bitmap.CopyPixels(pixels, stride, 0)
            Dim packed(widthBytes * heightDots - 1) As Byte

            For y = 0 To heightDots - 1
                For x = 0 To widthDots - 1
                    Dim index = y * stride + x * 4
                    Dim blue = CInt(pixels(index))
                    Dim green = CInt(pixels(index + 1))
                    Dim red = CInt(pixels(index + 2))
                    Dim alpha = CInt(pixels(index + 3))
                    Dim luminance = (red * 299 + green * 587 + blue * 114) \ 1000
                    If alpha > 32 AndAlso luminance < CInt(threshold) Then
                        Dim packedIndex = y * widthBytes + x \ 8
                        packed(packedIndex) = CByte(packed(packedIndex) Or (1 << (7 - (x Mod 8))))
                    End If
                Next
            Next

            Return New LabelRenderResult(bitmap, packed, widthDots, heightDots, widthBytes)
        End Function

        Private Shared Function PageContainsInk(page As DocumentPage, widthDip As Double, heightDip As Double) As Boolean
            Dim width = Math.Max(1, CInt(Math.Ceiling(widthDip)))
            Dim height = Math.Max(1, CInt(Math.Ceiling(heightDip)))
            Dim visual As New DrawingVisual()
            Using drawing = visual.RenderOpen()
                drawing.DrawRectangle(Brushes.White, Nothing, New Rect(0, 0, widthDip, heightDip))
                drawing.DrawRectangle(New VisualBrush(page.Visual) With {.Stretch = Stretch.Fill}, Nothing, New Rect(0, 0, widthDip, heightDip))
            End Using
            Dim bitmap As New RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32)
            bitmap.Render(visual)
            Dim stride = width * 4
            Dim pixels(stride * height - 1) As Byte
            bitmap.CopyPixels(pixels, stride, 0)
            For index = 0 To pixels.Length - 4 Step 4
                Dim alpha = CInt(pixels(index + 3))
                If alpha > 32 Then
                    Dim luminance = (CInt(pixels(index + 2)) * 299 + CInt(pixels(index + 1)) * 587 + CInt(pixels(index)) * 114) \ 1000
                    If luminance < 245 Then Return True
                End If
            Next
            Return False
        End Function

        Private Shared Sub RemoveTrailingEmptyParagraphs(document As FlowDocument)
            While document.Blocks.LastBlock IsNot Nothing AndAlso TypeOf document.Blocks.LastBlock Is Paragraph
                Dim paragraph = DirectCast(document.Blocks.LastBlock, Paragraph)
                If Not String.IsNullOrWhiteSpace(New TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text) Then Exit While
                document.Blocks.Remove(paragraph)
            End While
            If document.Blocks.Count = 0 Then document.Blocks.Add(New Paragraph())
        End Sub

        Public Shared Function CloneDocument(document As FlowDocument) As FlowDocument
            Return DocumentImageCodec.Deserialize(DocumentImageCodec.Serialize(document))
        End Function
    End Class
End Namespace

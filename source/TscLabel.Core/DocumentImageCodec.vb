Imports System.IO
Imports System.Text.Json
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Documents
Imports System.Windows.Markup
Imports System.Windows.Media
Imports System.Windows.Media.Imaging

Namespace TscLabel.Core
    Public NotInheritable Class DocumentImageCodec
        Private Const ImagePrefix As String = "SOZO_EMBEDDED_IMAGE_PNG:"
        Private Const BackgroundPrefix As String = "SOZO_EMBEDDED_BACKGROUND:"

        Private NotInheritable Class BackgroundPayload
            Public Property Png As String = ""
            Public Property Opacity As Double = 1
            Public Property AlignmentX As String = "Center"
            Public Property AlignmentY As String = "Center"
            Public Property ViewportX As Double
            Public Property ViewportY As Double
            Public Property ViewportWidth As Double
            Public Property ViewportHeight As Double
        End Class

        Public Shared Function Serialize(document As FlowDocument) As String
            If document Is Nothing Then Throw New ArgumentNullException(NameOf(document))
            Dim savedImages As New List(Of (Image As Image, Source As ImageSource, Tag As Object))()
            Dim savedBackground = document.Background
            Dim savedDocumentTag = document.Tag
            Try
                For Each image In EnumerateImages(document)
                    If image.Source Is Nothing Then Continue For
                    savedImages.Add((image, image.Source, image.Tag))
                    image.Tag = ImagePrefix & Convert.ToBase64String(EncodePng(image.Source))
                    image.Source = Nothing
                Next

                Dim imageBrush = TryCast(savedBackground, ImageBrush)
                If imageBrush IsNot Nothing AndAlso imageBrush.ImageSource IsNot Nothing Then
                    Dim payload As New BackgroundPayload With {
                        .Png = Convert.ToBase64String(EncodePng(imageBrush.ImageSource)),
                        .Opacity = imageBrush.Opacity,
                        .AlignmentX = imageBrush.AlignmentX.ToString(),
                        .AlignmentY = imageBrush.AlignmentY.ToString(),
                        .ViewportX = imageBrush.Viewport.X,
                        .ViewportY = imageBrush.Viewport.Y,
                        .ViewportWidth = imageBrush.Viewport.Width,
                        .ViewportHeight = imageBrush.Viewport.Height
                    }
                    document.Tag = BackgroundPrefix & JsonSerializer.Serialize(payload)
                    document.Background = Brushes.Transparent
                End If
                Return XamlWriter.Save(document)
            Finally
                For Each item In savedImages
                    item.Image.Source = item.Source
                    item.Image.Tag = item.Tag
                Next
                document.Background = savedBackground
                document.Tag = savedDocumentTag
            End Try
        End Function

        Public Shared Function Deserialize(xaml As String) As FlowDocument
            Dim document = DirectCast(XamlReader.Parse(xaml), FlowDocument)
            For Each image In EnumerateImages(document)
                Dim value = TryCast(image.Tag, String)
                If String.IsNullOrWhiteSpace(value) OrElse Not value.StartsWith(ImagePrefix, StringComparison.Ordinal) Then Continue For
                image.Source = DecodePng(Convert.FromBase64String(value.Substring(ImagePrefix.Length)))
                image.Tag = Nothing
            Next

            Dim documentTag = TryCast(document.Tag, String)
            If Not String.IsNullOrWhiteSpace(documentTag) AndAlso documentTag.StartsWith(BackgroundPrefix, StringComparison.Ordinal) Then
                Dim payload = JsonSerializer.Deserialize(Of BackgroundPayload)(documentTag.Substring(BackgroundPrefix.Length))
                If payload IsNot Nothing Then
                    document.Background = New ImageBrush(DecodePng(Convert.FromBase64String(payload.Png))) With {
                        .Opacity = Math.Clamp(payload.Opacity, 0, 1),
                        .AlignmentX = ParseAlignmentX(payload.AlignmentX),
                        .AlignmentY = ParseAlignmentY(payload.AlignmentY),
                        .Stretch = Stretch.Fill,
                        .TileMode = TileMode.None,
                        .ViewportUnits = BrushMappingMode.Absolute,
                        .Viewport = New Rect(payload.ViewportX, payload.ViewportY, payload.ViewportWidth, payload.ViewportHeight)
                    }
                End If
                document.Tag = Nothing
            End If
            Return document
        End Function

        Private Shared Function EncodePng(source As ImageSource) As Byte()
            Dim bitmap = TryCast(source, BitmapSource)
            If bitmap Is Nothing Then Throw New InvalidDataException("Das Bildformat kann nicht eingebettet werden.")
            Dim encoder As New PngBitmapEncoder()
            encoder.Frames.Add(BitmapFrame.Create(bitmap))
            Using stream As New MemoryStream()
                encoder.Save(stream)
                Return stream.ToArray()
            End Using
        End Function

        Private Shared Function DecodePng(bytes As Byte()) As BitmapSource
            Using stream As New MemoryStream(bytes)
                Dim bitmap As New BitmapImage()
                bitmap.BeginInit()
                bitmap.CacheOption = BitmapCacheOption.OnLoad
                bitmap.StreamSource = stream
                bitmap.EndInit()
                bitmap.Freeze()
                Return bitmap
            End Using
        End Function

        Private Shared Function ParseAlignmentX(value As String) As AlignmentX
            Dim result As AlignmentX
            If [Enum].TryParse(value, True, result) Then Return result
            Return AlignmentX.Center
        End Function

        Private Shared Function ParseAlignmentY(value As String) As AlignmentY
            Dim result As AlignmentY
            If [Enum].TryParse(value, True, result) Then Return result
            Return AlignmentY.Center
        End Function

        Public Shared Iterator Function EnumerateImages(document As FlowDocument) As IEnumerable(Of Image)
            For Each block In document.Blocks
                For Each image In EnumerateImages(block)
                    Yield image
                Next
            Next
        End Function

        Private Shared Iterator Function EnumerateImages(block As Block) As IEnumerable(Of Image)
            If TypeOf block Is BlockUIContainer Then
                Dim image = TryCast(DirectCast(block, BlockUIContainer).Child, Image)
                If image IsNot Nothing Then Yield image
            ElseIf TypeOf block Is Paragraph Then
                For Each inline In DirectCast(block, Paragraph).Inlines
                    If TypeOf inline Is InlineUIContainer Then
                        Dim image = TryCast(DirectCast(inline, InlineUIContainer).Child, Image)
                        If image IsNot Nothing Then Yield image
                    ElseIf TypeOf inline Is Figure Then
                        For Each childBlock In DirectCast(inline, Figure).Blocks
                            For Each image In EnumerateImages(childBlock)
                                Yield image
                            Next
                        Next
                    End If
                Next
            ElseIf TypeOf block Is Section Then
                For Each childBlock In DirectCast(block, Section).Blocks
                    For Each image In EnumerateImages(childBlock)
                        Yield image
                    Next
                Next
            ElseIf TypeOf block Is List Then
                For Each item In DirectCast(block, List).ListItems
                    For Each childBlock In item.Blocks
                        For Each image In EnumerateImages(childBlock)
                            Yield image
                        Next
                    Next
                Next
            End If
        End Function
    End Class
End Namespace

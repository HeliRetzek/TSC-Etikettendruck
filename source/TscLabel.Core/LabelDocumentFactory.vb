Imports System.Windows
Imports System.Windows.Documents
Imports System.Windows.Media
Imports System.Windows.Controls
Imports System.Text.RegularExpressions

Namespace TscLabel.Core
    Public NotInheritable Class LabelDocumentFactory
        Public Const OfficeLine As String = "SOZO Vienna 01-928 4446"

        Public Shared Function CreateFreeTemplate() As FlowDocument
            Dim document = CreateBaseDocument()
            document.Blocks.Add(CreateParagraph("Praxis Codex Test", 15, FontWeights.Bold))
            document.Blocks.Add(CreateParagraph("Freier Etikettentext", 11, FontWeights.Normal))
            Return document
        End Function

        Public Shared Function CreatePatientTemplate(data As PatientLabelData, Optional footerText As String = OfficeLine) As FlowDocument
            If data Is Nothing Then Throw New ArgumentNullException(NameOf(data))
            Dim document = CreateBaseDocument()

            Dim nameLine = If(String.IsNullOrWhiteSpace(data.DisplayName), JoinNonEmpty({data.Title, data.Surname, data.FirstName}, " "), data.DisplayName.Trim())
            Dim insuranceBirth = FormatInsuranceOrBirthDate(data.InsuranceNumber, data.BirthDate)
            If Not String.IsNullOrWhiteSpace(data.InsuranceProvider) Then insuranceBirth = JoinNonEmpty({insuranceBirth, data.InsuranceProvider}, " · ")
            Dim cityLine = JoinNonEmpty({data.PostalCode, data.City}, " ")
            Dim addressLine = JoinNonEmpty({cityLine, data.Street}, ", ")
            Dim phoneLine = If(String.IsNullOrWhiteSpace(data.Phone), "", "Tel. " & data.Phone)

            document.Blocks.Add(CreateParagraph(If(nameLine = "", "Name Vorname", nameLine), 13, FontWeights.Bold))
            document.Blocks.Add(CreateParagraph(insuranceBirth, 9, FontWeights.SemiBold))
            document.Blocks.Add(CreateParagraph(addressLine, 7.5, FontWeights.Medium))
            document.Blocks.Add(CreateParagraph(phoneLine, 7.5, FontWeights.Normal))
            document.Blocks.Add(CreateSeparatorBlock())
            document.Blocks.Add(CreateParagraph(If(String.IsNullOrWhiteSpace(footerText), OfficeLine, footerText.Trim()), 6, FontWeights.Normal, True, TextAlignment.Right))
            Return document
        End Function

        Public Shared Function CreatePatientPlaceholderTemplate(Optional footerText As String = "%Arztzeile%") As FlowDocument
            Dim document = CreateBaseDocument()
            document.Blocks.Add(CreateParagraph("%Name%", 13, FontWeights.Bold))
            document.Blocks.Add(CreateParagraph("%Versicherungsnummer%", 9, FontWeights.SemiBold))
            document.Blocks.Add(CreateParagraph("%Adresse%", 7.5, FontWeights.Medium))
            document.Blocks.Add(CreateParagraph("Tel. %Telefon%", 7.5, FontWeights.Normal))
            document.Blocks.Add(CreateSeparatorBlock())
            document.Blocks.Add(CreateParagraph(If(String.IsNullOrWhiteSpace(footerText), "%Arztzeile%", footerText), 6, FontWeights.Normal, True, TextAlignment.Right))
            Return document
        End Function

        Public Shared Function BuildPracticeFooter(doctorName As String, practiceLine As String) As String
            Dim practice = If(String.IsNullOrWhiteSpace(practiceLine), OfficeLine, practiceLine.Trim())
            If String.IsNullOrWhiteSpace(doctorName) Then Return practice
            Return doctorName.Trim() & " | " & practice
        End Function

        Public Shared Function FormatInsuranceNumber(value As String) As String
            Dim original = If(value, "").Trim()
            Dim digits = Regex.Replace(original, "[^0-9]", "")
            If digits.Length = 10 Then Return digits.Substring(0, 4) & "-" & digits.Substring(4)
            Return original
        End Function

        Public Shared Function CompleteInsuranceNumber(value As String, birthDate As String) As String
            Dim digits = Regex.Replace(If(value, ""), "[^0-9]", "")
            If digits.Length = 10 Then Return digits
            Dim parsedBirthDate As DateTime
            If digits.Length = 4 AndAlso TryParseBirthDate(birthDate, parsedBirthDate) Then
                Return digits & parsedBirthDate.ToString("ddMMyy", Globalization.CultureInfo.InvariantCulture)
            End If
            Return If(value, "").Trim()
        End Function

        Public Shared Function FormatInsuranceOrBirthDate(insuranceNumber As String, birthDate As String) As String
            Dim completed = CompleteInsuranceNumber(insuranceNumber, birthDate)
            Dim digits = Regex.Replace(completed, "[^0-9]", "")
            If digits.Length = 10 Then Return digits.Substring(0, 4) & "-" & digits.Substring(4)

            Dim formattedBirthDate = FormatBirthDate(birthDate)
            If Not String.IsNullOrWhiteSpace(formattedBirthDate) Then Return "geb. " & formattedBirthDate
            Return FormatInsuranceNumber(insuranceNumber)
        End Function

        Public Shared Function FormatBirthDate(value As String) As String
            Dim parsed As DateTime
            If Not TryParseBirthDate(value, parsed) Then Return If(value, "").Trim()
            Return parsed.ToString("dd.MM.yyyy", Globalization.CultureInfo.InvariantCulture)
        End Function

        Private Shared Function TryParseBirthDate(value As String, ByRef parsed As DateTime) As Boolean
            Dim formats = {"d.M.yyyy", "dd.MM.yyyy", "yyyyMMdd", "yyyy-MM-dd", "ddMMyyyy"}
            Return DateTime.TryParseExact(
                If(value, "").Trim(),
                formats,
                Globalization.CultureInfo.InvariantCulture,
                Globalization.DateTimeStyles.None,
                parsed)
        End Function

        Public Shared Function HasEmbeddedBirthDate(value As String) As Boolean
            Return Regex.Replace(If(value, ""), "[^0-9]", "").Length = 10
        End Function

        Public Shared Function CreateSeparatorBlock() As BlockUIContainer
            Dim separator As New Border With {
                .Height = 0.8,
                .Background = Brushes.Black,
                .Margin = New Thickness(0, PointsToDip(1.5), 0, PointsToDip(1.5)),
                .HorizontalAlignment = HorizontalAlignment.Stretch
            }
            Return New BlockUIContainer(separator) With {.Margin = New Thickness(0), .Padding = New Thickness(0)}
        End Function

        Public Shared Function CreateBaseDocument() As FlowDocument
            Return New FlowDocument() With {
                .FontFamily = New FontFamily("Segoe UI"),
                .FontSize = PointsToDip(11),
                .Foreground = Brushes.Black,
                .Background = Brushes.White,
                .TextAlignment = TextAlignment.Left,
                .ColumnGap = 0,
                .PagePadding = New Thickness(0)
            }
        End Function

        Public Shared Function PointsToDip(points As Double) As Double
            Return points * 96.0 / 72.0
        End Function

        Public Shared Function DipToPoints(dip As Double) As Double
            Return dip * 72.0 / 96.0
        End Function

        Public Shared Function GetPlainText(document As FlowDocument) As String
            If document Is Nothing Then Return ""
            Return New TextRange(document.ContentStart, document.ContentEnd).Text.Trim()
        End Function

        Private Shared Function CreateParagraph(textValue As String,
                                                points As Double,
                                                weight As FontWeight,
                                                Optional isItalic As Boolean = False,
                                                Optional alignment As TextAlignment = TextAlignment.Left) As Paragraph
            Dim paragraph As New Paragraph(New Run(If(textValue, ""))) With {
                .Margin = New Thickness(0),
                .Padding = New Thickness(0),
                .FontFamily = New FontFamily("Segoe UI"),
                .FontSize = PointsToDip(points),
                .FontWeight = weight,
                .FontStyle = If(isItalic, FontStyles.Italic, FontStyles.Normal),
                .TextAlignment = alignment,
                .LineHeight = PointsToDip(points * 1.04),
                .LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            }
            Return paragraph
        End Function

        Private Shared Function JoinNonEmpty(values As IEnumerable(Of String), separator As String) As String
            Return String.Join(separator, values.Where(Function(value) Not String.IsNullOrWhiteSpace(value)).Select(Function(value) value.Trim()))
        End Function
    End Class
End Namespace

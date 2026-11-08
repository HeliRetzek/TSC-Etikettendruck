Imports System.Text.RegularExpressions
Imports System.Globalization
Imports System.Windows.Documents

Namespace TscLabel.Core
    Public NotInheritable Class LabelTemplateEngine
        Public Shared ReadOnly Placeholders As IReadOnlyList(Of LabelPlaceholderDefinition) = New List(Of LabelPlaceholderDefinition) From {
            New LabelPlaceholderDefinition("Person", "Name", "%Name%"), New LabelPlaceholderDefinition("Person", "Vorname", "%Vorname%"), New LabelPlaceholderDefinition("Person", "Nachname", "%Nachname%"), New LabelPlaceholderDefinition("Person", "Titel", "%Titel%"),
            New LabelPlaceholderDefinition("Adresse", "Straße", "%Straße%"), New LabelPlaceholderDefinition("Adresse", "PLZ", "%PLZ%"), New LabelPlaceholderDefinition("Adresse", "Stadt", "%Stadt%"), New LabelPlaceholderDefinition("Adresse", "Adresse gesamt", "%Adresse%"),
            New LabelPlaceholderDefinition("Kontakt", "Telefon", "%Telefon%"), New LabelPlaceholderDefinition("Kontakt", "E-Mail", "%E-Mail%"),
            New LabelPlaceholderDefinition("Versicherung", "Versicherungsnummer", "%Versicherungsnummer%"), New LabelPlaceholderDefinition("Versicherung", "Versicherung", "%Versicherung%"), New LabelPlaceholderDefinition("Versicherung", "Geburtsdatum", "%Geburtsdatum%"),
            New LabelPlaceholderDefinition("Praxis", "Arzt", "%Arzt%"), New LabelPlaceholderDefinition("Praxis", "Praxis", "%Praxis%"), New LabelPlaceholderDefinition("Praxis", "Arztzeile", "%Arztzeile%"),
            New LabelPlaceholderDefinition("Datum/Zeit", "Datum", "%Date%"), New LabelPlaceholderDefinition("Datum/Zeit", "Datum + Zeit", "%DateTime%"), New LabelPlaceholderDefinition("Datum/Zeit", "Uhrzeit", "%Time%")
        }
        Private Shared ReadOnly PlaceholderPattern As New Regex(String.Join("|", Placeholders.Select(Function(item) Regex.Escape(item.Token)).Concat({Regex.Escape("%Namen%"), Regex.Escape("%Strasse%"), Regex.Escape("%VsgNr%"), Regex.Escape("%Email%"), Regex.Escape("%Versicherungsträger%")})), RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)

        Public Shared Function ContainsPlaceholders(document As FlowDocument) As Boolean
            If document Is Nothing Then Return False
            Return PlaceholderPattern.IsMatch(New TextRange(document.ContentStart, document.ContentEnd).Text)
        End Function

        Public Shared Function ApplyPatientData(document As FlowDocument,
                                                data As PatientLabelData,
                                                Optional doctorName As String = "",
                                                Optional practiceLine As String = LabelDocumentFactory.OfficeLine,
                                                Optional nowValue As DateTime? = Nothing) As Integer
            If document Is Nothing Then Throw New ArgumentNullException(NameOf(document))
            If data Is Nothing Then Throw New ArgumentNullException(NameOf(data))

            Dim fullName = If(String.IsNullOrWhiteSpace(data.DisplayName), JoinNonEmpty({data.Title, data.Surname, data.FirstName}, " "), data.DisplayName.Trim())
            Dim cityLine = JoinNonEmpty({data.PostalCode, data.City}, " ")
            Dim address = JoinNonEmpty({cityLine, data.Street}, ", ")
            Dim practice = If(String.IsNullOrWhiteSpace(practiceLine), LabelDocumentFactory.OfficeLine, practiceLine.Trim())
            Dim localNow = If(nowValue.HasValue, nowValue.Value, DateTime.Now)
            Dim replacements As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
                {"%Name%", fullName},
                {"%Namen%", fullName},
                {"%Vorname%", If(data.FirstName, "").Trim()},
                {"%Nachname%", If(data.Surname, "").Trim()},
                {"%Titel%", If(data.Title, "").Trim()},
                {"%Straße%", If(data.Street, "").Trim()},
                {"%Strasse%", If(data.Street, "").Trim()},
                {"%PLZ%", If(data.PostalCode, "").Trim()},
                {"%Stadt%", If(data.City, "").Trim()},
                {"%Adresse%", address},
                {"%Versicherungsnummer%", LabelDocumentFactory.FormatInsuranceOrBirthDate(data.InsuranceNumber, data.BirthDate)},
                {"%VsgNr%", LabelDocumentFactory.FormatInsuranceOrBirthDate(data.InsuranceNumber, data.BirthDate)},
                {"%Versicherung%", If(data.InsuranceProvider, "").Trim()},
                {"%Versicherungsträger%", If(data.InsuranceProvider, "").Trim()},
                {"%Telefon%", If(data.Phone, "").Trim()},
                {"%Email%", If(data.Email, "").Trim()},
                {"%E-Mail%", If(data.Email, "").Trim()},
                {"%Geburtsdatum%", LabelDocumentFactory.FormatBirthDate(data.BirthDate)},
                {"%Arzt%", If(doctorName, "").Trim()},
                {"%Arztzeile%", LabelDocumentFactory.BuildPracticeFooter(doctorName, practice)},
                {"%Praxis%", practice},
                {"%Date%", localNow.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)},
                {"%DateTime%", localNow.ToString("dd.MM.yyyy HH:mm'h'", CultureInfo.InvariantCulture)},
                {"%Time%", localNow.ToString("HH:mm'h'", CultureInfo.InvariantCulture)}
            }

            Dim replacementCount = 0
            ' WPF kann beim Ändern eines Runs die Inline-Sammlung intern neu
            ' aufteilen. Darum zuerst eine stabile Momentaufnahme bilden.
            For Each run In EnumerateRuns(document).ToList()
                Dim nextText = run.Text
                For Each item In replacements
                    Dim matches = Regex.Matches(nextText, Regex.Escape(item.Key), RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant).Count
                    If matches = 0 Then Continue For
                    nextText = Regex.Replace(nextText, Regex.Escape(item.Key), item.Value, RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
                    replacementCount += matches
                Next
                run.Text = nextText
            Next
            Return replacementCount
        End Function

        Private Shared Iterator Function EnumerateRuns(document As FlowDocument) As IEnumerable(Of Run)
            For Each block In document.Blocks
                For Each run In EnumerateRuns(block)
                    Yield run
                Next
            Next
        End Function

        Private Shared Iterator Function EnumerateRuns(block As Block) As IEnumerable(Of Run)
            If TypeOf block Is Paragraph Then
                For Each inline In DirectCast(block, Paragraph).Inlines
                    For Each run In EnumerateRuns(inline)
                        Yield run
                    Next
                Next
            ElseIf TypeOf block Is Section Then
                For Each child In DirectCast(block, Section).Blocks
                    For Each run In EnumerateRuns(child)
                        Yield run
                    Next
                Next
            ElseIf TypeOf block Is List Then
                For Each item In DirectCast(block, List).ListItems
                    For Each child In item.Blocks
                        For Each run In EnumerateRuns(child)
                            Yield run
                        Next
                    Next
                Next
            End If
        End Function

        Private Shared Iterator Function EnumerateRuns(inline As Inline) As IEnumerable(Of Run)
            If TypeOf inline Is Run Then
                Yield DirectCast(inline, Run)
            ElseIf TypeOf inline Is Span Then
                For Each child In DirectCast(inline, Span).Inlines
                    For Each run In EnumerateRuns(child)
                        Yield run
                    Next
                Next
            ElseIf TypeOf inline Is Figure Then
                For Each block In DirectCast(inline, Figure).Blocks
                    For Each run In EnumerateRuns(block)
                        Yield run
                    Next
                Next
            End If
        End Function

        Private Shared Function JoinNonEmpty(values As IEnumerable(Of String), separator As String) As String
            Return String.Join(separator, values.Where(Function(value) Not String.IsNullOrWhiteSpace(value)).Select(Function(value) value.Trim()))
        End Function
    End Class
End Namespace

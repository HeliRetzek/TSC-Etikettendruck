Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions

Namespace TscLabel.Core
    Public NotInheritable Class OffisyOcrParser
        Private Shared ReadOnly DatePattern As New Regex("\b\d{1,2}[.]\d{1,2}[.]\d{4}\b", RegexOptions.Compiled)
        Private Shared ReadOnly InsurancePattern As New Regex("(?<!\d)(?:\d[\s.-]*){10}(?!\d)", RegexOptions.Compiled)
        Private Shared ReadOnly PartialInsurancePattern As New Regex("(?<!\d)\d{4}(?!\d)", RegexOptions.Compiled)
        Private Shared ReadOnly PostalPattern As New Regex("\b(\d{4})\s+(.+)$", RegexOptions.Compiled)
        Private Shared ReadOnly DoctorPattern As New Regex("\bDr\.?\s*(?:med\.?\s*)?([\p{L}][\p{L}'’-]*(?:\s+[\p{L}][\p{L}'’-]*){1,3})", RegexOptions.IgnoreCase Or RegexOptions.Compiled)

        Public Shared Function Parse(rawText As String) As OffisyOcrResult
            If String.IsNullOrWhiteSpace(rawText) Then Throw New InvalidDataException("Die OCR hat keinen lesbaren Text erkannt.")

            Dim lines = rawText.Replace(vbCr, "").Split(ControlChars.Lf).
                Select(Function(value) Regex.Replace(value, "\s+", " ").Trim()).
                Where(Function(value) value.Length > 0).
                ToList()
            Dim data As New PatientLabelData()
            Dim doctorName = ExtractDoctor(lines)
            ExtractName(lines, data)
            ExtractAddress(lines, data)
            data.BirthDate = ExtractAfterSection(lines, "GEBURT", Function(value) FirstMatch(DatePattern, value))
            data.Phone = ExtractPhone(lines)
            data.InsuranceNumber = LabelDocumentFactory.CompleteInsuranceNumber(ExtractInsurance(lines), data.BirthDate)

            Dim warnings As New List(Of String)()
            If Not data.HasIdentity Then warnings.Add("Name wurde nicht sicher erkannt.")
            If String.IsNullOrWhiteSpace(data.InsuranceNumber) Then
                warnings.Add(If(String.IsNullOrWhiteSpace(data.BirthDate),
                    "Versicherungsnummer wurde nicht erkannt.",
                    "Versicherungsnummer wurde nicht erkannt; das Geburtsdatum wird ersatzweise ausgegeben."))
            End If
            If String.IsNullOrWhiteSpace(data.Street) OrElse String.IsNullOrWhiteSpace(data.PostalCode) Then warnings.Add("Adresse wurde nicht vollständig erkannt.")
            If String.IsNullOrWhiteSpace(data.Phone) Then warnings.Add("Telefonnummer wurde nicht erkannt.")
            If String.IsNullOrWhiteSpace(doctorName) Then warnings.Add("Arztname wurde nicht erkannt; nur die Praxiszeile wird eingesetzt.")
            Return New OffisyOcrResult(data, doctorName, warnings)
        End Function

        Private Shared Function ExtractDoctor(lines As IList(Of String)) As String
            For index = 0 To lines.Count - 1
                Dim match = DoctorPattern.Match(lines(index))
                If Not match.Success Then Continue For
                Dim doctor = "Dr. " & match.Groups(1).Value.Trim()
                lines(index) = (lines(index).Substring(0, match.Index) & " " & lines(index).Substring(match.Index + match.Length)).Trim()
                Return Regex.Replace(doctor, "\s+", " ")
            Next
            Return ""
        End Function

        Private Shared Sub ExtractName(lines As IReadOnlyList(Of String), data As PatientLabelData)
            Dim heading = lines.FirstOrDefault(Function(value) Not IsSectionLabel(value) AndAlso
                                                           Not DatePattern.IsMatch(value) AndAlso
                                                           Not InsurancePattern.IsMatch(value) AndAlso
                                                           Not PostalPattern.IsMatch(value) AndAlso
                                                           Not Regex.IsMatch(value, "\d"))
            If String.IsNullOrWhiteSpace(heading) Then Return
            Dim parts = heading.Split(" "c, StringSplitOptions.RemoveEmptyEntries).ToList()
            If parts.Count > 0 AndAlso IsTitle(parts(0)) Then
                data.Title = NormalizeTitle(parts(0))
                parts.RemoveAt(0)
            End If
            If parts.Count = 1 Then
                data.Surname = parts(0)
            ElseIf parts.Count > 1 Then
                data.Surname = parts(parts.Count - 1)
                data.FirstName = String.Join(" ", parts.Take(parts.Count - 1))
            End If
        End Sub

        Private Shared Sub ExtractAddress(lines As IReadOnlyList(Of String), data As PatientLabelData)
            Dim start = FindSection(lines, "ADRESS")
            If start < 0 Then Return
            For index = start + 1 To Math.Min(lines.Count - 1, start + 4)
                Dim value = lines(index)
                If IsSectionLabel(value) Then Exit For
                Dim postal = PostalPattern.Match(value)
                If postal.Success Then
                    data.PostalCode = postal.Groups(1).Value
                    data.City = postal.Groups(2).Value.Trim(" "c, ","c)
                ElseIf String.IsNullOrWhiteSpace(data.Street) AndAlso Regex.IsMatch(value, "\d") Then
                    data.Street = value
                End If
            Next
        End Sub

        Private Shared Function ExtractPhone(lines As IReadOnlyList(Of String)) As String
            Dim start = FindSection(lines, "TELEFON")
            If start < 0 Then Return ""
            For index = start To Math.Min(lines.Count - 1, start + 2)
                Dim candidate = If(index = start, RemoveLabelPrefix(lines(index), "TELEFONNUMMER"), lines(index))
                Dim cleaned = Regex.Replace(candidate, "[^0-9+() /-]", "").Trim()
                If Regex.Replace(cleaned, "\D", "").Length >= 6 Then Return cleaned
            Next
            Return ""
        End Function

        Private Shared Function ExtractInsurance(lines As IReadOnlyList(Of String)) As String
            Dim start = FindSection(lines, "SOZVER")
            If start < 0 Then start = FindSection(lines, "SOZIALVERS")
            Dim candidates As IEnumerable(Of String) = If(start >= 0, lines.Skip(start).Take(3), lines)
            For Each value In candidates
                Dim match = InsurancePattern.Match(value)
                If match.Success Then Return Regex.Replace(match.Value, "\D", "")
            Next
            If start >= 0 Then
                For Each value In lines.Skip(start).Take(3)
                    Dim partialMatch = PartialInsurancePattern.Match(value)
                    If partialMatch.Success Then Return partialMatch.Value
                Next
            End If
            Return ""
        End Function

        Private Shared Function ExtractAfterSection(lines As IReadOnlyList(Of String), sectionKey As String, selector As Func(Of String, String)) As String
            Dim start = FindSection(lines, sectionKey)
            If start < 0 Then Return ""
            For index = start To Math.Min(lines.Count - 1, start + 2)
                Dim value = selector(lines(index))
                If Not String.IsNullOrWhiteSpace(value) Then Return value
            Next
            Return ""
        End Function

        Private Shared Function FindSection(lines As IReadOnlyList(Of String), key As String) As Integer
            For index = 0 To lines.Count - 1
                If Fold(lines(index)).Contains(key, StringComparison.Ordinal) Then Return index
            Next
            Return -1
        End Function

        Private Shared Function IsSectionLabel(value As String) As Boolean
            Dim folded = Fold(value)
            Return folded.Contains("ADRESS") OrElse folded.Contains("GEBURT") OrElse folded.Contains("TELEFON") OrElse
                   folded.Contains("SOZVER") OrElse folded.Contains("SOZIALVERS") OrElse folded.Contains("VERSICHERUNGSTRAGER")
        End Function

        Private Shared Function Fold(value As String) As String
            Dim normalized = If(value, "").Normalize(NormalizationForm.FormD)
            Dim builder As New StringBuilder()
            For Each character In normalized
                If CharUnicodeInfo.GetUnicodeCategory(character) <> UnicodeCategory.NonSpacingMark AndAlso Char.IsLetterOrDigit(character) Then
                    builder.Append(Char.ToUpperInvariant(character))
                End If
            Next
            Return builder.ToString()
        End Function

        Private Shared Function FirstMatch(pattern As Regex, value As String) As String
            Dim match = pattern.Match(value)
            Return If(match.Success, match.Value, "")
        End Function

        Private Shared Function RemoveLabelPrefix(value As String, label As String) As String
            Return Regex.Replace(value, "^\s*" & Regex.Escape(label) & "\s*[:.-]?\s*", "", RegexOptions.IgnoreCase)
        End Function

        Private Shared Function IsTitle(value As String) As Boolean
            Return Regex.IsMatch(value, "^(Dr|Mag|Prof)[.]?$", RegexOptions.IgnoreCase)
        End Function

        Private Shared Function NormalizeTitle(value As String) As String
            Return value.TrimEnd("."c) & "."
        End Function
    End Class
End Namespace

Imports System.IO
Imports System.Text

Namespace TscLabel.Core
    Public NotInheritable Class LaborauftragParser
        Public Shared Function Parse(pathValue As String) As LaborImportResult
            If String.IsNullOrWhiteSpace(pathValue) Then Throw New ArgumentException("Es wurde keine Datei angegeben.", NameOf(pathValue))
            If Not File.Exists(pathValue) Then Throw New FileNotFoundException("Die Laborauftrag-Datei wurde nicht gefunden.", pathValue)

            Dim values As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each rawLine In File.ReadAllLines(pathValue, DetectEncoding(pathValue))
                Dim line = rawLine.Trim()
                If line = "" OrElse line.StartsWith("[", StringComparison.Ordinal) Then Continue For
                Dim separator = line.IndexOf("="c)
                If separator <= 0 Then Continue For
                values(line.Substring(0, separator).Trim()) = line.Substring(separator + 1).Trim()
            Next

            Dim data As New PatientLabelData With {
                .Surname = GetFirst(values, "P-Zuname", "Zuname", "Nachname", "Name"),
                .FirstName = GetFirst(values, "P-Vorname", "Vorname"),
                .Title = GetFirst(values, "P-Titel", "Titel"),
                .InsuranceNumber = GetFirst(values, "P-VersNr", "VersNr", "Versicherungsnummer", "SVNR"),
                .BirthDate = GetFirst(values, "P-Gebdat", "Gebdat", "Geburtsdatum", "Geboren"),
                .PostalCode = GetFirst(values, "P-Plz", "PLZ"),
                .City = GetFirst(values, "P-Ort", "Ort", "Stadt"),
                .Street = GetFirst(values, "P-Strasse", "P-Straße", "Strasse", "Straße", "Adresse"),
                .Phone = GetFirst(values, "P-Telefon", "Telefon", "Tel", "Mobil")
            }

            Dim warnings As New List(Of String)()
            If Not data.HasIdentity Then warnings.Add("Name und Vorname fehlen.")
            If String.IsNullOrWhiteSpace(data.InsuranceNumber) Then warnings.Add("Versicherungsnummer fehlt.")
            If String.IsNullOrWhiteSpace(data.BirthDate) Then warnings.Add("Geburtsdatum fehlt.")
            If String.IsNullOrWhiteSpace(data.PostalCode) AndAlso String.IsNullOrWhiteSpace(data.City) AndAlso String.IsNullOrWhiteSpace(data.Street) Then warnings.Add("Adresse fehlt.")
            If String.IsNullOrWhiteSpace(data.Phone) Then warnings.Add("Telefonnummer fehlt.")

            Return New LaborImportResult(data, pathValue, warnings)
        End Function

        Public Shared Function FindNewest(directoryPath As String) As String
            If String.IsNullOrWhiteSpace(directoryPath) OrElse Not Directory.Exists(directoryPath) Then Return ""
            Return Directory.EnumerateFiles(directoryPath, "Laborauftrag*.txt", SearchOption.TopDirectoryOnly).
                Concat(Directory.EnumerateFiles(directoryPath, "Laboranfrage*.txt", SearchOption.TopDirectoryOnly)).
                Select(Function(pathItem) New FileInfo(pathItem)).
                OrderByDescending(Function(info) info.LastWriteTimeUtc).
                Select(Function(info) info.FullName).
                FirstOrDefault("")
        End Function

        Private Shared Function GetFirst(values As Dictionary(Of String, String), ParamArray keys As String()) As String
            For Each key In keys
                Dim value As String = Nothing
                If values.TryGetValue(key, value) AndAlso Not String.IsNullOrWhiteSpace(value) Then Return value.Trim()
            Next
            Return ""
        End Function

        Private Shared Function DetectEncoding(pathValue As String) As Encoding
            Dim bytes = File.ReadAllBytes(pathValue)
            If bytes.Length >= 3 AndAlso bytes(0) = &HEF AndAlso bytes(1) = &HBB AndAlso bytes(2) = &HBF Then Return Encoding.UTF8
            If bytes.Length >= 2 AndAlso bytes(0) = &HFF AndAlso bytes(1) = &HFE Then Return Encoding.Unicode
            If bytes.Length >= 2 AndAlso bytes(0) = &HFE AndAlso bytes(1) = &HFF Then Return Encoding.BigEndianUnicode
            Try
                Dim strictUtf8 As New UTF8Encoding(False, True)
                strictUtf8.GetString(bytes)
                Return New UTF8Encoding(False)
            Catch ex As DecoderFallbackException
                Return Encoding.Latin1
            End Try
        End Function
    End Class
End Namespace

Imports System.IO
Imports System.Text.Json
Imports System.Windows.Documents
Imports System.Windows.Markup

Namespace TscLabel.Core
    Public NotInheritable Class PrintHistoryStore
        Public Const MaximumEntries As Integer = 50

        Private Shared ReadOnly JsonOptions As New JsonSerializerOptions With {
            .WriteIndented = True,
            .PropertyNameCaseInsensitive = True
        }

        Public Shared ReadOnly Property DefaultPath As String
            Get
                Return Path.Combine(AppContext.BaseDirectory, "TSC-Etikettendruck-Verlauf.json")
            End Get
        End Property

        Public Shared Function Load(Optional pathValue As String = Nothing) As List(Of PrintHistoryEntry)
            Dim actualPath = ResolvePath(pathValue)
            If Not File.Exists(actualPath) Then Return New List(Of PrintHistoryEntry)()
            Try
                Dim entries = JsonSerializer.Deserialize(Of List(Of PrintHistoryEntry))(File.ReadAllText(actualPath), JsonOptions)
                Return If(entries, New List(Of PrintHistoryEntry)()).
                    Where(Function(entry) entry IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(entry.DocumentXaml)).
                    OrderByDescending(Function(entry) entry.PrintedAt).
                    Take(MaximumEntries).
                    ToList()
            Catch ex As JsonException
                Throw New InvalidDataException("Der Etikettenverlauf ist beschädigt und wurde nicht verändert.", ex)
            End Try
        End Function

        Public Shared Sub Add(document As FlowDocument, profile As LabelProfile, copies As Integer, normalPrint As Boolean, Optional pathValue As String = Nothing)
            Add(document, profile, copies, normalPrint, "", "", pathValue)
        End Sub

        Public Shared Sub Add(document As FlowDocument, profile As LabelProfile, copies As Integer, normalPrint As Boolean, printerName As String, printerLanguage As String, Optional pathValue As String = Nothing)
            If document Is Nothing Then Throw New ArgumentNullException(NameOf(document))
            If profile Is Nothing Then Throw New ArgumentNullException(NameOf(profile))
            Dim entry As New PrintHistoryEntry With {
                .PrintedAt = DateTimeOffset.Now,
                .ProfileName = profile.Name,
                .WidthMm = profile.WidthMm,
                .HeightMm = profile.HeightMm,
                .Copies = Math.Max(1, copies),
                .NormalPrint = normalPrint,
                .PrinterName = If(printerName, "").Trim(),
                .PrinterLanguage = If(printerLanguage, "").Trim(),
                .PlainText = LabelDocumentFactory.GetPlainText(document),
                .DocumentXaml = DocumentImageCodec.Serialize(document)
            }
            Dim entries = Load(pathValue)
            entries.Insert(0, entry)
            Save(entries.Take(MaximumEntries), pathValue)
        End Sub

        Public Shared Function RestoreDocument(entry As PrintHistoryEntry) As FlowDocument
            If entry Is Nothing OrElse String.IsNullOrWhiteSpace(entry.DocumentXaml) Then Throw New InvalidDataException("Der Verlaufseintrag enthält kein Etikett.")
            Try
                Return DocumentImageCodec.Deserialize(entry.DocumentXaml)
            Catch ex As Exception
                Throw New InvalidDataException("Das gespeicherte Etikett konnte nicht wiederhergestellt werden.", ex)
            End Try
        End Function

        Public Shared Sub Clear(Optional pathValue As String = Nothing)
            Save(Array.Empty(Of PrintHistoryEntry)(), pathValue)
        End Sub

        Private Shared Sub Save(entries As IEnumerable(Of PrintHistoryEntry), pathValue As String)
            Dim actualPath = ResolvePath(pathValue)
            Dim directoryPath = Path.GetDirectoryName(actualPath)
            If Not String.IsNullOrWhiteSpace(directoryPath) Then Directory.CreateDirectory(directoryPath)
            Dim temporaryPath = actualPath & ".tmp"
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(entries.ToList(), JsonOptions))
            File.Move(temporaryPath, actualPath, True)
        End Sub

        Private Shared Function ResolvePath(pathValue As String) As String
            Return If(String.IsNullOrWhiteSpace(pathValue), DefaultPath, pathValue)
        End Function
    End Class
End Namespace

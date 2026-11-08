Imports System.IO
Imports System.IO.Compression
Imports System.Text.Json
Imports System.Threading
Imports System.Windows.Documents

Namespace TscLabel.Core
    Public NotInheritable Class LabelTemplateStore
        Private Shared ReadOnly JsonOptions As New JsonSerializerOptions With {
            .WriteIndented = True,
            .PropertyNameCaseInsensitive = True
        }

        Public Shared ReadOnly Property DefaultPath As String
            Get
                Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SOZO", "TSC-Etikettendruck", "TSC-Etikettendruck-Vorlagen.json")
            End Get
        End Property

        Public Shared Function Load(Optional pathValue As String = Nothing) As List(Of LabelTemplateDefinition)
            If Not String.IsNullOrWhiteSpace(pathValue) Then Return LoadUnlocked(pathValue)
            Dim gate As New Mutex(False, "Local\SOZO.TSC-Etikettendruck.TemplateStore.v1")
            Dim acquired = False
            Try
                acquired = gate.WaitOne(TimeSpan.FromSeconds(15))
                If Not acquired Then Throw New IOException("Der Vorlagenspeicher wird gerade von einem anderen Prozess aktualisiert.")
                Return LoadUnlocked(pathValue)
            Catch ex As AbandonedMutexException
                acquired = True
                Return LoadUnlocked(pathValue)
            Finally
                If acquired Then gate.ReleaseMutex()
                gate.Dispose()
            End Try
        End Function

        Private Shared Function LoadUnlocked(pathValue As String) As List(Of LabelTemplateDefinition)
            Dim actualPath = ResolvePath(pathValue)
            If String.IsNullOrWhiteSpace(pathValue) Then EnsureDefaultStoreMigrated()
            If Not File.Exists(actualPath) Then Return New List(Of LabelTemplateDefinition)()
            Try
                Dim jsonText = File.ReadAllText(actualPath)
                Dim entries = JsonSerializer.Deserialize(Of List(Of LabelTemplateDefinition))(jsonText, JsonOptions)
                Dim idsNeedPersistence = False
                Using parsed = JsonDocument.Parse(jsonText)
                    If parsed.RootElement.ValueKind = JsonValueKind.Array Then
                        idsNeedPersistence = parsed.RootElement.EnumerateArray().Any(Function(item)
                            Dim idProperty As JsonElement
                            Return Not item.TryGetProperty("Id", idProperty) OrElse String.IsNullOrWhiteSpace(idProperty.GetString())
                        End Function)
                    End If
                End Using
                Dim normalized = If(entries, New List(Of LabelTemplateDefinition)()).
                    Where(Function(entry) entry IsNot Nothing AndAlso
                        Not String.IsNullOrWhiteSpace(entry.Name) AndAlso
                        Not String.IsNullOrWhiteSpace(entry.PrinterId) AndAlso
                        Not String.IsNullOrWhiteSpace(entry.DocumentXaml)).
                    ToList()
                Dim seenIds As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                For Each entry In normalized
                    If String.IsNullOrWhiteSpace(entry.Id) OrElse Not seenIds.Add(entry.Id.Trim()) Then
                        entry.Id = Guid.NewGuid().ToString("N")
                        seenIds.Add(entry.Id)
                        idsNeedPersistence = True
                    Else
                        entry.Id = entry.Id.Trim()
                    End If
                    entry.PrinterIds = If(entry.PrinterIds, New List(Of String)())
                    entry.PrinterNames = If(entry.PrinterNames, New List(Of String)())
                    If entry.PrinterIds.Count = 0 AndAlso Not String.IsNullOrWhiteSpace(entry.PrinterId) Then entry.PrinterIds.Add(entry.PrinterId)
                    If entry.PrinterNames.Count = 0 AndAlso Not String.IsNullOrWhiteSpace(entry.PrinterName) Then entry.PrinterNames.Add(entry.PrinterName)
                Next
                Dim result = normalized.OrderBy(Function(entry) entry.Name, StringComparer.CurrentCultureIgnoreCase).ToList()
                If String.IsNullOrWhiteSpace(pathValue) AndAlso idsNeedPersistence Then Save(result, actualPath)
                Return result
            Catch ex As JsonException
                Throw New InvalidDataException("Die Vorlagendatei ist beschädigt und wurde nicht verändert.", ex)
            End Try
        End Function

        Public Shared Sub Save(entries As IEnumerable(Of LabelTemplateDefinition), Optional pathValue As String = Nothing)
            Dim actualPath = ResolvePath(pathValue)
            If Not String.Equals(actualPath, DefaultPath, StringComparison.OrdinalIgnoreCase) Then
                SaveUnlocked(entries, actualPath)
                Return
            End If
            Dim gate As New Mutex(False, "Local\SOZO.TSC-Etikettendruck.TemplateStore.v1")
            Dim acquired = False
            Try
                acquired = gate.WaitOne(TimeSpan.FromSeconds(15))
                If Not acquired Then Throw New IOException("Der Vorlagenspeicher wird gerade von einem anderen Prozess aktualisiert.")
                SaveUnlocked(entries, actualPath)
            Catch ex As AbandonedMutexException
                acquired = True
                SaveUnlocked(entries, actualPath)
            Finally
                If acquired Then gate.ReleaseMutex()
                gate.Dispose()
            End Try
        End Sub

        Private Shared Sub SaveUnlocked(entries As IEnumerable(Of LabelTemplateDefinition), actualPath As String)
            Dim directoryPath = Path.GetDirectoryName(actualPath)
            If Not String.IsNullOrWhiteSpace(directoryPath) Then Directory.CreateDirectory(directoryPath)
            Dim temporaryPath = actualPath & "." & Guid.NewGuid().ToString("N") & ".tmp"
            Dim ordered = entries.Where(Function(entry) entry IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(entry.Name)).
                GroupBy(Function(entry) If(String.IsNullOrWhiteSpace(entry.Id), Guid.NewGuid().ToString("N"), entry.Id.Trim()), StringComparer.OrdinalIgnoreCase).
                Select(Function(group) group.OrderByDescending(Function(entry) entry.UpdatedAt).First()).
                OrderBy(Function(entry) entry.PrinterId, StringComparer.OrdinalIgnoreCase).
                ThenBy(Function(entry) entry.Name, StringComparer.CurrentCultureIgnoreCase).
                ToList()
            For Each entry In ordered
                If String.IsNullOrWhiteSpace(entry.Id) Then entry.Id = Guid.NewGuid().ToString("N")
            Next
            If String.Equals(actualPath, DefaultPath, StringComparison.OrdinalIgnoreCase) AndAlso File.Exists(actualPath) Then BackupDefaultStore(actualPath)
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(ordered, JsonOptions))
            File.Move(temporaryPath, actualPath, True)
        End Sub

        Private Shared Sub EnsureDefaultStoreMigrated()
            If File.Exists(DefaultPath) Then Return
            Dim candidates As New List(Of String) From {
                Path.Combine(AppContext.BaseDirectory, "TSC-Etikettendruck-Vorlagen.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SOZO BrainOffice Bridge", "programs", "labels", "data", "TSC-Etikettendruck-Vorlagen.json")
            }
            Dim appPath = "E:\AI\APPS\TSC-Etikettendruck-Vorlagen.json"
            If File.Exists(appPath) Then candidates.Add(appPath)
            Dim merged As New List(Of LabelTemplateDefinition)()
            For Each candidate In candidates.Distinct(StringComparer.OrdinalIgnoreCase)
                If Not File.Exists(candidate) Then Continue For
                For Each entry In Load(candidate)
                    Dim existing = merged.FirstOrDefault(Function(item) String.Equals(item.Id, entry.Id, StringComparison.OrdinalIgnoreCase))
                    If existing Is Nothing Then
                        merged.Add(entry)
                    ElseIf entry.UpdatedAt > existing.UpdatedAt Then
                        merged(merged.IndexOf(existing)) = entry
                    End If
                Next
            Next
            If merged.Count > 0 Then Save(merged, DefaultPath)
        End Sub

        Private Shared Sub BackupDefaultStore(pathValue As String)
            Try
                Dim backupDirectory = Path.Combine(Path.GetDirectoryName(pathValue), "Backups")
                Directory.CreateDirectory(backupDirectory)
                Dim backupPath = Path.Combine(backupDirectory, $"TSC-Etikettendruck-Vorlagen-{DateTime.Now:yyyyMMdd-HHmmssfff}.json")
                File.Copy(pathValue, backupPath, False)
                For Each stale In New DirectoryInfo(backupDirectory).GetFiles("TSC-Etikettendruck-Vorlagen-*.json").OrderByDescending(Function(item) item.LastWriteTimeUtc).Skip(20)
                    stale.Delete()
                Next
            Catch
                ' Eine Sicherung ist best effort; atomisches Speichern bleibt davon unabhängig.
            End Try
        End Sub

        Public Shared Sub SavePackage(entries As IEnumerable(Of LabelTemplateDefinition), pathValue As String)
            If String.IsNullOrWhiteSpace(pathValue) Then Throw New ArgumentException("Der Exportpfad fehlt.", NameOf(pathValue))
            If Not String.Equals(Path.GetExtension(pathValue), ".zip", StringComparison.OrdinalIgnoreCase) Then
                Save(entries, pathValue)
                Return
            End If
            Dim directoryPath = Path.GetDirectoryName(pathValue)
            If Not String.IsNullOrWhiteSpace(directoryPath) Then Directory.CreateDirectory(directoryPath)
            Dim temporaryJson = Path.Combine(Path.GetTempPath(), "TSC-Etikettendruck-Vorlagen-" & Guid.NewGuid().ToString("N") & ".json")
            Dim temporaryZip = pathValue & ".tmp"
            Try
                Save(entries, temporaryJson)
                If File.Exists(temporaryZip) Then File.Delete(temporaryZip)
                Using archive = ZipFile.Open(temporaryZip, ZipArchiveMode.Create)
                    archive.CreateEntryFromFile(temporaryJson, "TSC-Etikettendruck-Vorlagen.json", CompressionLevel.Optimal)
                    Dim info = archive.CreateEntry("README.txt", CompressionLevel.Optimal)
                    Using writer As New StreamWriter(info.Open())
                        writer.WriteLine("Etikettenstudio-Vorlagenpaket")
                        writer.WriteLine("Import über Einstellungen > Vorlagen importieren.")
                    End Using
                End Using
                File.Move(temporaryZip, pathValue, True)
            Finally
                If File.Exists(temporaryJson) Then File.Delete(temporaryJson)
                If File.Exists(temporaryZip) Then File.Delete(temporaryZip)
            End Try
        End Sub

        Public Shared Function LoadPackage(pathValue As String) As List(Of LabelTemplateDefinition)
            If Not String.Equals(Path.GetExtension(pathValue), ".zip", StringComparison.OrdinalIgnoreCase) Then Return Load(pathValue)
            Dim temporaryJson = Path.Combine(Path.GetTempPath(), "TSC-Etikettendruck-Import-" & Guid.NewGuid().ToString("N") & ".json")
            Try
                Using archive = ZipFile.OpenRead(pathValue)
                    Dim entry = archive.Entries.FirstOrDefault(Function(item) String.Equals(item.Name, "TSC-Etikettendruck-Vorlagen.json", StringComparison.OrdinalIgnoreCase))
                    If entry Is Nothing Then Throw New InvalidDataException("Das ZIP enthält keine Etikettenstudio-Vorlagensammlung.")
                    Using source = entry.Open(), target = File.Create(temporaryJson)
                        source.CopyTo(target)
                    End Using
                End Using
                Return Load(temporaryJson)
            Catch ex As InvalidDataException
                Throw
            Catch ex As Exception
                Throw New InvalidDataException("Das Vorlagen-ZIP konnte nicht gelesen werden.", ex)
            Finally
                If File.Exists(temporaryJson) Then File.Delete(temporaryJson)
            End Try
        End Function

        Public Shared Function RestoreDocument(entry As LabelTemplateDefinition) As FlowDocument
            If entry Is Nothing OrElse String.IsNullOrWhiteSpace(entry.DocumentXaml) Then Throw New InvalidDataException("Die Vorlage enthält kein Etikett.")
            Try
                Return DocumentImageCodec.Deserialize(entry.DocumentXaml)
            Catch ex As Exception
                Throw New InvalidDataException("Die Vorlage konnte nicht wiederhergestellt werden.", ex)
            End Try
        End Function

        Private Shared Function ResolvePath(pathValue As String) As String
            Return If(String.IsNullOrWhiteSpace(pathValue), DefaultPath, pathValue)
        End Function
    End Class
End Namespace

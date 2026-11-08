Imports System.IO
Imports System.Text.Json

Namespace TscLabel.Core
    Public Enum BrainOfficeLabelCommandMode
        None
        ListTemplates
        OpenTemplate
    End Enum

    Public NotInheritable Class BrainOfficeLabelCommand
        Public Property Mode As BrainOfficeLabelCommandMode
        Public Property PatientJsonPath As String = ""
        Public Property TemplateId As String = ""
        Public Property TemplateName As String = ""
        Public Property RequestId As String = ""
        Public Property ResultPath As String = ""
        Public Property AutoPrint As Boolean

        Public Shared Function IsV1Command(args As IEnumerable(Of String)) As Boolean
            Return args.Any(Function(value) String.Equals(value, "--brainoffice-list-templates", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(value, "--brainoffice-open-template", StringComparison.OrdinalIgnoreCase))
        End Function

        Public Shared Function Parse(args As IReadOnlyList(Of String)) As BrainOfficeLabelCommand
            Dim result As New BrainOfficeLabelCommand()
            If args.Any(Function(value) String.Equals(value, "--brainoffice-list-templates", StringComparison.OrdinalIgnoreCase)) Then result.Mode = BrainOfficeLabelCommandMode.ListTemplates
            If args.Any(Function(value) String.Equals(value, "--brainoffice-open-template", StringComparison.OrdinalIgnoreCase)) Then
                If result.Mode <> BrainOfficeLabelCommandMode.None Then Throw New ArgumentException("Es darf nur ein BrainOffice-Befehl angegeben werden.")
                result.Mode = BrainOfficeLabelCommandMode.OpenTemplate
            End If
            If result.Mode = BrainOfficeLabelCommandMode.None Then Throw New ArgumentException("BrainOffice-Befehl fehlt.")

            Dim values As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim index = 0
            While index < args.Count
                Dim key = args(index)
                If Not key.StartsWith("--", StringComparison.Ordinal) OrElse String.Equals(key, "--brainoffice-list-templates", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(key, "--brainoffice-open-template", StringComparison.OrdinalIgnoreCase) Then
                    index += 1
                    Continue While
                End If
                If index + 1 >= args.Count Then Throw New ArgumentException($"Wert für {key} fehlt.")
                values(key) = args(index + 1)
                index += 2
            End While
            result.ResultPath = Required(values, "--result-path")
            If Not Path.IsPathFullyQualified(result.ResultPath) Then Throw New ArgumentException("result-path muss ein absoluter Pfad sein.")
            If result.Mode = BrainOfficeLabelCommandMode.OpenTemplate Then
                result.PatientJsonPath = Required(values, "--patient-json")
                result.TemplateId = Required(values, "--template-id")
                result.TemplateName = If(values.GetValueOrDefault("--template-name"), "")
                result.RequestId = Required(values, "--request-id")
                Dim autoPrintValue = Required(values, "--auto-print")
                If Not String.Equals(autoPrintValue, "false", StringComparison.OrdinalIgnoreCase) Then Throw New ArgumentException("Autodruck ist für Schnellwahlen nicht erlaubt.")
                result.AutoPrint = False
                If Not Path.IsPathFullyQualified(result.PatientJsonPath) Then Throw New ArgumentException("patient-json muss ein absoluter Pfad sein.")
            End If
            Return result
        End Function

        Private Shared Function Required(values As Dictionary(Of String, String), key As String) As String
            Dim value = values.GetValueOrDefault(key)
            If String.IsNullOrWhiteSpace(value) Then Throw New ArgumentException($"Pflichtparameter {key} fehlt.")
            Return value.Trim()
        End Function
    End Class

    Public NotInheritable Class BrainOfficeTemplateCatalogItem
        Public Property Id As String = ""
        Public Property Name As String = ""
        Public Property PrinterName As String = ""
        Public Property Available As Boolean
    End Class

    Public NotInheritable Class BrainOfficeTemplateCatalog
        Public Property Schema As String = "label-template-launch-v1"
        Public Property Capability As String = "label-template-launch-v1"
        Public Property Templates As New List(Of BrainOfficeTemplateCatalogItem)()
    End Class

    Public NotInheritable Class BrainOfficeTemplateLaunchResult
        Public Property RequestId As String = ""
        Public Property Accepted As Boolean
        Public Property TemplateResolved As Boolean
        Public Property TemplateId As String = ""
        Public Property TemplateName As String = ""
        Public Property PrinterName As String = ""
        Public Property Message As String = ""
    End Class

    Public NotInheritable Class BrainOfficeResultWriter
        Private Shared ReadOnly Options As New JsonSerializerOptions With {.WriteIndented = True, .PropertyNamingPolicy = JsonNamingPolicy.CamelCase}

        Public Shared Sub WriteAtomic(pathValue As String, value As Object)
            If String.IsNullOrWhiteSpace(pathValue) OrElse Not Path.IsPathFullyQualified(pathValue) Then Throw New ArgumentException("Ungültiger Ergebnisdateipfad.")
            Dim directoryPath = Path.GetDirectoryName(pathValue)
            If String.IsNullOrWhiteSpace(directoryPath) Then Throw New ArgumentException("Ergebnisordner fehlt.")
            Directory.CreateDirectory(directoryPath)
            Dim temporaryPath = Path.Combine(directoryPath, "." & Path.GetFileName(pathValue) & "." & Guid.NewGuid().ToString("N") & ".tmp")
            Try
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, value.GetType(), Options))
                File.Move(temporaryPath, pathValue, True)
            Finally
                If File.Exists(temporaryPath) Then File.Delete(temporaryPath)
            End Try
        End Sub
    End Class

    Public NotInheritable Class BrainOfficeTemplateCatalogService
        Public Shared Function Build(settings As AppSettings) As BrainOfficeTemplateCatalog
            Dim catalog As New BrainOfficeTemplateCatalog()
            Dim printers = If(settings?.Printers, New List(Of PrinterProfile)())
            Dim templates = LabelTemplateStore.Load()
            Dim sharedPath = If(settings?.SharedTemplatePath, "").Trim()
            If Not String.IsNullOrWhiteSpace(sharedPath) AndAlso File.Exists(sharedPath) Then
                Try
                    templates.AddRange(LabelTemplateStore.Load(sharedPath))
                Catch
                    ' Ein nicht erreichbarer zentraler Speicher darf den lokalen Katalog nicht verhindern.
                End Try
            End If
            For Each template In templates.OrderBy(Function(item) item.Name, StringComparer.CurrentCultureIgnoreCase)
                Dim printer = printers.FirstOrDefault(Function(item) String.Equals(item.Id, template.PrinterId, StringComparison.OrdinalIgnoreCase))
                catalog.Templates.Add(New BrainOfficeTemplateCatalogItem With {
                    .Id = template.Id,
                    .Name = template.Name,
                    .PrinterName = If(printer?.Name, template.PrinterName),
                    .Available = printer IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(template.DocumentXaml)
                })
            Next
            Return catalog
        End Function
    End Class
End Namespace

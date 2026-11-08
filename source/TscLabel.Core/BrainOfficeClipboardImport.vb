Imports System.Globalization
Imports System.Text.Json

Namespace TscLabel.Core
    Public NotInheritable Class BrainOfficeClipboardTransfer
        Public Const SupportedSchema As String = "sozo.brainoffice.patient-label.v1"

        Public Property Schema As String = ""
        Public Property CreatedAt As String = ""
        Public Property PatientId As String = ""
        Public Property Patient As New PatientLabelData()
        Public Property Copies As Integer = 1
        Public Property Profile As String = ""
        Public Property AutoPrint As Boolean

        Public ReadOnly Property DedupeKey As String
            Get
                Return CreatedAt.Trim() & "|" & PatientId.Trim()
            End Get
        End Property
    End Class

    Public NotInheritable Class BrainOfficeClipboardImport
        Public Shared Function TryParse(json As String,
                                        ByRef transfer As BrainOfficeClipboardTransfer,
                                        ByRef errorMessage As String) As Boolean
            transfer = Nothing
            errorMessage = ""
            If String.IsNullOrWhiteSpace(json) Then
                errorMessage = "Die Zwischenablage enthält keinen Text."
                Return False
            End If

            Try
                Using document = JsonDocument.Parse(json)
                    Dim root = document.RootElement
                    If root.ValueKind <> JsonValueKind.Object Then
                        errorMessage = "Die Zwischenablage enthält kein BrainOffice-Übergabeobjekt."
                        Return False
                    End If

                    Dim schema = ReadString(root, "schema")
                    If Not String.Equals(schema, BrainOfficeClipboardTransfer.SupportedSchema, StringComparison.Ordinal) Then
                        errorMessage = "Dieses Übergabeformat wird nicht unterstützt."
                        Return False
                    End If

                    Dim patientElement As JsonElement
                    If Not root.TryGetProperty("patient", patientElement) OrElse patientElement.ValueKind <> JsonValueKind.Object Then
                        errorMessage = "Im BrainOffice-Übergabeformat fehlen die Patientendaten."
                        Return False
                    End If

                    Dim createdAt = ReadString(root, "createdAt")
                    Dim patientId = ReadString(patientElement, "id")
                    If createdAt = "" OrElse patientId = "" Then
                        errorMessage = "In der BrainOffice-Übergabe fehlen createdAt oder patient.id."
                        Return False
                    End If
                    Dim rawBirthDate = ReadString(patientElement, "birthDate")
                    Dim birthDate = NormalizeBirthDate(rawBirthDate)
                    If rawBirthDate <> "" AndAlso birthDate = "" Then
                        errorMessage = "Das Geburtsdatum muss im Format yyyy-MM-dd übergeben werden."
                        Return False
                    End If
                    Dim firstName = ReadString(patientElement, "firstName")
                    Dim lastName = ReadString(patientElement, "lastName")
                    Dim title = ReadString(patientElement, "title")
                    Dim result As New BrainOfficeClipboardTransfer With {
                        .Schema = schema,
                        .CreatedAt = createdAt,
                        .PatientId = patientId,
                        .Patient = New PatientLabelData With {
                            .PatientId = ReadString(patientElement, "id"),
                            .Title = title,
                            .FirstName = firstName,
                            .Surname = lastName,
                            .DisplayName = JoinNonEmpty({title, firstName, lastName}),
                            .BirthDate = birthDate,
                            .InsuranceNumber = ReadString(patientElement, "insuranceNumber"),
                            .InsuranceProvider = ReadString(patientElement, "insuranceProvider"),
                            .Street = ReadString(patientElement, "street"),
                            .PostalCode = ReadString(patientElement, "postalCode"),
                            .City = ReadString(patientElement, "city"),
                            .Phone = ReadString(patientElement, "phone"),
                            .Email = ReadString(patientElement, "email")
                        }
                    }

                    Dim printElement As JsonElement
                    If root.TryGetProperty("print", printElement) AndAlso printElement.ValueKind = JsonValueKind.Object Then
                        Dim copiesElement As JsonElement
                        If printElement.TryGetProperty("copies", copiesElement) AndAlso copiesElement.ValueKind = JsonValueKind.Number Then
                            Dim parsedCopies As Integer
                            If copiesElement.TryGetInt32(parsedCopies) Then result.Copies = Math.Clamp(parsedCopies, 1, 3)
                        End If
                        result.Profile = ReadString(printElement, "profile")
                        Dim autoPrintElement As JsonElement
                        If printElement.TryGetProperty("autoPrint", autoPrintElement) AndAlso
                           (autoPrintElement.ValueKind = JsonValueKind.True OrElse autoPrintElement.ValueKind = JsonValueKind.False) Then
                            result.AutoPrint = autoPrintElement.GetBoolean()
                        End If
                    End If

                    transfer = result
                    Return True
                End Using
            Catch ex As JsonException
                errorMessage = "Der BrainOffice-Inhalt ist kein gültiges JSON."
                Return False
            End Try
        End Function

        Private Shared Function NormalizeBirthDate(value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return ""
            Dim parsed As DateTime
            If Not DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then Return ""
            Return parsed.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
        End Function

        Private Shared Function ReadString(element As JsonElement, name As String) As String
            Dim value As JsonElement
            If Not element.TryGetProperty(name, value) OrElse value.ValueKind <> JsonValueKind.String Then Return ""
            Return If(value.GetString(), "").Trim()
        End Function

        Private Shared Function JoinNonEmpty(values As IEnumerable(Of String)) As String
            Return String.Join(" ", values.Where(Function(value) Not String.IsNullOrWhiteSpace(value)).Select(Function(value) value.Trim()))
        End Function
    End Class
End Namespace

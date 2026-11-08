Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.Runtime.CompilerServices
Imports System.IO
Imports System.Text.Json.Serialization
Imports System.Windows.Media.Imaging

Namespace TscLabel.Core
    Public NotInheritable Class PrinterProfile
        Implements INotifyPropertyChanged

        Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

        Private Sub SetDisplayField(Of T)(ByRef field As T, value As T, <CallerMemberName> Optional propertyName As String = Nothing)
            If EqualityComparer(Of T).Default.Equals(field, value) Then Return
            field = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(ConnectionText)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(UsesWindowsQueue)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(UsesWindowsRaster)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(UsesSupvanUsb)))
        End Sub
        Public Const TcpRawConnection As String = "TCP_RAW"
        Public Const WindowsQueueConnection As String = "WINDOWS_QUEUE"
        Public Const SupvanUsbConnection As String = "SUPVAN_USB"
        Public Const TsplLanguage As String = "TSPL"
        Public Const ZplLanguage As String = "ZPL"
        Public Const WindowsRasterLanguage As String = "WINDOWS_RASTER"
        Public Const SupvanRasterLanguage As String = "SUPVAN_RASTER"

        Public Property Id As String = Guid.NewGuid().ToString("N")
        Private _Name As String = "Etikettendrucker"
        Public Property Name As String
            Get
                Return _Name
            End Get
            Set(value As String)
                SetDisplayField(_Name, value)
            End Set
        End Property
        Private _ConnectionMode As String = TcpRawConnection
        Public Property ConnectionMode As String
            Get
                Return _ConnectionMode
            End Get
            Set(value As String)
                SetDisplayField(_ConnectionMode, value)
            End Set
        End Property
        Private _Host As String = "192.168.1.76"
        Public Property Host As String
            Get
                Return _Host
            End Get
            Set(value As String)
                SetDisplayField(_Host, value)
            End Set
        End Property
        Private _Port As Integer = 9100
        Public Property Port As Integer
            Get
                Return _Port
            End Get
            Set(value As Integer)
                SetDisplayField(_Port, value)
            End Set
        End Property
        Private _QueueName As String = ""
        Public Property QueueName As String
            Get
                Return _QueueName
            End Get
            Set(value As String)
                SetDisplayField(_QueueName, value)
            End Set
        End Property
        Private _SupvanDeviceId As String = ""
        Public Property SupvanDeviceId As String
            Get
                Return _SupvanDeviceId
            End Get
            Set(value As String)
                SetDisplayField(_SupvanDeviceId, value)
            End Set
        End Property
        Public Property SupvanDevicePath As String = ""
        Public Property SupvanSerialNumber As String = ""
        Private _SupvanAlias As String = ""
        Public Property SupvanAlias As String
            Get
                Return _SupvanAlias
            End Get
            Set(value As String)
                SetDisplayField(_SupvanAlias, value)
            End Set
        End Property
        Public Property SupvanVid As Integer
        Public Property SupvanPid As Integer
        Public Property SupvanManufacturer As String = ""
        Public Property SupvanProductName As String = ""
        Private _Language As String = TsplLanguage
        Public Property Language As String
            Get
                Return _Language
            End Get
            Set(value As String)
                SetDisplayField(_Language, value)
            End Set
        End Property
        Public Property Dpi As Integer = 203
        Public Property Density As Integer = 8
        Public Property Speed As Integer = 4
        Public Property MarginMm As Double = 1.5
        Public Property PreferredProfileId As String = ""
        Public Property SwapLabelOrientation As Boolean
        Public Property RasterRotationDegrees As Integer
        Public Property CutterEnabled As Boolean

        Public ReadOnly Property ConnectionText As String
            Get
                If String.Equals(ConnectionMode, WindowsQueueConnection, StringComparison.OrdinalIgnoreCase) Then
                    Dim outputName = If(String.Equals(Language, WindowsRasterLanguage, StringComparison.OrdinalIgnoreCase), "Windows-Bilddruck", Language)
                    Return $"{outputName} · Windows · {QueueName}"
                End If
                If String.Equals(ConnectionMode, SupvanUsbConnection, StringComparison.OrdinalIgnoreCase) Then
                    Dim identity = If(String.IsNullOrWhiteSpace(SupvanAlias), SupvanDeviceId, SupvanAlias)
                    If String.IsNullOrWhiteSpace(identity) Then identity = "kein Gerät gewählt"
                    Return $"SUPVAN-USB · {identity}"
                End If
                Return $"{Language} · RAW · {Host}:{Port}"
            End Get
        End Property

        Public ReadOnly Property UsesWindowsQueue As Boolean
            Get
                Return String.Equals(ConnectionMode, WindowsQueueConnection, StringComparison.OrdinalIgnoreCase)
            End Get
        End Property

        Public ReadOnly Property UsesWindowsRaster As Boolean
            Get
                Return UsesWindowsQueue AndAlso String.Equals(Language, WindowsRasterLanguage, StringComparison.OrdinalIgnoreCase)
            End Get
        End Property

        Public ReadOnly Property UsesSupvanUsb As Boolean
            Get
                Return String.Equals(ConnectionMode, SupvanUsbConnection, StringComparison.OrdinalIgnoreCase)
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return $"{Name}  ·  {ConnectionText}"
        End Function

        Public Function Copy() As PrinterProfile
            Return New PrinterProfile With {
                .Id = Id,
                .Name = Name,
                .ConnectionMode = ConnectionMode,
                .Host = Host,
                .Port = Port,
                .QueueName = QueueName,
                .SupvanDeviceId = SupvanDeviceId,
                .SupvanDevicePath = SupvanDevicePath,
                .SupvanSerialNumber = SupvanSerialNumber,
                .SupvanAlias = SupvanAlias,
                .SupvanVid = SupvanVid,
                .SupvanPid = SupvanPid,
                .SupvanManufacturer = SupvanManufacturer,
                .SupvanProductName = SupvanProductName,
                .Language = Language,
                .Dpi = Dpi,
                .Density = Density,
                .Speed = Speed,
                .MarginMm = MarginMm,
                .PreferredProfileId = PreferredProfileId,
                .SwapLabelOrientation = SwapLabelOrientation,
                .RasterRotationDegrees = RasterRotationDegrees,
                .CutterEnabled = CutterEnabled
            }
        End Function
    End Class

    Public NotInheritable Class LabelProfile
        Public Property Id As String = Guid.NewGuid().ToString("N")
        Public Property Name As String = "Eigenes Format"
        Public Property WidthMm As Double = 50
        Public Property HeightMm As Double = 30
        Public Property GapMm As Double = 3
        Public Property IsCustom As Boolean

        Public Overrides Function ToString() As String
            Return $"{Name}  ·  {WidthMm:0.##} × {HeightMm:0.##} mm"
        End Function

        Public Function Copy() As LabelProfile
            Return New LabelProfile With {
                .Id = Id,
                .Name = Name,
                .WidthMm = WidthMm,
                .HeightMm = HeightMm,
                .GapMm = GapMm,
                .IsCustom = IsCustom
            }
        End Function

        Public Shared Function StandardProfiles() As IReadOnlyList(Of LabelProfile)
            Return New ReadOnlyCollection(Of LabelProfile)(New List(Of LabelProfile) From {
                Standard("std-50x30", "Standard 50 × 30", 50, 30, 3),
                Standard("std-50x25", "Standard 50 × 25", 50, 25, 3),
                Standard("std-zebra-zd410-50x25", "Zebra ZD410 50 × 25", 50, 25, 3),
                Standard("std-40x30", "Standard 40 × 30", 40, 30, 3),
                Standard("std-supvan-40x30", "SUPVAN 40 × 30", 40, 30, 0),
                Standard("std-40x25", "Standard 40 × 25", 40, 25, 3),
                Standard("std-38x25", "Röhrchen 38 × 25", 38, 25, 3),
                Standard("std-30x20", "Klein 30 × 20", 30, 20, 3),
                Standard("std-supvan-50x30", "SUPVAN 50 × 30", 50, 30, 0),
                Standard("std-supvan-50x20", "SUPVAN T50M 50 × 20", 50, 20, 0),
                Standard("std-supvan-50x80", "SUPVAN 50 × 80 · 90°", 50, 80, 0),
                Standard("std-54x30", "Breit 54 × 30", 54, 30, 3),
                Standard("std-25x50", "Hochformat 25 × 50", 25, 50, 3),
                Standard("std-dymo-99012", "99012 Large Address Dymo", 89, 36, 0),
                Standard("std-brother-dk11201", "Brother DK-11201 Standard Address", 90, 29, 0),
                Standard("std-brother-dk11209", "Brother DK-11209 Small Address", 62, 29, 0),
                Standard("std-brother-dk11202", "Brother DK-11202 Shipping", 100, 62, 0)
            })
        End Function

        Public Function CreateOriented(swapWidthAndHeight As Boolean) As LabelProfile
            Dim result = Copy()
            If swapWidthAndHeight Then
                result.WidthMm = HeightMm
                result.HeightMm = WidthMm
                result.Name &= " – 90° gedreht"
            End If
            Return result
        End Function

        Private Shared Function Standard(idValue As String, nameValue As String, widthValue As Double, heightValue As Double, gapValue As Double) As LabelProfile
            Return New LabelProfile With {
                .Id = idValue,
                .Name = nameValue,
                .WidthMm = widthValue,
                .HeightMm = heightValue,
                .GapMm = gapValue,
                .IsCustom = False
            }
        End Function
    End Class

    Public NotInheritable Class AppSettings
        Public Property PrinterIp As String = "192.168.1.76"
        Public Property PrinterPort As Integer = 9100
        Public Property Dpi As Integer = 203
        Public Property Density As Integer = 8
        Public Property Speed As Integer = 4
        Public Property MarginMm As Double = 1.5
        Public Property Threshold As Byte = 180
        Public Property CutterEnabled As Boolean
        Public Property NormalPrint As Boolean = True
        Public Property PrintModeSemanticsVersion As Integer
        Public Property HistoryEnabled As Boolean = True
        Public Property PracticeLine As String = "SOZO Vienna 01-928 4446"
        Public Property DownloadDirectory As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        Public Property SelectedProfileId As String = "std-50x25"
        Public Property CustomProfiles As New List(Of LabelProfile)()
        Public Property Printers As New List(Of PrinterProfile)()
        Public Property SelectedPrinterId As String = ""
        Public Property LastUsedTemplateId As String = ""
        Public Property SharedTemplatePath As String = ""
        Public Property TemplateSaveTarget As String = "LOCAL"
        Public Property WindowWidth As Double = 1240
        Public Property WindowHeight As Double = 880
    End Class

    Public NotInheritable Class OffisyOcrResult
        Public Sub New(dataValue As PatientLabelData, doctorNameValue As String, warningsValue As IReadOnlyList(Of String))
            Data = dataValue
            DoctorName = doctorNameValue
            Warnings = warningsValue
        End Sub

        Public ReadOnly Property Data As PatientLabelData
        Public ReadOnly Property DoctorName As String
        Public ReadOnly Property Warnings As IReadOnlyList(Of String)
    End Class

    Public NotInheritable Class PatientLabelData
        Public Property PatientId As String = ""
        Public Property Surname As String = ""
        Public Property FirstName As String = ""
        Public Property Title As String = ""
        Public Property DisplayName As String = ""
        Public Property InsuranceNumber As String = ""
        Public Property InsuranceProvider As String = ""
        Public Property BirthDate As String = ""
        Public Property PostalCode As String = ""
        Public Property City As String = ""
        Public Property Street As String = ""
        Public Property Phone As String = ""
        Public Property Email As String = ""

        Public ReadOnly Property HasIdentity As Boolean
            Get
                Return Not String.IsNullOrWhiteSpace(Surname) OrElse Not String.IsNullOrWhiteSpace(FirstName)
            End Get
        End Property
    End Class

    Public NotInheritable Class LaborImportResult
        Public Sub New(dataValue As PatientLabelData, sourcePathValue As String, warningsValue As IReadOnlyList(Of String))
            Data = dataValue
            SourcePath = sourcePathValue
            Warnings = warningsValue
        End Sub

        Public ReadOnly Property Data As PatientLabelData
        Public ReadOnly Property SourcePath As String
        Public ReadOnly Property Warnings As IReadOnlyList(Of String)
    End Class

    Public NotInheritable Class LabelRenderResult
        Public Sub New(previewValue As BitmapSource, packedValue As Byte(), widthDotsValue As Integer, heightDotsValue As Integer, widthBytesValue As Integer)
            Preview = previewValue
            PackedData = packedValue
            WidthDots = widthDotsValue
            HeightDots = heightDotsValue
            WidthBytes = widthBytesValue
        End Sub

        Public ReadOnly Property Preview As BitmapSource
        Public ReadOnly Property PackedData As Byte()
        Public ReadOnly Property WidthDots As Integer
        Public ReadOnly Property HeightDots As Integer
        Public ReadOnly Property WidthBytes As Integer
    End Class

    Public NotInheritable Class PrintHistoryEntry
        Public Property Id As String = Guid.NewGuid().ToString("N")
        Public Property PrintedAt As DateTimeOffset = DateTimeOffset.Now
        Public Property ProfileName As String = ""
        Public Property WidthMm As Double
        Public Property HeightMm As Double
        Public Property Copies As Integer = 1
        Public Property NormalPrint As Boolean
        Public Property PrinterName As String = ""
        Public Property PrinterLanguage As String = ""
        Public Property PlainText As String = ""
        Public Property DocumentXaml As String = ""

        Public Overrides Function ToString() As String
            Dim firstLine = PlainText.Replace(vbCr, "").Split(ControlChars.Lf).FirstOrDefault("").Trim()
            If firstLine.Length > 42 Then firstLine = firstLine.Substring(0, 39) & "..."
            Dim mode = If(NormalPrint, "Normal", "Invers")
            Dim printer = If(String.IsNullOrWhiteSpace(PrinterName), "", "  ·  " & PrinterName)
            Return $"{PrintedAt.LocalDateTime:dd.MM.yyyy HH:mm}  ·  {Copies}×  ·  {mode}{printer}  ·  {firstLine}"
        End Function
    End Class

    Public NotInheritable Class LabelTemplateDefinition
        Public Property Id As String = Guid.NewGuid().ToString("N")
        Public Property Name As String = "Neue Vorlage"
        Public Property PrinterId As String = ""
        Public Property PrinterName As String = ""
        Public Property PrinterIds As New List(Of String)()
        Public Property PrinterNames As New List(Of String)()
        Public Property ProfileId As String = ""
        Public Property ProfileName As String = ""
        Public Property WidthMm As Double
        Public Property HeightMm As Double
        Public Property DocumentXaml As String = ""
        Public Property UpdatedAt As DateTimeOffset = DateTimeOffset.Now
        <JsonIgnore>
        Public Property StoreScope As String = "LOCAL"
        <JsonIgnore>
        Public ReadOnly Property StoreScopeLabel As String
            Get
                Return If(String.Equals(StoreScope, "SHARED", StringComparison.OrdinalIgnoreCase), "Zentral", "Lokal")
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return $"{Name}  ·  {StoreScopeLabel}  ·  {WidthMm:0.##} × {HeightMm:0.##} mm"
        End Function
    End Class

    Public NotInheritable Class LabelPlaceholderDefinition
        Public Sub New(groupValue As String, labelValue As String, tokenValue As String)
            Group = groupValue
            Label = labelValue
            Token = tokenValue
        End Sub

        Public ReadOnly Property Group As String
        Public ReadOnly Property Label As String
        Public ReadOnly Property Token As String
    End Class
End Namespace

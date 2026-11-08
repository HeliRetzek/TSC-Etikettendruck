Namespace TscLabel.Core
    Public NotInheritable Class WindowsPrinterPreset
        Public Property Manufacturer As String = "Windows-Drucker"
        Public Property Language As String = PrinterProfile.WindowsRasterLanguage
        Public Property Dpi As Integer = 203
        Public Property PreferredProfileId As String = "std-50x25"
        Public Property RasterRotationDegrees As Integer
    End Class

    Public NotInheritable Class WindowsPrinterPresetDetector
        Public Shared Function Detect(printerName As String, driverName As String) As WindowsPrinterPreset
            Dim identity = (If(printerName, "") & " " & If(driverName, "")).ToUpperInvariant()

            If IsSupvan(identity) Then
                Return New WindowsPrinterPreset With {
                    .Manufacturer = "SUPVAN T50M",
                    .Language = PrinterProfile.WindowsRasterLanguage,
                    .Dpi = 203,
                    .PreferredProfileId = "std-50x30",
                    .RasterRotationDegrees = 0
                }
            End If

            If identity.Contains("DYMO", StringComparison.Ordinal) Then
                Return New WindowsPrinterPreset With {
                    .Manufacturer = "DYMO",
                    .Language = PrinterProfile.WindowsRasterLanguage,
                    .Dpi = 300,
                    .PreferredProfileId = "std-dymo-99012",
                    .RasterRotationDegrees = 90
                }
            End If

            If identity.Contains("BROTHER", StringComparison.Ordinal) OrElse
               identity.Contains("QL-820", StringComparison.Ordinal) OrElse
               identity.Contains("QL820", StringComparison.Ordinal) Then
                Return New WindowsPrinterPreset With {
                    .Manufacturer = "Brother QL",
                    .Language = PrinterProfile.WindowsRasterLanguage,
                    .Dpi = 300,
                    .PreferredProfileId = "std-brother-dk11201",
                    .RasterRotationDegrees = 90
                }
            End If

            If identity.Contains("ZEBRA", StringComparison.Ordinal) OrElse identity.Contains("ZDESIGNER", StringComparison.Ordinal) Then
                Dim dpi = If(identity.Contains("300DPI", StringComparison.Ordinal) OrElse identity.Contains("300 DPI", StringComparison.Ordinal), 300, 203)
                Return New WindowsPrinterPreset With {
                    .Manufacturer = If(IsZebraZd410(identity), "Zebra ZD410", "Zebra"),
                    .Language = PrinterProfile.ZplLanguage,
                    .Dpi = dpi,
                    .PreferredProfileId = If(IsZebraZd410(identity), "std-zebra-zd410-50x25", "std-50x25"),
                    .RasterRotationDegrees = 0
                }
            End If

            Return Nothing
        End Function

        Public Shared Function IsSupvan(identity As String) As Boolean
            Dim normalized = If(identity, "").ToUpperInvariant()
            Return normalized.Contains("SUPVAN", StringComparison.Ordinal) OrElse
                   normalized.Contains("KATASYMBOL", StringComparison.Ordinal) OrElse
                   normalized.Contains("T50M", StringComparison.Ordinal) OrElse
                   normalized.Contains("T50 M", StringComparison.Ordinal)
        End Function

        Public Shared Function IsZebraZd410(identity As String) As Boolean
            Dim normalized = If(identity, "").ToUpperInvariant()
            Return normalized.Contains("ZD410", StringComparison.Ordinal) OrElse normalized.Contains("ZD 410", StringComparison.Ordinal)
        End Function
    End Class
End Namespace

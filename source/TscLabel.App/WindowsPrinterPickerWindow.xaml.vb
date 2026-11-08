Imports System.Diagnostics
Imports System.Windows
Imports System.Windows.Input
Imports TscLabel.Core

Namespace TscLabel.App
    Public Partial Class WindowsPrinterPickerWindow
        Inherits Window

        Public Sub New(currentQueueName As String)
            InitializeComponent()
            RefreshPrinters(currentQueueName)
        End Sub

        Public Property SelectedQueueName As String = ""
        Public Property SelectedPrinterName As String = ""
        Public Property SelectedDriverName As String = ""

        Private Sub RefreshPrinters(preferredQueueName As String)
            Try
                StatusText.Text = "Windows-Drucker werden gelesen…"
                Dim printers = WindowsPrinterService.GetInstalledPrinters()
                PrinterList.ItemsSource = printers
                Dim selected = printers.FirstOrDefault(Function(item) String.Equals(item.FullName, preferredQueueName, StringComparison.OrdinalIgnoreCase) OrElse String.Equals(item.Name, preferredQueueName, StringComparison.OrdinalIgnoreCase))
                If selected Is Nothing Then selected = printers.FirstOrDefault(Function(item) item.IsDefault)
                If selected Is Nothing Then selected = printers.FirstOrDefault()
                PrinterList.SelectedItem = selected
                If selected IsNot Nothing Then PrinterList.ScrollIntoView(selected)
                StatusText.Text = If(printers.Count = 0, "Keine Windows-Drucker gefunden. Über ‚Windows-Drucker hinzufügen…‘ kann eine Freigabe verbunden werden.", $"{printers.Count} Windows-Drucker gefunden.")
            Catch ex As Exception
                PrinterList.ItemsSource = Nothing
                StatusText.Text = "Windows-Drucker konnten nicht gelesen werden: " & ex.Message
            End Try
        End Sub

        Private Sub RefreshClick(sender As Object, e As RoutedEventArgs)
            RefreshPrinters(SelectedQueueName)
        End Sub

        Private Sub OpenWindowsPrintersClick(sender As Object, e As RoutedEventArgs)
            Try
                Process.Start(New ProcessStartInfo("ms-settings:printers") With {.UseShellExecute = True})
                StatusText.Text = "Windows-Druckereinstellungen geöffnet. Nach dem Hinzufügen hier ‚Aktualisieren‘ wählen."
            Catch ex As Exception
                StatusText.Text = "Windows-Druckereinstellungen konnten nicht geöffnet werden: " & ex.Message
            End Try
        End Sub

        Private Sub PrinterListDoubleClick(sender As Object, e As MouseButtonEventArgs)
            ChooseSelectedPrinter()
        End Sub

        Private Sub ChooseClick(sender As Object, e As RoutedEventArgs)
            ChooseSelectedPrinter()
        End Sub

        Private Sub ChooseSelectedPrinter()
            Dim selected = TryCast(PrinterList.SelectedItem, WindowsPrinterInfo)
            If selected Is Nothing Then
                StatusText.Text = "Bitte zuerst einen Windows-Drucker auswählen."
                Return
            End If
            SelectedQueueName = selected.FullName
            SelectedPrinterName = selected.Name
            SelectedDriverName = selected.DriverName
            DialogResult = True
        End Sub

        Private Sub CancelClick(sender As Object, e As RoutedEventArgs)
            DialogResult = False
        End Sub
    End Class
End Namespace

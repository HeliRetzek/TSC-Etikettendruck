Imports System.Collections.Generic
Imports System.IO
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Windows
Imports System.Windows.Threading
Imports TscLabel.Core

Namespace TscLabel.App
    Partial Public Class Application
        Inherits System.Windows.Application

        Private _singleInstance As BrainOfficeSingleInstanceCoordinator

        Private Shared ReadOnly FallbackStartupLogPath As String = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SOZO",
            "TSC-Etikettendruck",
            "ETIKETTENSTUDIO-STARTFEHLER.txt")

        Protected Overrides Sub OnStartup(e As StartupEventArgs)
            AddHandler DispatcherUnhandledException, AddressOf OnDispatcherUnhandledException
            AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnDomainUnhandledException

            Try
                Dim launchCommand As BrainOfficeLabelCommand = Nothing
                If BrainOfficeLabelCommand.IsV1Command(e.Args) Then
                    Try
                        launchCommand = BrainOfficeLabelCommand.Parse(e.Args)
                    Catch ex As Exception
                        WriteCommandFailure(e.Args, ex.Message)
                        MyBase.OnStartup(e)
                        Shutdown(2)
                        Return
                    End Try
                    If launchCommand.Mode = BrainOfficeLabelCommandMode.ListTemplates Then
                        MyBase.OnStartup(e)
                        BrainOfficeResultWriter.WriteAtomic(launchCommand.ResultPath, BrainOfficeTemplateCatalogService.Build(SettingsStore.Load()))
                        Shutdown(0)
                        Return
                    End If

                    _singleInstance = New BrainOfficeSingleInstanceCoordinator()
                    If Not _singleInstance.IsPrimary Then
                        Dim forwarded = _singleInstance.ForwardAsync(launchCommand).GetAwaiter().GetResult()
                        If Not forwarded Then
                            BrainOfficeResultWriter.WriteAtomic(launchCommand.ResultPath, New BrainOfficeTemplateLaunchResult With {
                                .RequestId = launchCommand.RequestId,
                                .Accepted = False,
                                .Message = "Etikettenmaster antwortet nicht."
                            })
                        End If
                        MyBase.OnStartup(e)
                        Shutdown(If(forwarded, 0, 3))
                        Return
                    End If
                End If

                MyBase.OnStartup(e)
                Dim window As New MainWindow()
                Me.MainWindow = window
                If _singleInstance Is Nothing Then _singleInstance = New BrainOfficeSingleInstanceCoordinator()
                If _singleInstance.IsPrimary Then
                    _singleInstance.StartServer(Function(command) Dispatcher.InvokeAsync(Function() window.ProcessBrainOfficeTemplateLaunch(command)).Task)
                End If
                If launchCommand IsNot Nothing Then
                    AddHandler window.Loaded, Sub() window.ProcessBrainOfficeTemplateLaunch(launchCommand)
                End If
                window.Show()
            Catch ex As Exception
                ReportFatalStartupError(ex)
                Shutdown(-1)
            End Try
        End Sub

        Protected Overrides Sub OnExit(e As ExitEventArgs)
            _singleInstance?.Dispose()
            MyBase.OnExit(e)
        End Sub

        Private Shared Sub WriteCommandFailure(args As IReadOnlyList(Of String), message As String)
            Dim resultPath = ""
            For index = 0 To args.Count - 2
                If String.Equals(args(index), "--result-path", StringComparison.OrdinalIgnoreCase) Then resultPath = args(index + 1)
            Next
            If String.IsNullOrWhiteSpace(resultPath) OrElse Not Path.IsPathFullyQualified(resultPath) Then Return
            Try
                BrainOfficeResultWriter.WriteAtomic(resultPath, New BrainOfficeTemplateLaunchResult With {.Accepted = False, .Message = message})
            Catch
            End Try
        End Sub

        Private Sub OnDispatcherUnhandledException(sender As Object, e As DispatcherUnhandledExceptionEventArgs)
            e.Handled = True
            ReportFatalStartupError(e.Exception)
            Shutdown(-1)
        End Sub

        Private Shared Sub OnDomainUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
            Dim exceptionValue = TryCast(e.ExceptionObject, Exception)
            WriteStartupLog(If(exceptionValue, New Exception("Unbekannter schwerer Programmfehler.")))
        End Sub

        Private Shared Sub ReportFatalStartupError(ex As Exception)
            Dim report = BuildStartupReport(ex)
            Dim writtenPaths = WriteStartupLogs(report)
            Dim clipboardHint = ""
            Try
                Clipboard.SetText(report)
                clipboardHint = Environment.NewLine & "Die Fehlerdetails wurden außerdem in die Zwischenablage kopiert."
            Catch
                ' Die Zwischenablage kann voruebergehend durch ein anderes Programm gesperrt sein.
            End Try

            Dim reportHint = If(
                writtenPaths.Count > 0,
                "Fehlerbericht: " & writtenPaths(0),
                "Der Fehlerbericht konnte leider nicht als Datei gespeichert werden.")
            Try
                MessageBox.Show(
                    "Das Etikettenstudio konnte nicht gestartet werden." & Environment.NewLine & Environment.NewLine &
                    ex.GetType().Name & ": " & ex.Message & Environment.NewLine & Environment.NewLine &
                    reportHint & clipboardHint,
                    "Etikettenstudio – Startfehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error)
            Catch
                ' Auch bei einem sehr frühen WPF-Fehler muss zumindest die Logdatei erhalten bleiben.
            End Try
        End Sub

        Private Shared Sub WriteStartupLog(ex As Exception)
            WriteStartupLogs(BuildStartupReport(ex))
        End Sub

        Private Shared Function BuildStartupReport(ex As Exception) As String
            Dim assembly = GetType(Application).Assembly
            Dim informationalVersion = assembly.GetCustomAttribute(Of AssemblyInformationalVersionAttribute)()?.InformationalVersion
            Dim report As New StringBuilder()
            report.AppendLine("Etikettenstudio Startfehler")
            report.AppendLine($"Zeit: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}")
            report.AppendLine($"Version: {informationalVersion}")
            report.AppendLine($"Windows: {RuntimeInformation.OSDescription}")
            report.AppendLine($"Betriebssystem-Architektur: {RuntimeInformation.OSArchitecture}")
            report.AppendLine($"Programm-Architektur: {RuntimeInformation.ProcessArchitecture}")
            report.AppendLine($"EXE: {Environment.ProcessPath}")
            report.AppendLine("Fehler:")
            report.AppendLine(ex.ToString())
            report.AppendLine(New String("-"c, 72))
            Return report.ToString()
        End Function

        Private Shared Function WriteStartupLogs(report As String) As List(Of String)
            Dim writtenPaths As New List(Of String)()
            Dim candidatePaths As New List(Of String)()

            Dim executableDirectory = Path.GetDirectoryName(Environment.ProcessPath)
            If Not String.IsNullOrWhiteSpace(executableDirectory) Then
                candidatePaths.Add(Path.Combine(executableDirectory, "ETIKETTENSTUDIO-STARTFEHLER.txt"))
            End If

            Dim desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            If Not String.IsNullOrWhiteSpace(desktopDirectory) Then
                candidatePaths.Add(Path.Combine(desktopDirectory, "ETIKETTENSTUDIO-STARTFEHLER.txt"))
            End If
            candidatePaths.Add(FallbackStartupLogPath)

            Dim attemptedPaths As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each candidatePath In candidatePaths
                If Not attemptedPaths.Add(candidatePath) Then Continue For
                Try
                    Dim logDirectory = Path.GetDirectoryName(candidatePath)
                    If Not String.IsNullOrWhiteSpace(logDirectory) Then Directory.CreateDirectory(logDirectory)
                    File.AppendAllText(candidatePath, report, Encoding.UTF8)
                    writtenPaths.Add(candidatePath)
                Catch
                    ' Der naechste gut sichtbare Ablageort wird versucht.
                End Try
            Next
            Return writtenPaths
        End Function

    End Class
End Namespace

Imports System.IO
Imports System.IO.Pipes
Imports System.Security.Cryptography
Imports System.Security.Principal
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports TscLabel.Core

Namespace TscLabel.App
    Friend NotInheritable Class BrainOfficeSingleInstanceCoordinator
        Implements IDisposable

        Private Shared ReadOnly JsonOptions As New JsonSerializerOptions With {.PropertyNameCaseInsensitive = True, .PropertyNamingPolicy = JsonNamingPolicy.CamelCase}
        Private ReadOnly _pipeName As String
        Private ReadOnly _mutex As Mutex
        Private ReadOnly _ownsMutex As Boolean
        Private ReadOnly _cancellation As New CancellationTokenSource()

        Public Sub New()
            Dim sid = WindowsIdentity.GetCurrent().User?.Value
            If String.IsNullOrWhiteSpace(sid) Then sid = Environment.UserName
            Dim suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid))).Substring(0, 16)
            _pipeName = "SOZO.TSC.Etikettendruck." & suffix & ".v1"
            _mutex = New Mutex(False, "Local\" & _pipeName & ".mutex")
            Try
                _ownsMutex = _mutex.WaitOne(0)
            Catch ex As AbandonedMutexException
                _ownsMutex = True
            End Try
        End Sub

        Public ReadOnly Property IsPrimary As Boolean
            Get
                Return _ownsMutex
            End Get
        End Property

        Public Sub StartServer(handler As Func(Of BrainOfficeLabelCommand, Task(Of BrainOfficeTemplateLaunchResult)))
            If Not _ownsMutex Then Return
            Task.Run(Sub()
                While Not _cancellation.IsCancellationRequested
                    Try
                        Using pipe As New NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None)
                            pipe.WaitForConnection()
                            Using reader As New StreamReader(pipe, Encoding.UTF8, False, 4096, True),
                                  writer As New StreamWriter(pipe, New UTF8Encoding(False), 4096, True) With {.AutoFlush = True}
                                Dim line = reader.ReadLine()
                                If String.IsNullOrWhiteSpace(line) OrElse line.Length > 65536 Then Continue While
                                Dim command = JsonSerializer.Deserialize(Of BrainOfficeLabelCommand)(line, JsonOptions)
                                If command Is Nothing Then Continue While
                                Dim result = handler(command).GetAwaiter().GetResult()
                                writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions))
                            End Using
                        End Using
                    Catch
                        ' Der nächste sichere lokale Auftrag darf weiter angenommen werden.
                    End Try
                End While
            End Sub)
        End Sub

        Public Async Function ForwardAsync(command As BrainOfficeLabelCommand, Optional timeoutMilliseconds As Integer = 6000) As Task(Of Boolean)
            Dim deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds)
            Do
                Dim retry = False
                Try
                    Using pipe As New NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous)
                        Using timeout As New CancellationTokenSource(TimeSpan.FromMilliseconds(900))
                            Await pipe.ConnectAsync(timeout.Token).ConfigureAwait(False)
                        End Using
                        Using reader As New StreamReader(pipe, Encoding.UTF8, False, 4096, True),
                              writer As New StreamWriter(pipe, New UTF8Encoding(False), 4096, True) With {.AutoFlush = True}
                            Await writer.WriteLineAsync(JsonSerializer.Serialize(command, JsonOptions)).ConfigureAwait(False)
                            Using timeout As New CancellationTokenSource(TimeSpan.FromSeconds(12))
                                Dim response = Await reader.ReadLineAsync(timeout.Token).ConfigureAwait(False)
                                Return Not String.IsNullOrWhiteSpace(response)
                            End Using
                        End Using
                    End Using
                Catch
                    retry = True
                End Try
                If retry Then Await Task.Delay(150).ConfigureAwait(False)
            Loop While DateTime.UtcNow < deadline
            Return False
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            _cancellation.Cancel()
            If _ownsMutex Then
                Try
                    _mutex.ReleaseMutex()
                Catch
                End Try
            End If
            _mutex.Dispose()
            _cancellation.Dispose()
        End Sub
    End Class
End Namespace

Imports System.Windows
Imports System.Windows.Input

Namespace TscLabel.App
    Public Partial Class TextInputDialogWindow
        Inherits Window

        Public Sub New(titleValue As String, promptValue As String, initialValue As String, hintValue As String)
            InitializeComponent()
            DialogTitleText.Text = titleValue
            PromptText.Text = promptValue
            ValueBox.Text = If(initialValue, "")
            HintText.Text = If(hintValue, "")
            AddHandler Loaded,
                Sub()
                    ValueBox.Focus()
                    ValueBox.SelectAll()
                End Sub
        End Sub

        Public ReadOnly Property InputValue As String
            Get
                Return ValueBox.Text.Trim()
            End Get
        End Property

        Private Sub AcceptClick(sender As Object, e As RoutedEventArgs)
            If String.IsNullOrWhiteSpace(ValueBox.Text) Then
                ValueBox.Focus()
                Return
            End If
            DialogResult = True
        End Sub

        Private Sub CancelClick(sender As Object, e As RoutedEventArgs)
            DialogResult = False
        End Sub

        Private Sub ValueBoxKeyDown(sender As Object, e As KeyEventArgs)
            If e.Key = Key.Enter Then
                AcceptClick(sender, New RoutedEventArgs())
                e.Handled = True
            ElseIf e.Key = Key.Escape Then
                DialogResult = False
                e.Handled = True
            End If
        End Sub
    End Class
End Namespace

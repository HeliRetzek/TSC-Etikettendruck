Imports System.Windows
Imports System.Windows.Media

Namespace TscLabel.App
    Public Partial Class AppDialogWindow
        Inherits Window

        Public Sub New()
            InitializeComponent()
        End Sub

        Public Sub Configure(titleValue As String,
                             messageValue As String,
                             Optional contextValue As String = "",
                             Optional confirmText As String = "OK",
                             Optional cancelText As String = "Abbrechen",
                             Optional showCancel As Boolean = False,
                             Optional isWarning As Boolean = False)
            Title = titleValue
            DialogTitleText.Text = titleValue
            MessageText.Text = messageValue
            OkButton.Content = confirmText
            CancelButton.Content = cancelText
            CancelButton.Visibility = If(showCancel, Visibility.Visible, Visibility.Collapsed)
            ContextBorder.Visibility = If(String.IsNullOrWhiteSpace(contextValue), Visibility.Collapsed, Visibility.Visible)
            ContextText.Text = contextValue
            If isWarning Then
                AccentBar.Background = New SolidColorBrush(Color.FromRgb(182, 106, 19))
                IconText.Text = "!"
                IconText.Foreground = New SolidColorBrush(Color.FromRgb(130, 72, 10))
            Else
                AccentBar.Background = New SolidColorBrush(Color.FromRgb(15, 111, 115))
                IconText.Text = "i"
            End If
        End Sub

        Private Sub OkClick(sender As Object, e As RoutedEventArgs)
            DialogResult = True
        End Sub

        Private Sub CancelClick(sender As Object, e As RoutedEventArgs)
            DialogResult = False
        End Sub
    End Class
End Namespace

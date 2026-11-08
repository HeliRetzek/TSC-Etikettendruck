Imports System.Globalization
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Documents
Imports System.Windows.Media

Namespace TscLabel.App
    Public Class FormattingMarksOverlay
        Inherits FrameworkElement

        Public Property Editor As RichTextBox
        Public Property RenderedMarkCount As Integer

        Protected Overrides Sub OnRender(dc As DrawingContext)
            MyBase.OnRender(dc)
            RenderedMarkCount = 0
            If Editor Is Nothing OrElse Visibility <> Visibility.Visible Then Return
            dc.PushClip(New RectangleGeometry(New Rect(RenderSize)))
            Dim position = Editor.Document.ContentStart
            Dim visited = 0
            While position IsNot Nothing AndAlso position.CompareTo(Editor.Document.ContentEnd) < 0 AndAlso visited < 20000
                visited += 1
                Dim context = position.GetPointerContext(LogicalDirection.Forward)
                If context = TextPointerContext.Text Then
                    Dim text = position.GetTextInRun(LogicalDirection.Forward)
                    For index = 0 To Math.Min(text.Length, 20000 - visited) - 1
                        Dim mark = If(text(index) = " "c, ChrW(&HB7).ToString(), If(text(index) = ChrW(9), ChrW(&H2192).ToString(), ""))
                        If mark <> "" Then DrawMark(dc, position.GetPositionAtOffset(index), mark)
                    Next
                    visited += text.Length
                ElseIf context = TextPointerContext.ElementEnd AndAlso TypeOf position.Parent Is Paragraph Then
                    DrawMark(dc, position, ChrW(&HB6).ToString())
                ElseIf context = TextPointerContext.ElementStart AndAlso TypeOf position.GetAdjacentElement(LogicalDirection.Forward) Is LineBreak Then
                    DrawMark(dc, position, ChrW(&H21B5).ToString())
                End If
                position = position.GetNextContextPosition(LogicalDirection.Forward)
            End While
            dc.Pop()
        End Sub

        Private Sub DrawMark(dc As DrawingContext, position As TextPointer, mark As String)
            Dim rect = position.GetCharacterRect(LogicalDirection.Forward)
            If rect.IsEmpty OrElse rect.Height <= 0 Then Return
            Dim point = Editor.TranslatePoint(rect.TopLeft, Me)
            Dim size = Math.Min(10, Math.Max(5, rect.Height * 0.65))
            Dim text As New FormattedText(mark, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                New Typeface("Segoe UI"), size, Brushes.SteelBlue, VisualTreeHelper.GetDpi(Me).PixelsPerDip)
            dc.DrawText(text, New Point(point.X, point.Y + Math.Max(0, rect.Height - text.Height)))
            RenderedMarkCount += 1
        End Sub
    End Class
End Namespace

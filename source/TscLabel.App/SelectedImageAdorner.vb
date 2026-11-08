Imports System.Windows
Imports System.Windows.Documents
Imports System.Windows.Media

Namespace TscLabel.App
    Friend NotInheritable Class SelectedImageAdorner
        Inherits Adorner

        Private Shared ReadOnly OutlinePen As New Pen(New SolidColorBrush(Color.FromRgb(228, 122, 19)), 2.0)
        Private Shared ReadOnly HandleBrush As New SolidColorBrush(Color.FromRgb(255, 243, 230))
        Private Const HandleSize As Double = 7.0

        Public Sub New(adornedElement As UIElement)
            MyBase.New(adornedElement)
            IsHitTestVisible = False
            OutlinePen.Freeze()
            HandleBrush.Freeze()
        End Sub

        Protected Overrides Sub OnRender(drawingContext As DrawingContext)
            Dim bounds = New Rect(AdornedElement.RenderSize)
            drawingContext.DrawRectangle(Nothing, OutlinePen, bounds)
            DrawHandle(drawingContext, bounds.TopLeft)
            DrawHandle(drawingContext, bounds.TopRight)
            DrawHandle(drawingContext, bounds.BottomLeft)
            DrawHandle(drawingContext, bounds.BottomRight)
        End Sub

        Private Shared Sub DrawHandle(drawingContext As DrawingContext, center As Point)
            Dim half = HandleSize / 2.0
            drawingContext.DrawRectangle(HandleBrush, OutlinePen, New Rect(center.X - half, center.Y - half, HandleSize, HandleSize))
        End Sub
    End Class
End Namespace

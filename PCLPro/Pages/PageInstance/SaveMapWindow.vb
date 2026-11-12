Imports System.Windows.Input
Imports System.Windows.Media
Imports System.Windows.Media.Media3D

''' <summary>
''' 独立的 3D 地图预览窗口：把地形按方块列渲染成网格，支持旋转与缩放。
''' </summary>
Public Class SaveMapWindow
    Inherits Window

    Private ReadOnly Camera As PerspectiveCamera
    Private ReadOnly ViewCenter As Point3D
    Private ReadOnly MapSize As Integer
    Private Yaw As Double = 35
    Private Pitch As Double = 52
    Private Distance As Double
    Private IsDragging As Boolean
    Private DragPoint As Point
    Private DragYaw As Double
    Private DragPitch As Double

    Public Sub New(Model As Model3DGroup, Map As ModSave.TerrainMap, SaveName As String)
        Title = $"3D 地图预览 - {SaveName}"
        Width = 1040
        Height = 720
        MinWidth = 640
        MinHeight = 460
        WindowStartupLocation = WindowStartupLocation.CenterOwner
        Background = New SolidColorBrush(Color.FromRgb(18, 24, 32))

        MapSize = Math.Max(Map.Width, Map.Depth)
        ViewCenter = New Point3D(Map.Width / 2.0, (Map.MinY + Map.MaxY) / 2.0, Map.Depth / 2.0)
        Distance = MapSize * 1.3

        Dim Root As New Grid
        Dim Viewport As New Viewport3D
        Viewport.Children.Add(New ModelVisual3D With {.Content = Model})
        Camera = New PerspectiveCamera With {.FieldOfView = 50, .NearPlaneDistance = 1, .FarPlaneDistance = 30000}
        Viewport.Camera = Camera
        Root.Children.Add(Viewport)

        ' 左上角信息
        Dim InfoText As New TextBlock With {
            .Text = $"{Map.Width} × {Map.Depth} 方块 · 高度 {Map.MinY} ~ {Map.MaxY} · 覆盖率 {Map.Coverage}%",
            .FontSize = 12,
            .Margin = New Thickness(12, 6, 12, 6)
        }
        InfoText.Foreground = New SolidColorBrush(Color.FromRgb(205, 215, 225))
        Root.Children.Add(New Border With {
            .Background = New SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            .CornerRadius = New CornerRadius(6),
            .HorizontalAlignment = HorizontalAlignment.Left,
            .VerticalAlignment = VerticalAlignment.Top,
            .Margin = New Thickness(14),
            .Padding = New Thickness(2),
            .Child = InfoText,
            .IsHitTestVisible = False
        })

        ' 左下角操作提示
        Dim HintText As New TextBlock With {
            .Text = "左键拖动旋转 · 滚轮缩放",
            .FontSize = 12,
            .Margin = New Thickness(12, 6, 12, 6)
        }
        HintText.Foreground = New SolidColorBrush(Color.FromRgb(175, 188, 200))
        Root.Children.Add(New Border With {
            .Background = New SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            .CornerRadius = New CornerRadius(6),
            .HorizontalAlignment = HorizontalAlignment.Left,
            .VerticalAlignment = VerticalAlignment.Bottom,
            .Margin = New Thickness(14),
            .Padding = New Thickness(2),
            .Child = HintText,
            .IsHitTestVisible = False
        })

        ' 右上角重置视角
        Dim BtnReset As New Button With {
            .Content = "重置视角",
            .FontSize = 12,
            .Padding = New Thickness(14, 6, 14, 6),
            .Cursor = Cursors.Hand,
            .Foreground = New SolidColorBrush(Color.FromRgb(230, 236, 242)),
            .Background = New SolidColorBrush(Color.FromArgb(190, 42, 54, 68)),
            .BorderBrush = New SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
            .BorderThickness = New Thickness(1),
            .HorizontalAlignment = HorizontalAlignment.Right,
            .VerticalAlignment = VerticalAlignment.Top,
            .Margin = New Thickness(14)
        }
        AddHandler BtnReset.Click, Sub(sender As Object, e As RoutedEventArgs) ResetView()
        Root.Children.Add(BtnReset)

        Content = Root
        UpdateCamera()
    End Sub

    ''' <summary>
    ''' 把地形数据构建成可冻结的 3D 模型，可在后台线程调用。
    ''' </summary>
    Public Shared Function CreateModel(Map As ModSave.TerrainMap) As Model3DGroup
        Dim Group As New Model3DGroup
        Dim Buckets As New Dictionary(Of Integer, MeshBuilder)()
        Dim Width As Integer = Map.Width
        Dim Depth As Integer = Map.Depth
        For Z As Integer = 0 To Depth - 1
            For X As Integer = 0 To Width - 1
                Dim Index As Integer = Z * Width + X
                Dim Height As Integer = Map.Heights(Index)
                If Height = Integer.MinValue Then Continue For
                Dim ColorValue As Integer = Map.Colors(Index) Or &HFF000000
                Dim Builder As MeshBuilder = Nothing
                If Not Buckets.TryGetValue(ColorValue, Builder) Then
                    Builder = New MeshBuilder()
                    Buckets(ColorValue) = Builder
                End If
                Builder.AddQuad(X, X + 1, Z, Z + 1, Height + 1)
            Next
        Next

        For Each Pair As KeyValuePair(Of Integer, MeshBuilder) In Buckets
            Dim Geometry As New MeshGeometry3D With {
                .Positions = Pair.Value.Positions,
                .TriangleIndices = Pair.Value.Triangles,
                .Normals = Pair.Value.Normals}
            Geometry.Freeze()
            Dim ColorValue As Integer = Pair.Key
            Dim Brush As New SolidColorBrush(Color.FromRgb(
                CByte((ColorValue >> 16) And &HFF),
                CByte((ColorValue >> 8) And &HFF),
                CByte(ColorValue And &HFF)))
            Brush.Freeze()
            Dim Material As New DiffuseMaterial(Brush)
            Material.Freeze()
            Dim GeometryModel As New GeometryModel3D(Geometry, Material)
            GeometryModel.Freeze()
            Group.Children.Add(GeometryModel)
        Next

        Group.Children.Add(New AmbientLight(Color.FromRgb(110, 110, 110)))
        Dim MainLight As New DirectionalLight(Color.FromRgb(255, 255, 255), New Vector3D(-0.45, -1, -0.35))
        MainLight.Freeze()
        Group.Children.Add(MainLight)
        Dim FillLight As New DirectionalLight(Color.FromRgb(170, 195, 255), New Vector3D(0.4, -0.45, 0.6))
        FillLight.Freeze()
        Group.Children.Add(FillLight)
        Group.Freeze()
        Return Group
    End Function

    Private Sub ResetView()
        Yaw = 35
        Pitch = 52
        Distance = MapSize * 1.3
        UpdateCamera()
    End Sub

    Private Sub UpdateCamera()
        Dim PitchRad As Double = Pitch * Math.PI / 180
        Dim YawRad As Double = Yaw * Math.PI / 180
        Dim Offset As New Vector3D(
            Math.Sin(YawRad) * Math.Cos(PitchRad) * Distance,
            Math.Sin(PitchRad) * Distance,
            Math.Cos(YawRad) * Math.Cos(PitchRad) * Distance)
        Camera.Position = ViewCenter + Offset
        Camera.LookDirection = -Offset
        Camera.UpDirection = New Vector3D(0, 1, 0)
    End Sub

    Private Sub Window_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonDown
        IsDragging = True
        DragPoint = e.GetPosition(Me)
        DragYaw = Yaw
        DragPitch = Pitch
        CaptureMouse()
    End Sub

    Private Sub Window_MouseMove(sender As Object, e As MouseEventArgs) Handles Me.MouseMove
        If Not IsDragging Then Return
        Dim Pos As Point = e.GetPosition(Me)
        Yaw = DragYaw - (Pos.X - DragPoint.X) * 0.35
        Pitch = Math.Max(12, Math.Min(87, DragPitch + (Pos.Y - DragPoint.Y) * 0.3))
        UpdateCamera()
    End Sub

    Private Sub Window_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonUp
        If Not IsDragging Then Return
        IsDragging = False
        ReleaseMouseCapture()
    End Sub

    Private Sub Window_MouseWheel(sender As Object, e As MouseWheelEventArgs) Handles Me.MouseWheel
        Distance = Math.Max(30, Math.Min(MapSize * 4, Distance * If(e.Delta > 0, 0.88, 1.12)))
        UpdateCamera()
    End Sub

    Private Class MeshBuilder
        Public ReadOnly Positions As New Point3DCollection
        Public ReadOnly Triangles As New Int32Collection
        Public ReadOnly Normals As New Vector3DCollection

        Public Sub AddQuad(X0 As Double, X1 As Double, Z0 As Double, Z1 As Double, Y As Double)
            Dim Start As Integer = Positions.Count
            Positions.Add(New Point3D(X0, Y, Z0))
            Positions.Add(New Point3D(X0, Y, Z1))
            Positions.Add(New Point3D(X1, Y, Z1))
            Positions.Add(New Point3D(X1, Y, Z0))
            For i As Integer = 1 To 4
                Normals.Add(New Vector3D(0, 1, 0))
            Next
            Triangles.Add(Start)
            Triangles.Add(Start + 1)
            Triangles.Add(Start + 2)
            Triangles.Add(Start)
            Triangles.Add(Start + 2)
            Triangles.Add(Start + 3)
        End Sub
    End Class

End Class

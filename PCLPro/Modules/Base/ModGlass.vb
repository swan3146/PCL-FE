'全局液态玻璃材质模块
'为弹窗与浮层提供真实的背景模糊（快照式 backdrop blur），并叠加折射、色散、镜面高光与随鼠标流动的柔光
'——即液态玻璃的四要素：模糊 / 折射弯曲 / 边缘色散 / 镜面反光

Imports System.Windows.Media.Effects
Imports System.Windows.Media.Imaging

Public Module ModGlass

#Region "参数"

    ''' <summary>
    ''' 玻璃圆角半径。液态玻璃的圆角更大，让边缘的折射有充分展开的空间。
    ''' </summary>
    Public Const GlassCornerRadius As Double = 16
    ''' <summary>
    ''' 背景模糊半径。
    ''' </summary>
    Public Const GlassBlurRadius As Double = 34
    ''' <summary>
    ''' 是否启用真实背景模糊。当快照失败时会自动退化为纯半透明玻璃。
    ''' </summary>
    Public GlassBackdropEnabled As Boolean = True
    ''' <summary>
    ''' 玻璃边缘的折射放大倍率：越靠近边缘，背景被拉伸得越明显（透镜效应）。
    ''' </summary>
    Private Const GlassRefractScale As Double = 1.075

    ''' <summary>
    ''' 玻璃本体：Lens 风格——以通透为主，仅轻微提亮，让模糊背景的色彩透出来。
    ''' </summary>
    Private Function GlassTintBrush() As LinearGradientBrush
        Dim Brush As New LinearGradientBrush(
            Color.FromArgb(146, 255, 255, 255),
            Color.FromArgb(108, 240, 247, 255),
            New Point(0, 0), New Point(0.3, 1))
        Brush.Freeze()
        Return Brush
    End Function
    ''' <summary>
    ''' 镜面反光：顶部一道锐利亮边 + 一条斜向高光带 + 底部回光，模拟玻璃表面的反射。
    ''' </summary>
    Private Function GlassSpecularBrush() As LinearGradientBrush
        Dim Brush As New LinearGradientBrush With {.StartPoint = New Point(0, 0), .EndPoint = New Point(0.72, 1)}
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(112, 255, 255, 255), 0))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(52, 255, 255, 255), 0.05))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(0, 255, 255, 255), 0.2))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(74, 255, 255, 255), 0.44))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(18, 255, 255, 255), 0.56))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(0, 255, 255, 255), 0.86))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(34, 255, 255, 255), 0.96))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(66, 255, 255, 255), 1))
        Brush.Freeze()
        Return Brush
    End Function
    ''' <summary>
    ''' 边缘色散：玻璃厚度导致不同波长的光折射率不同，边缘会泛出冷暖色偏。
    ''' </summary>
    Private Function GlassChromaBrush() As LinearGradientBrush
        Dim Brush As New LinearGradientBrush With {.StartPoint = New Point(0, 0), .EndPoint = New Point(1, 1)}
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(46, 255, 158, 200), 0))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(14, 190, 220, 255), 0.12))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(0, 255, 255, 255), 0.34))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(0, 255, 255, 255), 0.66))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(16, 150, 205, 255), 0.88))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(50, 143, 212, 255), 1))
        Brush.Freeze()
        Return Brush
    End Function
    ''' <summary>
    ''' 折射遮罩：中心不显示折射层，越靠边缘显示越强（透镜只在边缘弯曲光线）。
    ''' </summary>
    Private Function GlassRimMask() As RadialGradientBrush
        Dim Brush As New RadialGradientBrush With {.Center = New Point(0.5, 0.5), .GradientOrigin = New Point(0.5, 0.5), .RadiusX = 0.78, .RadiusY = 0.78}
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(0, 255, 255, 255), 0))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(20, 255, 255, 255), 0.58))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(190, 255, 255, 255), 0.9))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(235, 255, 255, 255), 1))
        Brush.Freeze()
        Return Brush
    End Function
    ''' <summary>
    ''' 随鼠标流动的柔光：液态玻璃的高光会随视角移动。
    ''' </summary>
    Private Function GlassMouseBrush() As RadialGradientBrush
        Dim Brush As New RadialGradientBrush With {.Center = New Point(0.5, 0), .GradientOrigin = New Point(0.5, 0), .RadiusX = 0.85, .RadiusY = 0.85}
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(96, 255, 255, 255), 0))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(30, 255, 255, 255), 0.45))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(0, 255, 255, 255), 1))
        Return Brush
    End Function
    ''' <summary>
    ''' 玻璃边缘的折射描边。
    ''' </summary>
    Private Function GlassEdgeColor() As Color
        Return Color.FromArgb(158, 255, 255, 255)
    End Function

#End Region

#Region "弹窗玻璃"

    ''' <summary>
    ''' 为一个弹窗（或任意悬浮面板）的外框附加液态玻璃：
    ''' 真实背景模糊 + 边缘折射弯曲 + 边缘色散 + 镜面高光 + 随鼠标流动的柔光。
    ''' 该操作不会改变原内容结构，仅在其下方插入玻璃图层；任何一步失败都会退化为半透明玻璃。
    ''' </summary>
    ''' <param name="Border">弹窗的最外层 Border（PanBorder）。</param>
    ''' <param name="BlurRadius">背景模糊半径，0 表示不做模糊。</param>
    Public Sub GlassAttachDialog(Border As Border, Optional BlurRadius As Double = GlassBlurRadius)
        If Border Is Nothing OrElse FrmMain Is Nothing Then Return
        Try
            Dim Root = TryCast(Border.Parent, FrameworkElement) '弹窗根（通常是 Grid）
            If Root Is Nothing OrElse Border.Child Is Nothing Then Return
            '视觉参数（先铺一层降级用的玻璃底色，确保任何异常下都不会变成实心白）
            Border.Background = GlassTintBrush()
            Border.BorderThickness = New Thickness(1)
            Border.BorderBrush = New SolidColorBrush(GlassEdgeColor())
            Border.CornerRadius = New CornerRadius(GlassCornerRadius)
            If Border.Effect Is Nothing OrElse TypeOf Border.Effect Is DropShadowEffect Then
                Border.Effect = New DropShadowEffect With {.Color = Color.FromArgb(255, 24, 34, 48), .BlurRadius = 42, .ShadowDepth = 9, .Opacity = 0.34, .RenderingBias = RenderingBias.Performance}
            End If
            '建立玻璃图层（自下而上：模糊背景 → 边缘折射 → 玻璃本体 → 色散 → 镜面 → 鼠标柔光 → 内容 → 描边）
            Dim RawChild As UIElement = Border.Child
            Border.Child = Nothing
            Dim Host As New Grid With {.SnapsToDevicePixels = False, .UseLayoutRounding = False}
            Dim BackRect As New Shapes.Rectangle With {.HorizontalAlignment = HorizontalAlignment.Left, .VerticalAlignment = VerticalAlignment.Top, .IsHitTestVisible = False}
            Dim RefractRect As New Shapes.Rectangle With {.IsHitTestVisible = False, .HorizontalAlignment = HorizontalAlignment.Left, .VerticalAlignment = VerticalAlignment.Top, .OpacityMask = GlassRimMask()}
            Host.Children.Add(BackRect)
            Host.Children.Add(RefractRect)
            Host.Children.Add(New Shapes.Rectangle With {.Fill = GlassTintBrush(), .IsHitTestVisible = False, .HorizontalAlignment = HorizontalAlignment.Stretch, .VerticalAlignment = VerticalAlignment.Stretch})
            Host.Children.Add(New Shapes.Rectangle With {.Fill = GlassChromaBrush(), .IsHitTestVisible = False, .HorizontalAlignment = HorizontalAlignment.Stretch, .VerticalAlignment = VerticalAlignment.Stretch})
            Host.Children.Add(New Shapes.Rectangle With {.Fill = GlassSpecularBrush(), .IsHitTestVisible = False, .HorizontalAlignment = HorizontalAlignment.Stretch, .VerticalAlignment = VerticalAlignment.Stretch})
            Dim MouseBrush As RadialGradientBrush = GlassMouseBrush()
            Host.Children.Add(New Shapes.Rectangle With {.Fill = MouseBrush, .IsHitTestVisible = False, .HorizontalAlignment = HorizontalAlignment.Stretch, .VerticalAlignment = VerticalAlignment.Stretch})
            Host.Children.Add(RawChild)
            '厚度感：外侧亮边 + 内侧一道极淡的暗边
            Host.Children.Add(New Border With {
                .BorderThickness = New Thickness(1), .BorderBrush = New SolidColorBrush(Color.FromArgb(72, 255, 255, 255)),
                .CornerRadius = New CornerRadius(GlassCornerRadius - 1), .IsHitTestVisible = False, .SnapsToDevicePixels = False})
            Host.Children.Add(New Border With {
                .Margin = New Thickness(1), .BorderThickness = New Thickness(1), .BorderBrush = New SolidColorBrush(Color.FromArgb(18, 40, 60, 92)),
                .CornerRadius = New CornerRadius(GlassCornerRadius - 2), .IsHitTestVisible = False, .SnapsToDevicePixels = False})
            Border.Child = Host
            '背景模糊与折射底图
            If GlassBackdropEnabled AndAlso BlurRadius > 0 Then
                GlassFillBackdrop(Root, BackRect, RefractRect, BlurRadius)
            Else
                BackRect.Visibility = Visibility.Collapsed
                RefractRect.Visibility = Visibility.Collapsed
            End If
            '高光随鼠标流动
            AddHandler Root.MouseMove,
                Sub(sender As Object, e As MouseEventArgs)
                    GlassMoveShine(MouseBrush, Root, e)
                End Sub
            '裁剪与定位：随尺寸变化更新
            Dim Updater As SizeChangedEventHandler =
                Sub(sender As Object, e As SizeChangedEventArgs)
                    GlassUpdateClip(Host, BackRect, RefractRect, Root)
                End Sub
            AddHandler Host.SizeChanged, Updater
            GlassUpdateClip(Host, BackRect, RefractRect, Root)
        Catch ex As Exception
            Logger.Info($"[Glass] 弹窗玻璃化失败：{ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' 让玻璃上的柔光跟随鼠标移动。
    ''' </summary>
    Private Sub GlassMoveShine(Brush As RadialGradientBrush, Target As FrameworkElement, e As MouseEventArgs)
        Try
            If Target Is Nothing OrElse Target.ActualWidth <= 0 OrElse Target.ActualHeight <= 0 Then Return
            Dim Pos = e.GetPosition(Target)
            Brush.Center = New Point(Pos.X / Target.ActualWidth, Pos.Y / Target.ActualHeight)
            Brush.GradientOrigin = Brush.Center
        Catch
        End Try
    End Sub
    ''' <summary>
    ''' 给普通控件（如卡片）返回一支可跟随鼠标的柔光笔刷。
    ''' </summary>
    Public Function GlassCreateMouseShine() As RadialGradientBrush
        Dim Brush As New RadialGradientBrush With {.Center = New Point(0.5, 0), .GradientOrigin = New Point(0.5, 0), .RadiusX = 0.9, .RadiusY = 1.15}
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(84, 255, 255, 255), 0))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(26, 255, 255, 255), 0.4))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(0, 255, 255, 255), 1))
        Return Brush
    End Function

    ''' <summary>
    ''' 更新玻璃图层的裁剪区域，并让模糊层与折射层重新对齐到窗口背景。
    ''' </summary>
    Private Sub GlassUpdateClip(Host As Grid, BackRect As Shapes.Rectangle, RefractRect As Shapes.Rectangle, Root As FrameworkElement)
        Try
            Dim W = Host.ActualWidth, H = Host.ActualHeight
            If W <= 0 OrElse H <= 0 Then Return
            Host.Clip = New RectangleGeometry(New Rect(0, 0, W, H), GlassCornerRadius - 1, GlassCornerRadius - 1)
            Dim Info = TryCast(BackRect.Tag, GlassBackdropInfo)
            If Info Is Nothing OrElse Root Is Nothing Then Return
            Dim Pos As Point = Info.GetOffset(Root)
            BackRect.Margin = New Thickness(-Pos.X - Info.Padding, -Pos.Y - Info.Padding, 0, 0)
            '折射层：以中心为锚点把背景放大，让边缘看起来被玻璃“拉弯”
            Dim Zoom As Double = GlassRefractScale
            Dim ShiftX As Double = (Pos.X - Info.Offset.X)
            Dim ShiftY As Double = (Pos.Y - Info.Offset.Y)
            RefractRect.Width = W * Zoom
            RefractRect.Height = H * Zoom
            RefractRect.Margin = New Thickness(-Pos.X - (W * (Zoom - 1)) / 2 + ShiftX, -Pos.Y - (H * (Zoom - 1)) / 2 + ShiftY, 0, 0)
            Dim Brush = TryCast(RefractRect.Fill, ImageBrush)
            If Brush IsNot Nothing Then
                Brush.Viewport = New Rect(Info.Padding - (W * (Zoom - 1)) / 2 + ShiftX, Info.Padding - (H * (Zoom - 1)) / 2 + ShiftY, W * Zoom, H * Zoom)
            End If
        Catch ex As Exception
            Logger.Info($"[Glass] 玻璃裁剪更新失败：{ex.Message}")
        End Try
    End Sub

    Private Class GlassBackdropInfo
        Public Padding As Double
        ''' <summary>
        ''' 快照时弹窗左上角相对窗口内容区的坐标（布局坐标，不含动画变换）。
        ''' </summary>
        Public Offset As Point
        Public Function GetOffset(Root As FrameworkElement) As Point
            Try
                If Root Is Nothing OrElse FrmMain Is Nothing Then Return Offset
                Dim Raw = Root.RenderTransform
                Dim Pos As Point
                If Raw Is Nothing Then
                    Pos = Root.TranslatePoint(New Point(0, 0), FrmMain.PanForm)
                Else
                    Root.RenderTransform = Nothing
                    Pos = Root.TranslatePoint(New Point(0, 0), FrmMain.PanForm)
                    Root.RenderTransform = Raw
                End If
                If Not Double.IsNaN(Pos.X) AndAlso Pos.X <> 0 Then Offset = Pos
                Return Offset
            Catch
                Return Offset
            End Try
        End Function
    End Class

    ''' <summary>
    ''' 抓取弹窗下方的窗口内容，模糊后作为玻璃的底图，并生成一份放大的副本用于边缘折射。
    ''' </summary>
    Private Sub GlassFillBackdrop(Root As FrameworkElement, BackRect As Shapes.Rectangle, RefractRect As Shapes.Rectangle, BlurRadius As Double)
        Try
            If FrmMain Is Nothing OrElse Not FrmMain.IsLoaded Then Return
            Dim W As Double = FrmMain.PanForm.ActualWidth, H As Double = FrmMain.PanForm.ActualHeight
            If W < 8 OrElse H < 8 Then Return
            '临时移除变换与自身，避免把弹窗本身拍进去
            Dim RawTransform As Transform = Root.RenderTransform
            Dim RawVisibility As Visibility = Root.Visibility
            Root.RenderTransform = Nothing
            Root.UpdateLayout()
            Dim Pos As Point = Root.TranslatePoint(New Point(0, 0), FrmMain.PanForm)
            Root.Visibility = Visibility.Collapsed
            Dim Bitmap As New RenderTargetBitmap(CInt(Math.Ceiling(W)), CInt(Math.Ceiling(H)), 96, 96, PixelFormats.Pbgra32)
            Bitmap.Render(FrmMain.PanForm)
            Root.Visibility = RawVisibility
            Root.RenderTransform = RawTransform
            If Double.IsNaN(Pos.X) OrElse Pos.X < -W OrElse Pos.Y < -H Then Return
            '铺设为底图：向外多留一段，避免模糊的边缘淡出出现在玻璃内
            Dim Padding As Double = BlurRadius * 2.6
            BackRect.Tag = New GlassBackdropInfo With {.Padding = Padding, .Offset = Pos}
            BackRect.Width = W + Padding * 2
            BackRect.Height = H + Padding * 2
            BackRect.Fill = New ImageBrush With {
                .ImageSource = Bitmap, .Stretch = Stretch.None,
                .ViewboxUnits = BrushMappingMode.Absolute, .Viewbox = New Rect(0, 0, Bitmap.PixelWidth, Bitmap.PixelHeight),
                .ViewportUnits = BrushMappingMode.Absolute, .Viewport = New Rect(Padding, Padding, W, H),
                .AlignmentX = AlignmentX.Left, .AlignmentY = AlignmentY.Top}
            BackRect.Effect = New BlurEffect With {.Radius = BlurRadius, .KernelType = KernelType.Gaussian, .RenderingBias = RenderingBias.Performance}
            RefractRect.Fill = New ImageBrush With {
                .ImageSource = Bitmap, .Stretch = Stretch.None,
                .ViewboxUnits = BrushMappingMode.Absolute, .Viewbox = New Rect(0, 0, Bitmap.PixelWidth, Bitmap.PixelHeight),
                .ViewportUnits = BrushMappingMode.Absolute, .Viewport = New Rect(Padding, Padding, W, H),
                .AlignmentX = AlignmentX.Left, .AlignmentY = AlignmentY.Top}
            BackRect.Margin = New Thickness(-Pos.X - Padding, -Pos.Y - Padding, 0, 0)
        Catch ex As Exception
            Logger.Info($"[Glass] 背景快照失败，退化为半透明玻璃：{ex.Message}")
            BackRect.Visibility = Visibility.Collapsed
            RefractRect.Visibility = Visibility.Collapsed
        End Try
    End Sub

#End Region

#Region "通用玻璃元素"

    ''' <summary>
    ''' 创建一片可直接铺在容器底部的液态玻璃（不含背景模糊，适用于已经位于模糊壁纸之上的元素）。
    ''' </summary>
    Public Function GlassCreateLayer(Optional CornerRadius As Double = 10, Optional TintTop As Byte = 190, Optional TintBottom As Byte = 150) As Grid
        Dim Layer As New Grid With {.IsHitTestVisible = False, .SnapsToDevicePixels = False}
        Layer.Children.Add(New Border With {
            .Background = New LinearGradientBrush(Color.FromArgb(TintTop, 255, 255, 255), Color.FromArgb(TintBottom, 244, 249, 255), New Point(0, 0), New Point(0.3, 1)),
            .CornerRadius = New CornerRadius(CornerRadius), .IsHitTestVisible = False})
        Layer.Children.Add(New Border With {
            .Background = GlassChromaBrush(), .CornerRadius = New CornerRadius(CornerRadius), .IsHitTestVisible = False})
        Layer.Children.Add(New Border With {
            .Background = GlassSpecularBrush(), .CornerRadius = New CornerRadius(CornerRadius), .IsHitTestVisible = False})
        Layer.Children.Add(New Border With {
            .BorderThickness = New Thickness(1), .BorderBrush = New SolidColorBrush(GlassEdgeColor()),
            .CornerRadius = New CornerRadius(CornerRadius), .IsHitTestVisible = False})
        Return Layer
    End Function

    ''' <summary>
    ''' 为任意 Border 套用液态玻璃描边与柔光阴影。
    ''' </summary>
    Public Sub GlassDecorateBorder(Border As Border, Optional CornerRadius As Double = GlassCornerRadius, Optional WithShadow As Boolean = True)
        If Border Is Nothing Then Return
        Border.CornerRadius = New CornerRadius(CornerRadius)
        Border.BorderThickness = New Thickness(1)
        Border.BorderBrush = New SolidColorBrush(GlassEdgeColor())
        If WithShadow Then
            Border.Effect = New DropShadowEffect With {.Color = Color.FromArgb(255, 24, 34, 48), .BlurRadius = 30, .ShadowDepth = 6, .Opacity = 0.3, .RenderingBias = RenderingBias.Performance}
        End If
    End Sub

    ''' <summary>
    ''' 液态玻璃的镜面高光（供卡片等控件复用）。
    ''' </summary>
    Public Function GlassSpecular() As LinearGradientBrush
        Return GlassSpecularBrush()
    End Function
    ''' <summary>
    ''' 液态玻璃的边缘色散（供卡片等控件复用）。
    ''' </summary>
    Public Function GlassChroma() As LinearGradientBrush
        Return GlassChromaBrush()
    End Function

#End Region

    ''' <summary>
    ''' 毛玻璃模式的半透明磨砂填充（用于卡片与侧栏背景板）。
    ''' </summary>
    Public Function GlassFrostBrush() As Brush
        Dim Frost As New LinearGradientBrush With {.StartPoint = New Point(0, 0), .EndPoint = New Point(0.35, 1)}
        Frost.GradientStops.Add(New GradientStop(Color.FromArgb(38, 255, 255, 255), 0))
        Frost.GradientStops.Add(New GradientStop(Color.FromArgb(26, 255, 255, 255), 0.16))
        Frost.GradientStops.Add(New GradientStop(Color.FromArgb(56, 255, 255, 255), 0.56))
        Frost.GradientStops.Add(New GradientStop(Color.FromArgb(72, 255, 255, 255), 1))
        Frost.Freeze()
        Return Frost
    End Function

End Module

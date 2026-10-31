Public Class MyCard

    '控件
    Inherits Grid
    Private ReadOnly MainGrid As Grid
    Public ReadOnly Property MainChrome As MyDropShadow
    Private ReadOnly MainBorder As Border
    Public Property BorderChild As UIElement
        Get
            Return MainBorder.Child
        End Get
        Set(value As UIElement)
            MainBorder.Child = value
        End Set
    End Property
    Private _MainTextBlock As TextBlock
    Public Property MainTextBlock As TextBlock
        Get
            Init() '当父级触发 Loaded 时，本卡片可能尚未触发 Loaded（该事件从父级向子级调用），因此这会是 null。手动触发以确保控件已加载。
            Return _MainTextBlock
        End Get
        Set(value As TextBlock)
            _MainTextBlock = value
        End Set
    End Property
    Private _MainSwap As Shapes.Path
    Public Property MainSwap As Shapes.Path
        Get
            Init()
            Return _MainSwap
        End Get
        Set(value As Shapes.Path)
            _MainSwap = value
        End Set
    End Property

    '属性
    Public Uuid As Integer = GetUuid()
    ''' <summary>
    ''' 卡片表面的柔光，会跟随鼠标位置流动。
    ''' </summary>
    Private ReadOnly MouseShine As RadialGradientBrush = GlassCreateMouseShine()
    Public ReadOnly Property Inlines As InlineCollection
        Get
            Return MainTextBlock.Inlines
        End Get
    End Property
    Public Property CornerRadius As CornerRadius
        Get
            Return MainChrome.CornerRadius
        End Get
        Set(value As CornerRadius)
            MainChrome.CornerRadius = value
            MainBorder.CornerRadius = value
        End Set
    End Property
    Public Property Title As String
        Get
            Return GetValue(TitleProperty)
        End Get
        Set(value As String)
            SetValue(TitleProperty, value)
            If _MainTextBlock IsNot Nothing Then MainTextBlock.Text = value
        End Set
    End Property
    Public Shared ReadOnly TitleProperty As DependencyProperty = DependencyProperty.Register("Title", GetType(String), GetType(MyCard), New PropertyMetadata(""))

    'UI 建立
    Public Sub New()
        MainChrome = New MyDropShadow With {
            .Margin = New Thickness(-5, -5, -5, -5 - GetWPFSize(1)), .ShadowRadius = 11, .Opacity = DropShadowIdleOpacity, .CornerRadius = New CornerRadius(12)}
        MainChrome.SetResourceReference(MyDropShadow.ColorProperty, "ColorObject1")
        Children.Insert(0, MainChrome)
        Dim GlassBrush As New LinearGradientBrush(
            Color.FromArgb(158, 255, 255, 255),
            Color.FromArgb(108, 236, 244, 255),
            New Point(0, 0), New Point(0.3, 1))
        GlassBrush.Freeze()
        Dim GlassEdge As New SolidColorBrush(Color.FromArgb(158, 255, 255, 255))
        GlassEdge.Freeze()
        '液态玻璃表面：边缘色散 + 镜面反光 + 随鼠标流动的柔光 + 厚度描边
        Dim GlassInner As New Grid With {.IsHitTestVisible = False}
        GlassInner.Children.Add(New Shapes.Rectangle With {.Fill = GlassChroma(), .IsHitTestVisible = False})
        GlassInner.Children.Add(New Shapes.Rectangle With {.Fill = GlassSpecular(), .IsHitTestVisible = False})
        GlassInner.Children.Add(New Shapes.Rectangle With {.Fill = MouseShine, .IsHitTestVisible = False})
        GlassInner.Children.Add(New Border With {
            .BorderThickness = New Thickness(1), .CornerRadius = New CornerRadius(11), .IsHitTestVisible = False,
            .BorderBrush = New SolidColorBrush(Color.FromArgb(52, 255, 255, 255))})
        GlassInner.Children.Add(New Border With {
            .Margin = New Thickness(1), .BorderThickness = New Thickness(1), .CornerRadius = New CornerRadius(10), .IsHitTestVisible = False,
            .BorderBrush = New SolidColorBrush(Color.FromArgb(14, 40, 60, 92))})
        MainBorder = New Border With {
            .CornerRadius = New CornerRadius(12),
            .IsHitTestVisible = False,
            .Background = GlassBrush,
            .BorderThickness = New Thickness(1),
            .BorderBrush = GlassEdge,
            .Child = GlassInner
        }
        Children.Insert(1, MainBorder)
        MainGrid = New Grid
        Children.Add(MainGrid)
    End Sub
    Private IsLoad As Boolean = False
    Private Sub Init() Handles Me.Loaded
        If IsLoad Then Return
        IsLoad = True
        '初次加载限定
        If MainTextBlock Is Nothing Then
            MainTextBlock = New TextBlock With {.HorizontalAlignment = HorizontalAlignment.Left, .VerticalAlignment = VerticalAlignment.Top, .Margin = New Thickness(15, 12, 0, 0), .FontWeight = FontWeights.Bold, .FontSize = 13, .IsHitTestVisible = False}
            MainTextBlock.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
            MainTextBlock.SetBinding(TextBlock.TextProperty, New Binding("Title") With {.Source = Me, .Mode = BindingMode.OneWay})
            MainGrid.Children.Add(MainTextBlock)
        End If
        If CanSwap OrElse SwapControl IsNot Nothing Then
            If SwapControl Is Nothing AndAlso Children.Count > 3 Then SwapControl = Children(3)
            MainSwap = New Shapes.Path With {.HorizontalAlignment = HorizontalAlignment.Right, .Stretch = Stretch.Uniform, .Height = 6, .Width = 10, .VerticalAlignment = VerticalAlignment.Top, .Margin = New Thickness(0, 17, 16, 0), .Data = New GeometryConverter().ConvertFromString("M2,4 l-2,2 10,10 10,-10 -2,-2 -8,8 -8,-8 z"), .RenderTransform = New RotateTransform(180), .RenderTransformOrigin = New Point(0.5, 0.5)}
            MainSwap.SetResourceReference(Shapes.Path.FillProperty, "ColorBrush1")
            MainGrid.Children.Add(MainSwap)
        End If
        '改变默认的折叠
        If IsSwapped AndAlso SwapControl IsNot Nothing Then
            MainSwap.RenderTransform = New RotateTransform(If(SwapLogoRight, 270, 0))
            SwapControl.Visibility = Visibility.Collapsed
            '取消由于高度变化被迫触发的高度动画
            Dim RawUseAnimation As Boolean = UseAnimation
            UseAnimation = False
            Height = SwapedHeight
            AniStop("MyCard Height " & Uuid)
            IsHeightAnimating = False
            RunInUi(Sub() UseAnimation = RawUseAnimation, True)
        End If
    End Sub

    '填充
    Private ContentBuilder As Action(Of Panel)
    Public Sub SetContentBuilder(Builder As Action(Of Panel))
        ContentBuilder = Builder
    End Sub
    Public Sub SetContentBuilder(Of T)(Items As IEnumerable(Of T), ItemBuilder As Func(Of T, UIElement))
        SetContentBuilder(
        Sub(Stack)
            For Each Item In Items
                Stack.Children.Add(ItemBuilder(Item))
            Next
            Stack.Children.Add(New FrameworkElement With {.Height = 18})
        End Sub)
    End Sub
    ''' <summary>
    ''' 使用 ContentBuilder 生成卡片内容。
    ''' </summary>
    Public Sub BuildContent()
        If ContentBuilder Is Nothing Then Return
        Dim Stack = TryCast(SwapControl, Panel)
        If Stack Is Nothing Then Return
        Try
            ContentBuilder(Stack)
            ContentBuilder = Nothing
        Catch
            Stack.Children.Clear()
            Throw
        End Try
        TriggerForceResize()
    End Sub

    '动画
    Private Const DropShadowIdleOpacity As Double = 0.1
    Private Const DropShadowHoverOpacity As Double = 0.32
    '悬停时的缩放比例与纵向位移（向上为负）
    Private Const CardHoverScale As Double = 1.016
    Private Const CardHoverOffset As Double = -8
    Public Property HasMouseAnimation As Boolean = True
    ''' <summary>
    ''' 读取卡片当前的缩放与纵向位移，用于把动画精确推进到目标值（可随时打断）。
    ''' </summary>
    Private Function GetPopState() As Double()
        Dim Group = TryCast(Me.RenderTransform, TransformGroup)
        If Group IsNot Nothing AndAlso Group.Children.Count = 2 AndAlso
           TypeOf Group.Children(0) Is ScaleTransform AndAlso TypeOf Group.Children(1) Is TranslateTransform Then
            Return New Double() {CType(Group.Children(0), ScaleTransform).ScaleX, CType(Group.Children(1), TranslateTransform).Y}
        End If
        Return New Double() {1, 0}
    End Function
    Private Sub MyCard_MouseEnter(sender As Object, e As MouseEventArgs) Handles Me.MouseEnter
        If Not HasMouseAnimation Then Return
        Dim AniList As New List(Of AniData)
        If Not IsNothing(MainTextBlock) Then AniList.Add(AaColor(MainTextBlock, TextBlock.ForegroundProperty, "ColorBrush2", 130))
        If Not IsNothing(MainSwap) Then AniList.Add(AaColor(MainSwap, Shapes.Path.FillProperty, "ColorBrush2", 130))
        AniList.AddRange({
            AaColor(MainChrome, MyDropShadow.ColorProperty, "ColorObject4", 130),
            AaOpacity(MainChrome, DropShadowHoverOpacity - MainChrome.Opacity, 130)
        })
        AniStart(AniList, "MyCard Mouse " & Uuid)
    End Sub
    Private Sub MyCard_MouseLeave(sender As Object, e As MouseEventArgs) Handles Me.MouseLeave
        If Not HasMouseAnimation Then Return
        Dim AniList As New List(Of AniData)
        If Not IsNothing(MainTextBlock) Then AniList.Add(AaColor(MainTextBlock, TextBlock.ForegroundProperty, "ColorBrush1", 130))
        If Not IsNothing(MainSwap) Then AniList.Add(AaColor(MainSwap, Shapes.Path.FillProperty, "ColorBrush1", 130))
        AniList.AddRange({
            AaColor(MainChrome, MyDropShadow.ColorProperty, "ColorObject1", 130),
            AaOpacity(MainChrome, DropShadowIdleOpacity - MainChrome.Opacity, 130)
        })
        AniStart(AniList, "MyCard Mouse " & Uuid)
    End Sub

    ''' <summary>按压形变：液态玻璃被按下时会像果冻一样轻微凹陷（Lens 的 spring deformation）。</summary>
    Private Sub MyCard_Press(sender As Object, e As MouseButtonEventArgs) Handles Me.PreviewMouseLeftButtonDown
        '板块不做动画：已移除卡片按压形变，避免变换动画引发错误
    End Sub
    ''' <summary>松开回弹。</summary>
    Private Sub MyCard_Release(sender As Object, e As MouseButtonEventArgs) Handles Me.PreviewMouseLeftButtonUp
        '板块不做动画：已移除卡片回弹形变
    End Sub

#Region "高度改变动画"

    ''' <summary>
    ''' 是否启用高度改变动画。
    ''' </summary>
    Public Property UseAnimation As Boolean = True
    Private IsHeightAnimating As Boolean = False
    Private ActualUsedHeight As Double '回滚实际高度（例如 NaN）
    Private Sub MySizeChanged(sender As Object, e As SizeChangedEventArgs) Handles Me.SizeChanged
        If Not UseAnimation Then Return
        Dim DeltaHeight As Double = If(IsSwapped, SwapedHeight, e.NewSize.Height) - e.PreviousSize.Height
        '卡片的进入时动画已被页面通用切换动画替代
        If e.PreviousSize.Height = 0 OrElse IsHeightAnimating OrElse Math.Abs(DeltaHeight) < 1 OrElse ActualHeight = 0 Then Return
        StartHeightAnimation(DeltaHeight, e.PreviousSize.Height, False)
    End Sub
    Private Sub StartHeightAnimation(Delta As Double, PreviousHeight As Double, IsLoadAnimation As Boolean)
        If IsHeightAnimating OrElse FrmMain Is Nothing Then Return '避免 XAML 设计器出错

        Dim AnimList As New List(Of AniData)
        Dim AbsDelta = Math.Abs(Delta)

        '更慢、更柔和的单段缓出，让展开/收起丝滑不跳跃
        If AbsDelta <= 1400 Then
            '中短距离：延长时长并用柔和的缓出
            AnimList.Add(AaHeight(Me, Delta, 520,, New AniEaseOutFluent(AniEasePower.Weak)))
        Else
            '长距离：时长随距离增长（开方缓和），整体放慢，落点更柔
            Dim Duration As Integer = CInt(Math.Min(1100, 520 + Math.Sqrt(AbsDelta) * 7))
            AnimList.Add(AaHeight(Me, Delta, Duration,, New AniEaseOutFluent(AniEasePower.Weak)))
        End If

        AnimList.Add(AaCode(
        Sub()
            IsHeightAnimating = False
            Height = ActualUsedHeight
            If IsSwapped AndAlso SwapControl IsNot Nothing Then SwapControl.Visibility = Visibility.Collapsed
        End Sub,, True))
        AniStart(AnimList, "MyCard Height " & Uuid)
        IsHeightAnimating = True
        ActualUsedHeight = If(IsSwapped, SwapedHeight, Height)
        Height = PreviousHeight
    End Sub
    ''' <summary>
    ''' 通知 MyCard，控件内容已改变，需要中断动画并瞬间更新高度。
    ''' </summary>
    Public Sub TriggerForceResize()
        Height = If(IsSwapped, SwapedHeight, Double.NaN)
        AniStop("MyCard Height " & Uuid)
        IsHeightAnimating = False
    End Sub

#End Region

#Region "折叠"

    '若设置了 CanSwap，或 SwapControl 不为空，则判定为会进行折叠
    '这是因为不能直接在 XAML 中设置 SwapControl
    Public SwapControl As FrameworkElement
    Public Property CanSwap As Boolean = False
    ''' <summary>
    ''' 是否已被折叠。
    ''' </summary>
    Public Property IsSwapped As Boolean
        Get
            Return _IsSwapped
        End Get
        Set(value As Boolean)
            If _IsSwapped = value Then Return
            _IsSwapped = value
            If SwapControl Is Nothing Then Return
            '展开
            If Not IsSwapped Then BuildContent()
            '若尚未加载，会在 Loaded 事件中触发无动画的折叠，不需要在这里进行
            If Not IsLoaded Then Return
            '更新样式
            SwapControl.Visibility = Visibility.Visible
            SwapControl.Opacity = 0
            AniStart({AaOpacity(SwapControl, 1, 360,, New AniEaseOutFluent(AniEasePower.Weak))}, "MyCard SwapContent " & Uuid)
            TriggerForceResize()
            UpdateCursorStyle()
            '改变箭头
            AniStart(AaRotateTransform(MainSwap, If(_IsSwapped, If(SwapLogoRight, 270, 0), 180) - CType(MainSwap.RenderTransform, RotateTransform).Angle, 250,, New AniEaseOutFluent(AniEasePower.ExtraStrong)), "MyCard Swap " & Uuid, True)
        End Set
    End Property
    Private _IsSwapped As Boolean = False
    <Obsolete("IsSwaped 存在拼写错误，请换用 IsSwapped 属性。")>
    Public Property IsSwaped As Boolean
        Get
            Return GetValue(IsSwapedProperty)
        End Get
        Set(value As Boolean)
            SetValue(IsSwapedProperty, value)
        End Set
    End Property
    Public Shared ReadOnly IsSwapedProperty As DependencyProperty = DependencyProperty.Register("IsSwaped", GetType(Boolean), GetType(MyCard), New PropertyMetadata(False,
    Sub(sender, e) If sender IsNot Nothing AndAlso TypeOf sender Is MyCard Then CType(sender, MyCard).IsSwapped = CType(e.NewValue, Boolean)))

    Public Property SwapLogoRight As Boolean = False
    Private IsSwapMouseDown As Boolean = False '用于触发卡片展开/折叠的 MouseDown
    Private IsCustomMouseDown As Boolean = False '用于触发自定义事件的 MouseDown
    Public Event PreviewSwap(sender As Object, e As RouteEventArgs)
    Public Event Swap(sender As Object, e As RouteEventArgs)
    Public Const SwapedHeight As Integer = 40
    Private Sub MyCard_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonDown
        Dim Pos As Double = Mouse.GetPosition(Me).Y
        If Not IsSwapped AndAlso
            (Pos > If(IsSwapped, SwapedHeight, SwapedHeight - 6) OrElse (Pos = 0 AndAlso Not IsMouseDirectlyOver)) Then Return
        IsCustomMouseDown = True
        If Not IsSwapped AndAlso
            (SwapControl Is Nothing OrElse Pos > If(IsSwapped, SwapedHeight, SwapedHeight - 6) OrElse (Pos = 0 AndAlso Not IsMouseDirectlyOver)) Then Return '检测点击位置；或已经不在可视树上的误判
        IsSwapMouseDown = True
    End Sub
    Private Sub MyCard_MouseLeftButtonUp() Handles Me.MouseLeftButtonUp
        If Not IsCustomMouseDown Then Return
        IsCustomMouseDown = False
        RaiseCustomEvent() '触发自定义事件

        If Not IsSwapMouseDown Then Return
        IsSwapMouseDown = False

        Dim Pos As Double = Mouse.GetPosition(Me).Y
        If Not IsSwapped AndAlso
            (SwapControl Is Nothing OrElse Pos > If(IsSwapped, SwapedHeight, SwapedHeight - 6) OrElse (Pos = 0 AndAlso Not IsMouseDirectlyOver)) Then Return '检测点击位置；或已经不在可视树上的误判

        Dim e = New RouteEventArgs(True)
        RaiseEvent PreviewSwap(Me, e)
        If e.Handled Then
            IsSwapMouseDown = False
            Return
        End If

        IsSwapped = Not IsSwapped
        Logger.Info($"{If(IsSwapped, "折叠卡片", "展开卡片")}{If(Title Is Nothing, "", "：" & Title)}")
        RaiseEvent Swap(Me, e)
    End Sub
    Private Sub MyCard_MouseLeave_Swap(sender As Object, e As MouseEventArgs) Handles Me.MouseLeave
        IsSwapMouseDown = False
    End Sub
    Private Sub UpdateCursorStyle() Handles Me.MouseMove
        '柔光跟随鼠标：液态玻璃的高光会随视角流动
        If MouseShine IsNot Nothing AndAlso ActualWidth > 0 AndAlso ActualHeight > 0 Then
            Dim ShinePos = Mouse.GetPosition(Me)
            MouseShine.Center = New Point(ShinePos.X / ActualWidth, ShinePos.Y / ActualHeight)
            MouseShine.GradientOrigin = MouseShine.Center
        End If
        If _MainSwap Is Nothing Then Cursor = Nothing : Return
        Dim Pos As Double = Mouse.GetPosition(Me).Y
        Cursor = If(MainSwap.Visibility = Visibility.Visible AndAlso (IsSwapped OrElse (Pos <= SwapedHeight - 6 AndAlso Not (Pos = 0 AndAlso Not IsMouseDirectlyOver))),
            Cursors.Hand, Nothing)
    End Sub

#End Region

End Class
Partial Public Module ModAnimation
    Public Sub AniDispose(Control As MyCard, RemoveFromChildren As Boolean, Optional CallBack As ParameterizedThreadStart = Nothing)
        If Control.IsHitTestVisible Then
            Control.IsHitTestVisible = False
            AniStart({
                AaScaleTransform(Control, -0.08, 200,, New AniEaseInFluent),
                AaOpacity(Control, -1, 200,, New AniEaseOutFluent),
                AaHeight(Control, -Control.ActualHeight, 150, 100, New AniEaseOutFluent),
                AaCode(
                Sub()
                    If RemoveFromChildren Then
                        If Control.Parent Is Nothing Then Return
                        CType(Control.Parent, Object).Children.Remove(Control)
                    Else
                        Control.Visibility = Visibility.Collapsed
                    End If
                    If CallBack IsNot Nothing Then CallBack(Control)
                End Sub,, True)
            }, "MyCard Dispose " & Control.Uuid)
        Else
            If RemoveFromChildren Then
                If Control.Parent Is Nothing Then Return
                CType(Control.Parent, Object).Children.Remove(Control)
            Else
                Control.Visibility = Visibility.Collapsed
            End If
            If CallBack IsNot Nothing Then CallBack(Control)
        End If
    End Sub
End Module

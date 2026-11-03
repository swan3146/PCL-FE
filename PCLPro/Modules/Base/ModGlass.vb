'PCL FE 毛玻璃引擎（1.3.5 从零重写，旧实现见 dist\PCLPro-1.3.5-UI重写前-源码.zip）
'
'这个模块只负责一件事：给界面上的各种「玻璃表面」提供材质。
'材质 = 背后画面的模糊底图 + 磨砂提亮层 + 一根发丝亮边，共三层，全部是静态冻结笔刷。
'
'底图分两种来源，语义严格区分：
'  1) 界面快照（UI 模糊）：把整个窗口（壁纸 + 卡片 + 文字，即真正的「背后界面」）拍一张，
'     降采样后用 UiUiBlur 模糊，弹窗用它。这才是毛玻璃该有的样子——
'     玻璃后面是被糊掉的界面元素，而不只是一张模糊壁纸。
'  2) 壁纸快照（壁纸模糊）：只拍 ImgBack，滚动中的卡片与侧栏用它，因为它可以全局缓存、
'     也可以按元素位置精确对齐，滚动时不需要重拍。
'
'性能约束（分层窗口 AllowsTransparency=True 只能 CPU 软件光栅化）：
'  - 界面快照只在弹窗打开时拍一次，降采样到最长边 900 px，模糊也在这张小图上做；
'  - 壁纸快照全局缓存一张，键 = 尺寸桶 + 模糊半径 + 画笔哈希，拖窗口不会每帧重建；
'  - 位置对齐统一走一个 90 ms 去抖队列，滚动过程只登记、停下才算；
'  - 全模块没有任何 Effect 挂在视觉树上（BlurEffect 只在离屏 DrawingVisual 里用一次）。

Imports System.Threading.Tasks
Imports System.Windows.Media.Effects
Imports System.Windows.Media.Imaging

''' <summary>玻璃表面：位置/尺寸变化后需要重新对齐底图。</summary>
Public Interface IGlassPanel
    ''' <summary>重新按当前位置对齐自己的玻璃底图。</summary>
    Sub GlassRefreshBackdrop()
End Interface

''' <summary>弹窗玻璃的刷新目标。</summary>
Friend Class GlassDialogPanel
    Implements IGlassPanel
    Public ReadOnly Border As Border
    Public ReadOnly Backdrop As Shapes.Rectangle
    Public Sub New(Border As Border, Backdrop As Shapes.Rectangle)
        Me.Border = Border
        Me.Backdrop = Backdrop
    End Sub
    Public Sub GlassRefreshBackdrop() Implements IGlassPanel.GlassRefreshBackdrop
        If Border Is Nothing OrElse Backdrop Is Nothing Then Return
        Backdrop.Fill = ModGlass.GlassSurfaceBrush(Border, True)
    End Sub
End Class

Public Module ModGlass

    ''' <summary>已玻璃化的容器标记（防止同一个 Border 被套两层玻璃）。</summary>
    Friend ReadOnly GlassHostTag As New Object

#Region "参数"

    Private Const CornerDefault As Double = 16
    Private Const UiBlurDefault As Double = 34

    ''' <summary>
    ''' 底图的总像素预算。高斯模糊是 CPU 软件渲染，面积 × 半径一起涨，
    ''' 全屏 225%～250% 时不设上限会把一次烘焙拖到几百毫秒（历史上就是这里「高 DPI 卡死」）。
    ''' </summary>
    Private Const MaxSurfacePixels As Double = 4200000

    ''' <summary>
    ''' 底图缩放系数：位图像素数与屏幕物理像素 1:1。
    ''' 这样模糊的渐变是逐像素的，铺回屏幕不再被放大，也就不会出现块状的「小颗粒」。
    ''' </summary>
    ''' <param name="Width">采样区域的宽（DIP）。</param>
    ''' <param name="Height">采样区域的高（DIP）。</param>
    Friend Function SnapshotScale(Element As FrameworkElement, Width As Double, Height As Double) As Double
        '必须按设备像素拍：降采样的位图铺回屏幕要放大 DPI 倍，模糊的渐变会被放大成
        '一块一块的「小颗粒」。全分辨率 + 模糊半径乘 DPI，才是真正平滑的高斯模糊。
        '历史上 250% 卡死的是 BitmapCache（挂在视觉树上、每帧失效重光栅化），
        '这里只是离屏烘焙一次再缓存复用，不进视觉树，全分辨率没有那个风险。
        Dim Scale As Double = 0
        Try
            Scale = VisualTreeHelper.GetDpi(Element).DpiScaleX
        Catch
        End Try
        'GetDpi 不可靠：窗口还没挂上 PresentationSource、DPI 正在切换、元素在弹窗里，
        '都可能安静地给出 1.0（等于什么都没做）。用进程级 DPI 兜底，两者取大。
        Try
            Scale = Math.Max(Scale, DPI / 96.0)
        Catch
        End Try
        If Double.IsNaN(Scale) OrElse Scale < 1.0 Then Scale = 1.0
        Scale = Math.Min(Scale, 3.0)
        '像素预算：超了就整体等比降一点（不是硬砍到某个宽度），
        '这样底图分辨率始终贴着设备像素走，只是极端尺寸下略降。
        Dim Pixels As Double = Math.Max(1, Width) * Math.Max(1, Height) * Scale * Scale
        If Pixels > MaxSurfacePixels Then Scale *= Math.Sqrt(MaxSurfacePixels / Pixels)
        Return Math.Max(1.0, Scale)
    End Function

    ''' <summary>玻璃圆角（0～28，0 为直角）。</summary>
    Public ReadOnly Property GlassCornerRadius As Double
        Get
            Dim Value As Integer = CornerDefault
            Try
                Value = Settings.Get(Of Integer)("UiGlassCorner")
            Catch
            End Try
            Return Math.Max(0, Math.Min(28, Value))
        End Get
    End Property

    ''' <summary>UI 模糊（0～60）：玻璃背后那一整张界面快照的模糊程度。</summary>
    Public ReadOnly Property GlassUiBlur As Double
        Get
            Dim Value As Integer = UiBlurDefault
            Try
                Value = Settings.Get(Of Integer)("UiUiBlur")
            Catch
            End Try
            Return Math.Max(0, Math.Min(60, Value))
        End Get
    End Property

    ''' <summary>卡片磨砂强度（0～100）。0 = 只剩描边，100 = 默认浓度。</summary>
    Public ReadOnly Property GlassCardOpacity As Integer
        Get
            Try
                Return Math.Max(0, Math.Min(100, Settings.Get(Of Integer)("UiGlassCardOpacity")))
            Catch
                Return 100
            End Try
        End Get
    End Property

#End Region

#Region "笔刷（冻结复用，全模块共享）"

    Private _Frost As Brush = Nothing
    Private _FrostKey As Integer = -1
    ''' <summary>
    ''' 卡片磨砂提亮层：半透明白色渐变，浓度由「磨砂强度」缩放。
    ''' 与壁纸无关，拿不到底图时它就是卡片唯一的底色。
    ''' </summary>
    Public Function GlassCardTint() As Brush
        '纯毛玻璃：磨砂层只做极轻的提亮，磨砂强度 0 时完全透明（不要再留 60% 的白色蒙板）。
        '可读性交给卡片底图的模糊与描边，而不是一层白纱。
        Dim Factor As Integer = Math.Max(0, Math.Min(100, GlassCardOpacity))
        If Factor <= 0 Then Return Nothing '返回 Nothing 表示不铺磨砂层
        If _Frost IsNot Nothing AndAlso _FrostKey = Factor Then Return _Frost
        Dim Brush As New LinearGradientBrush With {.StartPoint = New Point(0, 0), .EndPoint = New Point(0.35, 1)}
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(CByte(22 * Factor \ 100), 255, 255, 255), 0))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(CByte(18 * Factor \ 100), 255, 255, 255), 0.16))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(CByte(26 * Factor \ 100), 253, 254, 255), 0.56))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(CByte(34 * Factor \ 100), 251, 252, 255), 1))
        Brush.Freeze()
        _Frost = Brush
        _FrostKey = Factor
        Return Brush
    End Function

    Private _DialogTint As Brush = Nothing
    ''' <summary>弹窗磨砂提亮层：比卡片厚，保证任何壁纸上文字都清楚。</summary>
    Public Function GlassDialogTint() As Brush
        If _DialogTint IsNot Nothing Then Return _DialogTint
        Dim Brush As New LinearGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(120, 246, 250, 255), New Point(0, 0), New Point(0.3, 1))
        Brush.Freeze()
        _DialogTint = Brush
        Return Brush
    End Function

    Private _SolidTint As Brush = Nothing
    ''' <summary>简洁版的浓磨砂：接近不透明的白，保住文字对比度，但仍叠在模糊底图之上。</summary>
    Public Function GlassSolidTint() As Brush
        If _SolidTint IsNot Nothing Then Return _SolidTint
        Dim Brush As New SolidColorBrush(Color.FromArgb(242, 255, 255, 255))
        Brush.Freeze()
        _SolidTint = Brush
        Return Brush
    End Function

    Private _VideoTint As Brush = Nothing
    Private _VideoTintBlur As Double = -1
    ''' <summary>
    ''' 视频背景下的玻璃底色。
    ''' MediaElement 在分层窗口里抓不到帧（RenderTargetBitmap 取不到视频画面），所以只能铺一层「雾」。
    ''' 雾的浓度跟随 UI 模糊滑块：模糊为 0 时返回 Nothing（UI 完全透出视频，不再压一层白），
    ''' 模糊调大雾渐浓，上限仍是极淡中性（不超过 44），避免叠成黑/白蒙版。
    ''' </summary>
    Public Function GlassVideoTint() As Brush
        Dim B As Double = Math.Max(0, Math.Min(60, GlassUiBlur))
        If _VideoTintBlur = B AndAlso (_VideoTint IsNot Nothing OrElse B <= 0.5) Then Return _VideoTint
        _VideoTintBlur = B
        If B <= 0.5 Then
            'UI 模糊关到最低：完全透，一层雾都不铺。
            _VideoTint = Nothing
            Return Nothing
        End If
        Dim K As Double = (B - 1) / 59.0
        Dim Brush As New LinearGradientBrush With {.StartPoint = New Point(0, 0), .EndPoint = New Point(0.4, 1)}
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(CByte(Math.Round(5 + 21 * K)), 255, 255, 255), 0))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(CByte(Math.Round(7 + 27 * K)), 250, 251, 253), 0.5))
        Brush.GradientStops.Add(New GradientStop(Color.FromArgb(CByte(Math.Round(10 + 34 * K)), 244, 247, 252), 1))
        Brush.Freeze()
        _VideoTint = Brush
        Return Brush
    End Function

    ''' <summary>视频背景正在显示时（抓不到帧，只能用半透明深色玻璃）。</summary>
    Public ReadOnly Property IsVideoBackdrop As Boolean
        Get
            Try
                Return FrmMain IsNot Nothing AndAlso FrmMain.IsVideoBackgroundActive
            Catch
                Return False
            End Try
        End Get
    End Property

    Private _Edge As Brush = Nothing
    ''' <summary>外沿发丝亮边。</summary>
    Public Function GlassEdgeBrush() As Brush
        If _Edge IsNot Nothing Then Return _Edge
        Dim Brush As New SolidColorBrush(Color.FromArgb(76, 255, 255, 255))
        Brush.Freeze()
        _Edge = Brush
        Return Brush
    End Function

    Private _CardEdge As Brush = Nothing
    ''' <summary>卡片外沿发丝亮边（比弹窗淡一点）。</summary>
    Public Function GlassCardEdgeBrush() As Brush
        If _CardEdge IsNot Nothing Then Return _CardEdge
        Dim Brush As New SolidColorBrush(Color.FromArgb(52, 255, 255, 255))
        Brush.Freeze()
        _CardEdge = Brush
        Return Brush
    End Function

#End Region

#Region "离屏模糊"

    ''' <summary>
    ''' 把元素按「自身原点」拍成位图。
    ''' 不能直接用 RenderTargetBitmap.Render(Element)：那样会把元素在父级里的布局偏移一起拍进去，
    ''' ImgBack 挂在 Grid.Row=1 上、PanForm 在 BorderForm 里都有偏移，拍出来的画面就会整体下沉，
    ''' 铺回卡片时表现为「玻璃层比卡片低了一截」（偏移量正好等于导航栏高度）。
    ''' 走 VisualBrush 从 (0,0) 起画，绕开 VisualOffset。
    ''' </summary>
    Private Function SnapshotVisual(Element As FrameworkElement, Width As Double, Height As Double, Scale As Double, PixelWidth As Integer, PixelHeight As Integer) As RenderTargetBitmap
        Dim Shot As New RenderTargetBitmap(Math.Max(1, PixelWidth), Math.Max(1, PixelHeight), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32)
        Dim Wrap As New DrawingVisual()
        Using DC As DrawingContext = Wrap.RenderOpen()
            Dim VB As New VisualBrush(Element) With {
                .Stretch = Stretch.None,
                .AlignmentX = AlignmentX.Left,
                .AlignmentY = AlignmentY.Top,
                .TileMode = TileMode.None}
            DC.DrawRectangle(VB, Nothing, New Rect(0, 0, Math.Max(1, Width), Math.Max(1, Height)))
        End Using
        Shot.Render(Wrap)
        Shot.Freeze()
        Return Shot
    End Function

    ''' <summary>
    ''' 高斯模糊：自己做三次盒式模糊（≈ 高斯），不再用 BlurEffect。
    ''' 分层窗口（AllowsTransparency）只能走 CPU 软件光栅化，BlurEffect 在 225% 缩放下处理一张
    ''' 2292x928 的底图要 700 ms 以上，整窗 UI 线程全程卡住——这就是「高 DPI 卡死」的真身。
    ''' 同一张图这里 40 ms 左右，所以底图才敢按设备像素全分辨率烘焙（分辨率不够就会被放大成颗粒）。
    ''' Pbgra32 是预乘 alpha，四个通道各自模糊就是正确的高斯。
    ''' </summary>
    Private Function Blur(Source As BitmapSource, Radius As Double, PixelWidth As Integer, PixelHeight As Integer) As BitmapSource
        If Radius <= 0.35 Then Return Source
        Dim W As Integer = Math.Max(1, PixelWidth)
        Dim H As Integer = Math.Max(1, PixelHeight)
        Try
            Dim Stride As Integer = W * 4
            Dim Raw(Stride * H - 1) As Byte
            Dim Flat As BitmapSource = Source
            If Flat.Format <> PixelFormats.Pbgra32 Then Flat = New FormatConvertedBitmap(Source, PixelFormats.Pbgra32, Nothing, 0)
            Flat.CopyPixels(Raw, Stride, 0)
            '三次盒式模糊的等效 sigma ≈ 盒宽/2；BlurEffect 的 Radius 与它同量级，所以盒半径取一半。
            Dim BoxRadius As Integer = Math.Max(1, CInt(Math.Round(Radius * 0.5)))
            Dim Plane(W * H - 1) As Byte
            Dim Temp(W * H - 1) As Byte
            Dim Offset As Integer
            For Channel As Integer = 0 To 3
                Offset = Channel
                For P As Integer = 0 To W * H - 1
                    Plane(P) = Raw(Offset)
                    Offset += 4
                Next
                For Pass As Integer = 1 To 3
                    BlurPlane(Plane, Temp, W, H, BoxRadius)
                Next
                Offset = Channel
                For P As Integer = 0 To W * H - 1
                    Raw(Offset) = Plane(P)
                    Offset += 4
                Next
            Next
            Dim Result As BitmapSource = BitmapSource.Create(W, H, 96, 96, PixelFormats.Pbgra32, Nothing, Raw, Stride)
            Result.Freeze()
            Return Result
        Catch ex As Exception
            Dim Inner As Exception = ex
            Do While Inner.InnerException IsNot Nothing
                Inner = Inner.InnerException
            Loop
            Logger.Info($"[Glass] 盒式模糊失败，回退到 BlurEffect：{Inner.GetType().Name} {Inner.Message}")
            Return BlurByEffect(Source, Radius, W, H)
        End Try
    End Function

    ''' <summary>单通道盒式模糊一遍（先横后纵），结果写回 Plane，Temp 只作中转。边缘按复制处理。</summary>
    Private Sub BlurPlane(Plane() As Byte, Temp() As Byte, W As Integer, H As Integer, R As Integer)
        Dim Count As Integer = R * 2 + 1
        Dim LastW As Integer = W - 1
        Dim LastH As Integer = H - 1
        '注意：VB 里 Byte - Byte 的结果类型仍是 Byte，差值为负会直接抛 OverflowException
        '（对应 .NET 的 checked 窄化转换）。所以四处滑窗累减都必须先 CInt 再相减。
        Parallel.For(0, H,
            Sub(Y As Integer)
                Dim Base As Integer = Y * W
                Dim Sum As Integer = 0
                For K As Integer = -R To R
                    Sum += Plane(Base + Math.Min(LastW, Math.Max(0, K)))
                Next
                For X As Integer = 0 To LastW
                    Temp(Base + X) = CByte(Sum \ Count)
                    Sum += CInt(Plane(Base + Math.Min(LastW, X + R + 1))) - CInt(Plane(Base + Math.Max(0, X - R)))
                Next
            End Sub)
        Parallel.For(0, W,
            Sub(X As Integer)
                Dim Sum As Integer = 0
                For K As Integer = -R To R
                    Sum += Temp(Math.Min(LastH, Math.Max(0, K)) * W + X)
                Next
                For Y As Integer = 0 To LastH
                    Plane(Y * W + X) = CByte(Sum \ Count)
                    Sum += CInt(Temp(Math.Min(LastH, Y + R + 1) * W + X)) - CInt(Temp(Math.Max(0, Y - R) * W + X))
                Next
            End Sub)
    End Sub

    ''' <summary>兜底：盒式模糊出异常时仍用 WPF 的 BlurEffect，宁可慢也不要空底图。</summary>
    Private Function BlurByEffect(Source As BitmapSource, Radius As Double, W As Integer, H As Integer) As BitmapSource
        Dim Draw As New DrawingVisual()
        Draw.Effect = New BlurEffect With {.Radius = Radius, .KernelType = KernelType.Gaussian, .RenderingBias = RenderingBias.Performance}
        Using DC As DrawingContext = Draw.RenderOpen()
            DC.DrawImage(Source, New Rect(0, 0, W, H))
        End Using
        Dim Out As New RenderTargetBitmap(Math.Max(1, W), Math.Max(1, H), 96, 96, PixelFormats.Pbgra32)
        Out.Render(Draw)
        Out.Freeze()
        Return Out
    End Function

#End Region

#Region "壁纸快照（卡片 / 侧栏）"

    ''' <summary>
    ''' 启动动画结束、界面稳定后才允许生成底图。
    ''' 启动阶段界面还在布局与实例化，这时候 RenderTargetBitmap + Blur 会直接拖慢启动，先不铺。
    ''' </summary>
    Public GlassReady As Boolean = False

    Private _Wall As BitmapSource = Nothing
    Private _WallKey As String = ""

    ''' <summary>壁纸或窗口尺寸变化后调用。</summary>
    Public Sub GlassInvalidate()
        _Wall = Nothing
        _WallKey = ""
        _Snapshot = Nothing
        _SnapshotGeneration = -1
        _Revision += 1
    End Sub

    Private _Revision As Integer = 0
    ''' <summary>
    ''' 底图缓存的版本号。任何一次 GlassInvalidate 都会 +1，
    ''' 界面样式刷新可以据此判断「底图变了要不要重铺」，避免无意义地遍历整棵可视化树。
    ''' </summary>
    Public ReadOnly Property SurfaceRevision As Integer
        Get
            Return _Revision
        End Get
    End Property

    Private Function WallBitmap() As BitmapSource
        Dim Img As Panel = Nothing
        Dim RawOpacity As Double = 1
        Dim RawEffect As Effects.Effect = Nothing
        Dim Cleaned As Boolean = False
        Try
            If Not GlassReady Then Return Nothing '启动阶段不铺底图
            If FrmMain Is Nothing OrElse FrmMain.ImgBack Is Nothing Then Return Nothing
            Img = FrmMain.ImgBack
            If Img.Visibility <> Visibility.Visible OrElse Img.ActualWidth < 32 OrElse Img.ActualHeight < 32 Then Return Nothing
            '壁纸透明度调到 0 等于没有背景图：这时不能再用缓存的位图，否则卡片上会残留背景图片
            If Img.Opacity <= 0.01 Then Return Nothing
            'UI 模糊调到最低（0）时必须真正关闭：底图是降采样位图，即使不糊也发虚，
            '所以直接不铺底图（卡片变成完全透明的玻璃），而不是给一个模糊下限。
            Dim Radius As Double = GlassUiBlur
            If Radius <= 0.5 Then Return Nothing
            '画笔对象本身不会换（烘焙只是换 ImageSource），所以键里必须带上图片源与「是否还在实时模糊」，
            '否则烘焙完成后会继续复用实时模糊阶段的旧位图。
            '注意：键必须在改动元素状态之前算好，否则透明度/Effect 永远被记成默认值，拖滑块不会重建。
            Dim SourceHash As Integer = 0
            Try
                Dim WallBrush = TryCast(Img.Background, ImageBrush)
                If WallBrush IsNot Nothing AndAlso WallBrush.ImageSource IsNot Nothing Then SourceHash = WallBrush.ImageSource.GetHashCode()
            Catch
            End Try
            Dim Key As String = $"{CInt(Img.ActualWidth) \ 64}x{CInt(Img.ActualHeight) \ 64}|{CInt(Radius)}|{CInt(Img.Opacity * 100)}|{SourceHash}|{If(Img.Effect Is Nothing, 0, 1)}|{CInt(Img.Margin.Left)}|{If(Img.Background Is Nothing, 0, Img.Background.GetHashCode())}"
            If _Wall IsNot Nothing AndAlso _WallKey = Key Then Return _Wall
            '渲染前必须清掉透明度与实时模糊：
            '  · Opacity < 1 时拍出来是半透明位图，铺到卡片上会透出窗口底色、显得发灰；
            '  · 还在实时 BlurEffect 阶段时拍出来已经糊过一次，再叠加 UI 模糊就成了双重模糊。
            RawOpacity = Img.Opacity
            RawEffect = Img.Effect
            If RawOpacity < 0.999 OrElse RawEffect IsNot Nothing Then
                Img.Opacity = 1
                Img.Effect = Nothing
                Cleaned = True
            End If
            Dim Scale As Double = SnapshotScale(Img, Img.ActualWidth, Img.ActualHeight)
            Dim Wi As Integer = Math.Max(1, CInt(Math.Ceiling(Img.ActualWidth * Scale)))
            Dim Hi As Integer = Math.Max(1, CInt(Math.Ceiling(Img.ActualHeight * Scale)))
            Dim Watch As Stopwatch = Stopwatch.StartNew()
            Dim Shot As RenderTargetBitmap = SnapshotVisual(Img, Img.ActualWidth, Img.ActualHeight, Scale, Wi, Hi)
            Dim Result As BitmapSource = Blur(Shot, Radius * Scale, Wi, Hi)
            Watch.Stop()
            _Wall = Result
            _WallKey = Key
            Logger.Info($"[Glass] 重建壁纸底图：{Wi}x{Hi}（{Scale:F2}x，原 {CInt(Img.ActualWidth)}x{CInt(Img.ActualHeight)} DIP），半径 {Radius}，透明度 {RawOpacity}，画笔 {Img.Background.GetHashCode()}，图源 {SourceHash}，耗时 {Watch.ElapsedMilliseconds} ms")
            Return Result
        Catch ex As Exception
            Logger.Info($"[Glass] 壁纸快照失败：{ex.Message}")
            Return Nothing
        Finally
            '状态一定要还原，否则用户设的壁纸透明度会被永久改成 100%
            If Cleaned AndAlso Img IsNot Nothing Then
                Try
                    Img.Opacity = RawOpacity
                    Img.Effect = RawEffect
                Catch
                End Try
            End If
        End Try
    End Function

#End Region

#Region "界面快照（弹窗，真正的 UI 模糊）"

    Private _Snapshot As BitmapSource = Nothing
    Private _SnapshotGeneration As Integer = -1
    Private _Generation As Integer = 0

    ''' <summary>界面内容或窗口尺寸变化后调用，让下一次弹窗重新拍快照。</summary>
    Public Sub GlassInvalidateSnapshot()
        _Generation += 1
    End Sub

    ''' <summary>
    ''' 拍一张「整个窗口」的界面快照并模糊：壁纸、卡片、文字全在里面，
    ''' 所以弹窗玻璃后面是真正被糊掉的界面，而不是一张模糊壁纸。
    ''' 调用前必须先把要玻璃化的弹窗自身隐藏，否则会把自己拍进去。
    ''' </summary>
    Private Function SnapshotBitmap() As BitmapSource
        Try
            If Not GlassReady Then Return Nothing '启动阶段不拍快照
            If GlassUiBlur <= 0.5 Then Return Nothing 'UI 模糊关闭时不铺底图
            If FrmMain Is Nothing Then Return Nothing
            Dim PanForm = FrmMain.PanForm
            If PanForm Is Nothing Then Return Nothing
            Dim W As Double = PanForm.ActualWidth, H As Double = PanForm.ActualHeight
            If W < 32 OrElse H < 32 Then Return Nothing
            If _Snapshot IsNot Nothing AndAlso _SnapshotGeneration = _Generation Then Return _Snapshot
            Dim Scale As Double = SnapshotScale(PanForm, W, H)
            Dim Wi As Integer = Math.Max(1, CInt(Math.Ceiling(W * Scale)))
            Dim Hi As Integer = Math.Max(1, CInt(Math.Ceiling(H * Scale)))
            Dim Watch As Stopwatch = Stopwatch.StartNew()
            Dim Shot As RenderTargetBitmap = SnapshotVisual(PanForm, W, H, Scale, Wi, Hi)
            Dim Result As BitmapSource = Blur(Shot, GlassUiBlur * Scale, Wi, Hi)
            Watch.Stop()
            _Snapshot = Result
            _SnapshotGeneration = _Generation
            Logger.Info($"[Glass] 重建界面快照：{Wi}x{Hi}（{Scale:F2}x），半径 {GlassUiBlur}，耗时 {Watch.ElapsedMilliseconds} ms")
            Return Result
        Catch ex As Exception
            Logger.Info($"[Glass] 界面快照失败：{ex.Message}")
            Return Nothing
        End Try
    End Function

#End Region

#Region "对齐"

    ''' <summary>
    ''' 创建一个「可复用」的玻璃底图笔刷（不冻结）。配合 <see cref="AlignSurfaceBrush"/> 使用，
    ''' 滚动时只改 ImageSource / Viewport，不再每 90 ms 新建一个笔刷对象。
    ''' </summary>
    Public Function CreateSurfaceBrush() As ImageBrush
        Return New ImageBrush With {
            .Stretch = Stretch.Fill,
            .ViewboxUnits = BrushMappingMode.Absolute,
            .ViewportUnits = BrushMappingMode.Absolute,
            .AlignmentX = AlignmentX.Left,
            .AlignmentY = AlignmentY.Top,
            .TileMode = TileMode.None}
    End Function

    ''' <summary>排查玻璃对齐用：置 True 会把每次对齐的 Offset/Viewport 写进日志（默认关闭）。</summary>
    Public GlassDiagLog As Boolean = False
    Private _DiagCount As Integer = 0

    ''' <summary>
    ''' 把已有笔刷对齐到元素当前位置。返回是否成功（失败时调用方应退化为纯磨砂）。
    ''' UseSnapshot = True 用界面快照（弹窗），False 用壁纸快照（卡片 / 侧栏）。
    ''' </summary>
    ''' <param name="RequireLaidOut">
    ''' 要求元素已经有实际尺寸才对齐。卡片要走 True：卡片还没参与布局时 TranslatePoint 会安静地返回 (0,0)，
    ''' 底图会被钉到窗口左上角。侧栏不能走 True：它收起时宽度就是 0，而 Viewport 只依赖 Offset、与元素尺寸无关，
    ''' 展开后照样正确，一旦判定失败反而会让侧栏丢掉玻璃底图。
    ''' </param>
    Public Function AlignSurfaceBrush(Brush As ImageBrush, Element As FrameworkElement, Optional UseSnapshot As Boolean = False, Optional RequireLaidOut As Boolean = False) As Boolean
        Try
            If Brush Is Nothing OrElse Element Is Nothing OrElse FrmMain Is Nothing Then Return False
            Dim Reference As FrameworkElement
            Dim Bitmap As BitmapSource
            If UseSnapshot Then
                Reference = FrmMain.PanForm
                Bitmap = SnapshotBitmap()
            Else
                Reference = FrmMain.ImgBack
                Bitmap = WallBitmapCached()
            End If
            If Bitmap Is Nothing OrElse Reference Is Nothing Then Return False
            '元素还没参与布局时 TranslatePoint 会老老实实返回 (0,0) 而不是报错，
            '于是底图会被对齐到窗口左上角，卡片上看到的模糊壁纸整体错位一大截。
            '这里必须当成「取不到位置」处理，让调用方退化成纯磨砂，等布局完成后再对齐。
            If RequireLaidOut AndAlso (Element.ActualWidth < 1 OrElse Element.ActualHeight < 1) Then Return False
            Dim Offset As Point
            Try
                Offset = Element.TranslatePoint(New Point(0, 0), Reference)
            Catch
                Return False
            End Try
            If Double.IsNaN(Offset.X) OrElse Double.IsNaN(Offset.Y) Then Return False
            If Not Object.ReferenceEquals(Brush.ImageSource, Bitmap) Then
                Brush.ImageSource = Bitmap
                Brush.Viewbox = New Rect(0, 0, Bitmap.PixelWidth, Bitmap.PixelHeight)
            End If
            '笔刷铺的是 Border 的 Background（边框内侧），坐标系原点在 (BorderThickness.Left, BorderThickness.Top)，
            '而不是元素原点。不补偿的话底图会整体偏移一个边框厚度（1 DIP ≈ 高 DPI 下 2~3 px），方向是右下。
            Dim BiasX As Double = 0, BiasY As Double = 0
            Dim Bd As Border = TryCast(Element, Border)
            If Bd IsNot Nothing Then
                BiasX = Bd.BorderThickness.Left
                BiasY = Bd.BorderThickness.Top
            End If
            Dim NewViewport As New Rect(-Offset.X - BiasX, -Offset.Y - BiasY, Math.Max(1, Reference.ActualWidth), Math.Max(1, Reference.ActualHeight))
            If Brush.Viewport <> NewViewport Then Brush.Viewport = NewViewport
            If GlassDiagLog AndAlso _DiagCount < 200 Then
                _DiagCount += 1
                Try
                    Logger.Info($"[GlassDiag] {Element.GetType().Name}#{_DiagCount} elem={Element.ActualWidth:F1}x{Element.ActualHeight:F1} off=({Offset.X:F1},{Offset.Y:F1}) bmp={Bitmap.PixelWidth}x{Bitmap.PixelHeight} vp=({NewViewport.X:F1},{NewViewport.Y:F1},{NewViewport.Width:F1},{NewViewport.Height:F1})")
                Catch
                End Try
            End If
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 一次性生成冻结的玻璃底图笔刷（低频调用者用，例如侧栏）。
    ''' </summary>
    Public Function GlassSurfaceBrush(Element As FrameworkElement, Optional UseSnapshot As Boolean = False) As Brush
        Try
            Dim Brush As ImageBrush = CreateSurfaceBrush()
            If Not AlignSurfaceBrush(Brush, Element, UseSnapshot) Then Return Nothing
            Brush.Freeze()
            Return Brush
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 已烘焙的壁纸底图。滚动等高频路径直接取缓存，不重新计算缓存键。
    ''' 壁纸变化会由 300 ms 的签名监视调用 GlassInvalidate 清空缓存。
    ''' </summary>
    Public Function WallBitmapCached() As BitmapSource
        If _Wall IsNot Nothing Then Return _Wall
        Return WallBitmap()
    End Function

    ''' <summary>直接把某个形状（侧栏背景板）的填充换成对齐后的玻璃底图；拿不到时保留原填充。</summary>
    Public Sub GlassRefreshFill(Element As Shapes.Shape)
        If Element Is Nothing Then Return
        Try
            Dim Brush As Brush = GlassSurfaceBrush(Element, False)
            If Brush IsNot Nothing Then
                Element.Fill = Brush
            ElseIf IsVideoBackdrop Then
                '视频背景播放中：抓不到视频帧，用半透明深色玻璃让视频透出来（否则会是一块近白色）
                Element.Fill = GlassVideoTint()
            ElseIf GlassReady Then
                '毛玻璃版但拿不到底图（UI 模糊关闭 / 背景图被关闭）：保持透明。
                '旧实现回落到 ColorBrushBackgroundTransparentSidebar（#F1FFFFFF，94% 白），
                '在关闭模糊时就是一块实打实的白色蒙板，正是用户看到的那层白。
                Element.Fill = Nothing
            End If
        Catch
        End Try
    End Sub

#End Region

#Region "壁纸变化监视（自愈）"

    '背景图的更换路径有好几条（设置页导入 / 刷新 / 恢复默认 / 视频切换 / 启动加载），
    '只要有一条没通知到玻璃层，就会出现「窗口是新壁纸、卡片还是旧壁纸」。
    '与其一条条补通知，这里用一个 300 ms 的轻量签名比较兜底：签名一变就作废缓存并重铺。
    Private _WatchTimer As Windows.Threading.DispatcherTimer = Nothing
    Private _WatchSignature As String = ""
    Private _OnWallpaperChanged As Action = Nothing

    ''' <summary>主窗口注册回调：壁纸签名变化时重铺所有玻璃面板。</summary>
    Public Sub GlassWatchWallpaper(OnChanged As Action)
        _OnWallpaperChanged = OnChanged
        If _WatchTimer IsNot Nothing Then Return
        _WatchTimer = New Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(300)}
        AddHandler _WatchTimer.Tick, AddressOf WatchWallpaper
        _WatchTimer.Start()
    End Sub

    Private Function WallpaperSignature() As String
        Try
            If FrmMain Is Nothing OrElse FrmMain.ImgBack Is Nothing Then Return ""
            Dim Img = FrmMain.ImgBack
            Dim Brush = TryCast(Img.Background, ImageBrush)
            Dim SourceHash As Integer = 0
            If Brush IsNot Nothing AndAlso Brush.ImageSource IsNot Nothing Then SourceHash = Brush.ImageSource.GetHashCode()
            Return $"{Img.Visibility}|{CInt(Img.Opacity * 100)}|{CInt(Img.ActualWidth) \ 64}x{CInt(Img.ActualHeight) \ 64}|{SourceHash}|{If(Brush Is Nothing, 0, Brush.GetHashCode())}|{CInt(GlassUiBlur)}"
        Catch
            Return ""
        End Try
    End Function

    Private Sub WatchWallpaper(sender As Object, e As EventArgs)
        Try
            Dim Signature As String = WallpaperSignature()
            If Signature = _WatchSignature Then Return
            _WatchSignature = Signature
            GlassInvalidate()
            If _OnWallpaperChanged IsNot Nothing Then _OnWallpaperChanged()
        Catch ex As Exception
            Logger.Info($"[Glass] 壁纸变化监视失败：{ex.Message}")
        End Try
    End Sub

#End Region

#Region "对齐去抖队列"

    Private ReadOnly _Pending As New List(Of WeakReference)
    Private _FlushTimer As Windows.Threading.DispatcherTimer = Nothing
    ''' <summary>挂起计数。页面切换等全窗动画期间挂起重铺，避免动画中途做 RenderTargetBitmap + 模糊。</summary>
    Private _SuspendCount As Integer = 0

    ''' <summary>
    ''' 挂起毛玻璃重铺。必须在 ResumeRefresh 之前成对调用。
    ''' </summary>
    Public Sub SuspendRefresh()
        _SuspendCount += 1
    End Sub

    ''' <summary>
    ''' 恢复重铺，并把挂起期间积压的请求一次性处理。
    ''' </summary>
    Public Sub ResumeRefresh()
        If _SuspendCount > 0 Then _SuspendCount -= 1
        If _SuspendCount > 0 Then Return
        If _Pending.Count = 0 Then Return
        If _FlushTimer Is Nothing Then
            _FlushTimer = New Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(90)}
            AddHandler _FlushTimer.Tick, AddressOf FlushPending
        End If
        _FlushTimer.Stop()
        _FlushTimer.Start()
    End Sub

    ''' <summary>
    ''' 请求重新对齐。滚动、动画期间会被反复调用，这里只登记，等布局安静下来（90 ms）统一处理。
    ''' </summary>
    Public Sub GlassQueueRefresh(Panel As IGlassPanel)
        If Panel Is Nothing Then Return
        SyncLock _Pending
            For Each Ref As WeakReference In _Pending
                If Ref.Target IsNot Nothing AndAlso Ref.Target Is Panel Then Return
            Next
            _Pending.Add(New WeakReference(Panel))
        End SyncLock
        '挂起期间只登记不启动定时器：动画里每 90 ms 重铺一次会把整窗软件渲染彻底压垮
        If _SuspendCount > 0 Then Return
        If _FlushTimer Is Nothing Then
            _FlushTimer = New Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(90)}
            AddHandler _FlushTimer.Tick, AddressOf FlushPending
        End If
        _FlushTimer.Stop()
        _FlushTimer.Start()
    End Sub

    Private Sub FlushPending(sender As Object, e As EventArgs)
        If _FlushTimer IsNot Nothing Then _FlushTimer.Stop()
        Dim Targets As New List(Of IGlassPanel)
        SyncLock _Pending
            For Each Ref As WeakReference In _Pending
                Dim Target As IGlassPanel = TryCast(Ref.Target, IGlassPanel)
                If Target IsNot Nothing Then Targets.Add(Target)
            Next
            _Pending.Clear()
        End SyncLock
        For Each Target As IGlassPanel In Targets
            Try
                Target.GlassRefreshBackdrop()
            Catch
            End Try
        Next
    End Sub

#End Region

#Region "弹窗"

    ''' <summary>
    ''' 把一个弹窗的外框做成毛玻璃：界面快照底图（背后界面真的被糊掉）+ 磨砂提亮 + 发丝亮边。
    ''' 只在其下方插入两层矩形，不改动原内容，不挂任何 Effect。
    ''' </summary>
    ''' <param name="Border">弹窗最外层 Border（PanBorder）。</param>
    Public Sub GlassAttachDialog(Border As Border)
        If Border Is Nothing OrElse FrmMain Is Nothing Then Return
        Dim Content As UIElement = Border.Child
        If Content Is Nothing Then Return
        '幂等保护：同一个 Border 被玻璃化两次会套两层底图 + 两层磨砂，看起来就是「背景叠了两层」。
        'Loaded 事件在某些情况下会重入（控件移出/重新挂回可视化树），所以这里必须挡住。
        If TypeOf Content Is Grid AndAlso CType(Content, Grid).Tag Is GlassHostTag Then Return
        Try
            Dim Corner As Double = Math.Max(0, GlassCornerRadius)
            Dim Host As New Grid With {.Tag = GlassHostTag}
            Dim Backdrop As New Shapes.Rectangle With {.IsHitTestVisible = False, .RadiusX = Corner, .RadiusY = Corner}
            Dim Tint As New Shapes.Rectangle With {.Fill = GlassDialogTint(), .IsHitTestVisible = False, .RadiusX = Corner, .RadiusY = Corner}

            Border.Background = Nothing
            Border.CornerRadius = New CornerRadius(Corner)
            Border.BorderThickness = New Thickness(1)
            Border.BorderBrush = GlassEdgeBrush()

            '拍快照前先把自己藏起来，否则弹窗会出现在自己的底图里
            Dim RawVisibility As Visibility = Border.Visibility
            Border.Visibility = Visibility.Collapsed
            '弹窗的遮罩是 Loaded 之后才淡入的，快照时若还透明，底图就会比周围环境亮，像贴了张亮纸。
            '这里临时把遮罩压到目标色，拍完立刻还原，动画照常进行。
            Dim RawMask As Brush = Nothing
            Dim MaskTouched As Boolean = False
            Try
                If FrmMain.PanMsg IsNot Nothing Then
                    RawMask = FrmMain.PanMsg.Background
                    FrmMain.PanMsg.Background = New SolidColorBrush(Color.FromArgb(90, 0, 0, 0))
                    MaskTouched = True
                End If
            Catch
            End Try
            Try
                GlassInvalidateSnapshot() '每次打开都重拍，保证底图与当前界面一致
                FrmMain.PanForm.UpdateLayout()
                Backdrop.Fill = GlassSurfaceBrush(Border, True)
            Finally
                Border.Visibility = RawVisibility
                If MaskTouched Then
                    Try
                        FrmMain.PanMsg.Background = RawMask
                    Catch
                    End Try
                End If
            End Try

            Host.Children.Add(Backdrop)
            Host.Children.Add(Tint)
            Border.Child = Nothing
            Host.Children.Add(Content)
            Border.Child = Host

            '内容换行、窗口缩放等导致尺寸变化时重新对齐（复用同一张快照，不再重拍）
            Dim Target As New GlassDialogPanel(Border, Backdrop)
            AddHandler Host.SizeChanged, Sub() GlassQueueRefresh(Target)
        Catch ex As Exception
            Logger.Info($"[Glass] 弹窗玻璃化失败：{ex.Message}")
        End Try
    End Sub

#End Region

End Module

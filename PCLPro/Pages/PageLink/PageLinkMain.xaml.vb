Imports System.Collections.ObjectModel
Imports System.Globalization
Imports System.Net.Sockets
Imports System.Numerics
Imports System.Reflection
Imports System.Security.Cryptography
Imports PCL

Public Class PageLinkMain

    '===============================
    '  陶瓦联机（Terracotta）房间码
    '===============================

    ' 房间码与陶瓦 / HMCL 保持一致：U/XXXX-XXXX-XXXX-XXXX
    ' 由 128 位随机数对 34^16 取模并使其能被 7 整除得到；前 8 位字符即网络名，后 8 位即网络密码
    Private Const TC_CHARS As String = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ"
    ''' <summary>房间码中 U/ 之后的长度：16 位字符与 3 个连字符。</summary>
    Private Const TC_CODE_LEN As Integer = 21
    ' 公共节点由陶瓦程序内置维护（tcp://public.easytier.top:11010 等），启动器不再自行拼接
    Private Shared ReadOnly TC_CodeRange As BigInteger = BigInteger.Pow(34, 16)

    ''' <summary>生成一个随机的陶瓦房间，返回 (房间码, 网络名, 网络密码)。</summary>
    Private Shared Function TcRandomRoom() As Tuple(Of String, String, String)
        Dim Bytes(15) As Byte
        Using Rng As RandomNumberGenerator = RandomNumberGenerator.Create()
            Rng.GetBytes(Bytes)
        End Using
        Dim Buffer(16) As Byte '末位为 0，保证 BigInteger 被解释为正数
        Array.Copy(Bytes, Buffer, 16)
        Dim Value As New BigInteger(Buffer)
        Value = Value Mod TC_CodeRange
        Value -= Value Mod 7
        Return TcFormat(Value)
    End Function
    ''' <summary>把 128 位房间数值格式化为 (房间码, 网络名, 网络密码)。</summary>
    Private Shared Function TcFormat(Value As BigInteger) As Tuple(Of String, String, String)
        Dim Chars(15) As Char
        For i = 0 To 15
            Chars(i) = TC_CHARS(CInt(Value Mod 34))
            Value = BigInteger.Divide(Value, 34)
        Next
        Dim Code As String = "U/" & New String(Chars, 0, 4) & "-" & New String(Chars, 4, 4) & "-" &
                             New String(Chars, 8, 4) & "-" & New String(Chars, 12, 4)
        Dim Name As String = "scaffolding-mc-" & New String(Chars, 0, 4) & "-" & New String(Chars, 4, 4)
        Dim Secret As String = New String(Chars, 8, 4) & "-" & New String(Chars, 12, 4)
        Return New Tuple(Of String, String, String)(Code, Name, Secret)
    End Function
    ''' <summary>
    ''' 解析陶瓦房间码，失败返回 Nothing，成功返回 (房间码, 网络名, 网络密码)。
    ''' </summary>
    Private Shared Function TcParse(Code As String) As Tuple(Of String, String, String)
        If String.IsNullOrEmpty(Code) Then Return Nothing
        Dim Index As Integer = Code.IndexOf("U/", StringComparison.Ordinal)
        If Index < 0 OrElse Code.Length < Index + TC_CODE_LEN Then Return Nothing
        Dim Body As String = Code.Substring(Index + 2, TC_CODE_LEN - 2)
        If Body.Length <> 19 Then Return Nothing
        If Body(4) <> "-"c OrElse Body(9) <> "-"c OrElse Body(14) <> "-"c Then Return Nothing
        Body = Body.Replace("-", "")
        Dim Value As BigInteger = BigInteger.Zero
        For Each C As Char In Body
            Dim Ch As Char = Char.ToUpperInvariant(C)
            If Ch = "I"c Then Ch = "1"c
            If Ch = "O"c Then Ch = "0"c
            Dim Pos As Integer = TC_CHARS.IndexOf(Ch)
            If Pos < 0 Then Return Nothing
            Value = Value * 34 + Pos
        Next
        If Value Mod 7 <> 0 Then Return Nothing
        Return TcFormat(Value)
    End Function
    '===============================
    '  状态机与前端页面
    '===============================

#Region "状态管理"

    Public Enum LinkStates
        Waiting
        Loading
        Failed
        Finished
    End Enum
    Public Shared LinkState As LinkStates = LinkStates.Waiting

    ''' <summary>
    ''' 切换到指定的状态。
    ''' </summary>
    Private Sub ChangeState(NewState As LinkStates)
        If LinkState = NewState Then Return
        Dim OldState = LinkState
        LinkState = NewState
        Logger.Info($"主状态由 {OldState} 变更为 {NewState}")
        '触发状态切换
        RunInUi(
        Sub()
            Select Case NewState
                Case LinkStates.Waiting
                    SwitchToWaiting(OldState)
                Case LinkStates.Loading
                    SwitchToLoading(OldState)
                Case LinkStates.Failed
                    SwitchToFailed(OldState)
                Case LinkStates.Finished
                    SwitchToFinished(OldState)
            End Select
        End Sub)
    End Sub

    '让加载器的 成功/失败 事件触发状态切换
    Private Sub LoaderInit() Handles Me.Initialized
        PageLoaderInit(Load, PanLoad, PanContent, PanAlways, LinkLoader, AutoRun:=False)
        AddHandler LinkLoader.OnStateChangedUi,
        Sub(Loader As LoaderBase, NewState As LoadState, OldState As LoadState)
            Select Case NewState
                Case LoadState.Finished
                    ChangeState(LinkStates.Finished)
                Case LoadState.Failed
                    Telemetry("联机失败", "Exception", FilterUserName(Loader.Error.GetDisplay(True), "*"))
                    ChangeState(LinkStates.Failed)
            End Select
        End Sub
    End Sub

    '页面动画兼容
    Private Sub UpdatePanelVisibility() Handles Me.PageEnter
        FrmLinkMain.PanSelect.Visibility = If(LinkState = LinkStates.Waiting, Visibility.Visible, Visibility.Collapsed)
        FrmLinkMain.PanFinish.Visibility = If(LinkState = LinkStates.Finished, Visibility.Visible, Visibility.Collapsed)
    End Sub

#End Region

#Region "初始页面 (Waiting)"

    '由任意状态切换到 Waiting
    Private Sub SwitchToWaiting(OldState As LinkStates)
        '加载器与进程状态
        LinkLoader.Cancel()
        SyncLock LinkLoader.LockState
            LinkLoader.State = LoadState.Waiting
        End SyncLock
        ProcessStop()
        '页面切换
        If OldState <> LinkStates.Finished Then
            '从完成页面退出时，PageOnContentExit 结束会触发 PageEnter 事件，这会调用 UpdatePanelVisibility
            '如果在现在提前调用了，会丢失完成页面的退出动画
            UpdatePanelVisibility()
        End If
        PageOnContentExit()
    End Sub

    '===============================
    '  页面 UI
    '===============================

    '创建（按钮）
    Private LastCreateTrigger As Long = 0
    Private Sub BtnCreate_Click() Handles BtnCreate.Click
        '短时间内重复点击去重
        If Math.Abs(GetTimeMs() - LastCreateTrigger) < 500 Then Return
        LastCreateTrigger = GetTimeMs()
        '基础信息（使用陶瓦联机的房间码体系，游戏端口由陶瓦自动扫描，不需要用户填写）
        IsServerSide = True
        Dim Room = TcRandomRoom()
        RoomCode = Room.Item1
        NetworkName = Room.Item2
        NetworkSecret = Room.Item3
        Logger.Info($"尝试创建陶瓦房间，房间码 {RoomCode}，网络名 {NetworkName}")
        '启动
        ChangeState(LinkStates.Loading)
    End Sub

    '加入
    Private Shared LastCode As String = Nothing
    ''' <summary>
    ''' 输入房间码，切换到联机页并立即加入房间。
    ''' </summary>
    Public Shared Sub Join()
        '页面可见时：聚焦内联的房间码输入框，由「加入房间」按钮执行加入
        Try
            If FrmLinkMain IsNot Nothing AndAlso FrmLinkMain.TextJoinCode IsNot Nothing AndAlso FrmLinkMain.PanSelectJoin.IsVisible Then
                FrmLinkMain.TextJoinCode.Focus()
                Return
            End If
        Catch
        End Try
        '退回：弹窗输入房间码
        Dim Code As String = MyMsgBoxInput("输入房间码", "输入房主发给你的房间码。",
            HintText:=If(String.IsNullOrEmpty(LastCode), "", "使用上一次的房间码"))
        If Not String.IsNullOrEmpty(LastCode) AndAlso Code IsNot Nothing AndAlso Code = "" Then Code = LastCode
        If Code Is Nothing Then Return
        Join(Code)
    End Sub
    Private Sub BtnJoin_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnJoin.Click
        Dim Code As String = TextJoinCode.Text
        If String.IsNullOrWhiteSpace(Code) Then
            Hint("请先输入房主发你的房间码！", HintType.Red)
            TextJoinCode.Focus()
            Return
        End If
        JoinInternal(Code.Trim())
    End Sub
    Private Sub TextJoinCode_KeyDown(sender As Object, e As KeyEventArgs) Handles TextJoinCode.KeyDown
        If e.Key = Key.Enter Then BtnJoin_Click(Nothing, Nothing)
    End Sub
    Public Sub JoinInternal(Code As String)
        If Code Is Nothing Then Return
        '基础格式校验
        Dim ValidateResult = ValidateCodeFormat(Code)
        If ValidateResult IsNot Nothing Then
            Hint(ValidateResult, HintType.Red)
            Return
        End If
        Code = FixCodeFormat(Code)
        Logger.Info($"实际使用的联机房间码：{Code}")
        '基础信息
        Dim Room = TcParse(Code)
        If Room Is Nothing Then
            Hint("房间码有误，请让房主重新发送房间码！", HintType.Red)
            Return
        End If
        IsServerSide = False
        RoomCode = Room.Item1
        NetworkName = Room.Item2
        NetworkSecret = Room.Item3
        Logger.Info($"尝试加入陶瓦房间，房间码 {RoomCode}，网络名 {NetworkName}")
        '启动
        LastCode = Code
        ChangeState(LinkStates.Loading)
    End Sub
    Public Shared Function ValidateCodeFormat(Code As String) As String
        If String.IsNullOrWhiteSpace(Code) Then Return "房间码为空！"
        Code = FixCodeFormat(Code)
        If TcParse(Code) Is Nothing Then Return "房间码有误，请让房主重新发送房间码！"
        Return Nothing
    End Function
    ''' <summary>整理用户输入的房间码，去掉复制消息带来的多余字符。</summary>
    Private Shared Function FixCodeFormat(Code As String) As String
        Code = Code.Between("【", "】").Between("[", "]").Upper.Trim '从完整消息中提取
        Code = Code.Replace(" ", "").Replace(vbCrLf, "").Replace(vbCr, "")
        Dim Index As Integer = Code.IndexOf("U/", StringComparison.Ordinal)
        If Index > 0 Then Code = Code.Substring(Index)
        Return Code
    End Function

    '自动加入

    ''' <summary>
    ''' 切换到联机页并立即加入指定房间。
    ''' </summary>
    Public Shared Sub Join(Code As String)
        If LinkState <> LinkStates.Waiting Then
            Hint("你已经在联机房间中了！", HintType.Red)
        ElseIf FrmMain.PageCurrent = FormMain.PageType.Link Then
            FrmLinkMain.JoinInternal(Code)
        Else
            AutoJoinCode = Code
            FrmMain.PageChange(FormMain.PageType.Link, FormMain.PageSubType.LinkMain)
        End If
    End Sub
    '在进入页面时自动尝试加入房间
    Private Shared AutoJoinCode As String = Nothing
    Private Sub PanSelectJoin_Loaded() Handles PanSelectJoin.Loaded
        If AutoJoinCode Is Nothing Then Return
        If LinkState = LinkStates.Waiting Then JoinInternal(AutoJoinCode)
        AutoJoinCode = Nothing
    End Sub

#End Region

#Region "加载页面 (Loading | Failed)"

    '由 Waiting 或 Failed 状态切换到 Loading
    Private Sub SwitchToLoading(OldState As LinkStates)
        '清理变量
        TcFailCount = 0
        '加载器与进程状态
        LinkLoader.Start(IsForceRestart:=True)
        '页面切换会由 Loader 调用 MyPageRight 来触发
        'UI 更新
        UpdateProgressBar(0)
        LabLoadTitle.Text = If(IsServerSide, "创建房间中", "加入房间中")
        UpdateLoadingPage("正在初始化……", "准备初始化")
    End Sub

    '由 Loading 状态切换到 Failed
    Private Sub SwitchToFailed(OldState As LinkStates)
        '加载器与进程状态
        LinkLoader.Cancel()
        ProcessStop()
        'UI 更新
        UpdateProgressBar(1)
        '显示错误信息
        LabLoadTitle.Text = FailReason
        Dim Brief As String = LinkLoader.Error.GetDisplay(False)
        Dim InnerEx As Exception = LinkLoader.Error
        If InnerEx.Message.StartsWithF("$") Then Brief = InnerEx.Message
        Do Until InnerEx.InnerException Is Nothing
            InnerEx = InnerEx.InnerException
            If InnerEx.Message.StartsWithF("$") Then Brief = InnerEx.Message
        Loop
        LabLoadDesc.Text = If(Brief.StartsWithF("$"), Brief.TrimStart("$"), LinkLoader.Error.GetDisplay(True))
        Logger.Warn(LinkLoader.Error, LabLoadTitle.Text)
    End Sub

    '===============================
    '  页面 UI
    '===============================

    Private Sub UpdateLoadingPage(Title As String, FailBrief As String)
        If FailReason = FailBrief & "失败" Then Return
        FailReason = FailBrief & "失败"
        Logger.Info($"开始步骤：{Title}")
        RunInUiWait(
        Sub()
            If FrmLinkMain Is Nothing OrElse Not FrmLinkMain.LabLoadDesc.IsLoaded Then Return
            FrmLinkMain.LabLoadDesc.Text = Title
        End Sub)
    End Sub
    Private FailReason As String = "准备初始化"

    '取消加载
    Private Sub BtnLoadCancel_Click() Handles BtnLoadCancel.Click
        ChangeState(LinkStates.Waiting)
    End Sub

    '点击重试
    Private Sub CardLoad_MouseLeftButtonUp() Handles CardLoad.MouseLeftButtonUp
        ChangeState(LinkStates.Loading)
    End Sub

    '进度条
    Private Sub UpdateProgressBar(Optional Value As Double = -1)
        If Value = -1 Then Value = LinkLoader.Progress
        Dim DisplayingProgress As Double = ColumnProgressA.Width.Value
        If Math.Round(Value - DisplayingProgress, 3) = 0 Then Return
        If DisplayingProgress <= Value Then
            Dim NewProgress As Double = If(Value = 1, 1, DisplayingProgress +
                (Value - DisplayingProgress) ^ 2 * 0.5)
            AniStart({
                AaGridLengthWidth(ColumnProgressA, NewProgress - ColumnProgressA.Width.Value, 200),
                AaGridLengthWidth(ColumnProgressB, (1 - NewProgress) - ColumnProgressB.Width.Value, 200)
            }, "Link Progress")
        Else
            ColumnProgressA.Width = New GridLength(Value, GridUnitType.Star)
            ColumnProgressB.Width = New GridLength(1 - Value, GridUnitType.Star)
            AniStop("Link Progress")
        End If
    End Sub
    Private Sub CardLoad_SizeChanged() Handles CardLoad.SizeChanged
        RectProgressClip.Rect = New Rect(0, 0, CardLoad.ActualWidth, 12)
    End Sub

#End Region

#Region "完成页面 (Finished)"

    '由 Loading 状态切换到 Finished
    Private Sub SwitchToFinished(OldState As LinkStates)
        '由于只可能从加载器的完成事件触发，不需要管加载器
        UpdatePanelVisibility() '页面的实际切换会由 Loader 调用 MyPageRight 来触发

        'UI 更新
        If IsServerSide Then
            LabFinishTitle.Text = "已创建房间"
            LabFinishDesc.Text = $"把房间码发给朋友，让大家加入房间吧！{vbCrLf}房间码：{GetInviteCode()}"
            BtnFinishExit.Text = "关闭"
            BtnFinishPing.ToolTip = "网络延迟"
            BtnFinishCopy.Visibility = Visibility.Visible
            Copy() '立即复制房间码
            '下边栏：陶瓦联机不需要端口，房主不展示连接信息
            BtnFinishPort.Visibility = Visibility.Collapsed
            LabFinishPort.Visibility = Visibility.Collapsed
            LineFinishPort.Visibility = Visibility.Collapsed
            BtnFinishIp.Visibility = Visibility.Collapsed
            LabFinishIp.Visibility = Visibility.Collapsed
        Else
            LabFinishTitle.Text = "已加入房间"
            LabFinishDesc.Text = $"在多人游戏页面的最下方就能找到联机房间！{vbCrLf}注意：使用离线登录时不要手动输入 IP！"
            BtnFinishExit.Text = "离开"
            BtnFinishPing.ToolTip = "与房主的延迟"
            BtnFinishCopy.Visibility = Visibility.Collapsed
            '下边栏
            BtnFinishPort.Visibility = Visibility.Collapsed
            LabFinishPort.Visibility = Visibility.Collapsed
            LineFinishPort.Visibility = Visibility.Collapsed
            LabFinishIp.Visibility = Visibility.Visible
            LabFinishIp.Text = ClientAddress
            BtnFinishIp.Visibility = Visibility.Visible
        End If
        Update() '立即刷新
    End Sub

    '===============================
    '  页面 UI
    '===============================

    '退出
    Private Sub BtnFinishExit_Click() Handles BtnFinishExit.Click
        TryExit(False, False)
    End Sub
    ''' <summary>
    ''' 退出联机。
    ''' 返回是否弹出了警告窗口并且玩家选择了取消。
    ''' </summary>
    Public Function TryExit(Silent As Boolean, Closing As Boolean) As Boolean
        If LinkState = LinkStates.Waiting OrElse LinkState = LinkStates.Failed Then Return False
        If Not Silent Then
            If IsServerSide Then
                If MyMsgBox("你确定要关闭联机房间吗？" & vbCrLf & "所有玩家都需要重新输入房间码才可加入游戏！", "退出联机", "确定", "取消", IsWarn:=True) = 2 Then Return True
            ElseIf Closing Then
                If MyMsgBox(If(IsServerSide, "你确定要关闭联机房间吗？", "你确定要离开联机房间吗？"), "退出联机", "确定", "取消") = 2 Then Return True
            End If
        End If
        ChangeState(LinkStates.Waiting)
        Return False
    End Function

    '复制房间码
    Private Sub Copy() Handles BtnFinishCopy.Click
        Dim CodeText As String = $"在 PCL 启动器中输入房间码【{GetInviteCode()}】，即可加入联机房间！"
        ClipboardSet(CodeText, False)
        Settings.Set("LinkLastAutoJoinInviteCode", CodeText)
        Hint("已复制房间码！", HintType.Green)
    End Sub
    Private Function GetInviteCode() As String
        Return RoomCode
    End Function

    '复制 IP
    Private Sub BtnFinishIp_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles BtnFinishIp.MouseLeftButtonUp
        ClipboardSet(ClientAddress, False)
        Hint("已复制服务器地址！", HintType.Green)
    End Sub

#End Region

    '===============================
    '  后端（陶瓦联机 Terracotta）
    '===============================

    ''' <summary>陶瓦联机模块所在的文件夹路径，以 \ 结尾。</summary>
    Private ReadOnly PathTerracotta As String = Paths.AppDataThenName & "Terracotta\"
    Private Const TcExeName As String = "terracotta.exe"
    ''' <summary>等待陶瓦给出本地 API 端口的最长时间（秒）。</summary>
    Private Const TcStartupTimeout As Integer = 60
    ''' <summary>等待房间建立的最长时间（秒）。</summary>
    Private Const TcRoomTimeout As Integer = 180

    ''' <summary>当前是服务端还是客户端。</summary>
    Private IsServerSide As Boolean
    ''' <summary>客户端连接游戏时使用的地址，由陶瓦给出；服务端一直为 Nothing。</summary>
    Private ClientAddress As String = Nothing
    ''' <summary>陶瓦联机的房间码。</summary>
    Private RoomCode As String
    ''' <summary>网络名与网络密码，由房间码推导，仅用于日志。</summary>
    Private NetworkName As String, NetworkSecret As String
    ''' <summary>本启动器拉起的陶瓦进程（复用已有实例时为 Nothing）。</summary>
    Private Shared ProcessTc As Process
    ''' <summary>陶瓦本地 HTTP API 端口，未启动时为 0。</summary>
    Private Shared TcPort As Integer = 0
    ''' <summary>陶瓦进程是否由本启动器启动。</summary>
    Private Shared TcOwned As Boolean = False
    ''' <summary>最近一次从陶瓦读到的状态。</summary>
    Private Shared LastTcState As String = Nothing
    ''' <summary>连续无法与陶瓦通信的次数。</summary>
    Private Shared TcFailCount As Integer = 0
    ''' <summary>陶瓦输出的最近若干行日志。</summary>
    Private Shared ReadOnly LogHistory As New Queue(Of String)(20)

#Region "加载"

    Private WithEvents LinkLoader As New LoaderCombo(Of Integer)("联机", {
        New LoaderTask(Of Integer, Integer)("获取配置", AddressOf InitConfig) With {.ProgressWeight = 4},
        New LoaderTask(Of Integer, Integer)("准备陶瓦联机模块", AddressOf InitPrepare) With {.ProgressWeight = 6},
        New LoaderTask(Of Integer, Integer)("启动陶瓦联机模块", AddressOf InitLaunch) With {.ProgressWeight = 20},
        New LoaderTask(Of Integer, Integer)("进入联机房间", AddressOf InitEnterRoom) With {.ProgressWeight = 70}
    })

    '1. 获取服务器配置（仅用于判断联机功能是否被禁用，陶瓦联机不依赖服务器配置与节点下发）
    Private Sub InitConfig(Task As LoaderTask(Of Integer, Integer))
        UpdateLoadingPage("正在获取联机配置……", "获取联机配置")
        Try
            ServerLoader.WaitForExit(LoaderToSyncProgress:=Task)
        Catch ex As Exception
            Logger.Warn(ex, "获取联机配置失败，将直接使用陶瓦联机")
        End Try
        If ServerConfig Is Nothing OrElse ServerConfig("Link") Is Nothing Then
            Logger.Warn("没有可用的联机配置，将直接使用陶瓦联机")
            Return
        End If
        '检查是否已禁用联机功能
        Dim DisableReason = ServerConfig("Link")?("DisableReason2")?.ToString
        If Not String.IsNullOrEmpty(DisableReason) Then Throw New Exception("$" & DisableReason)
    End Sub

    '2. 准备陶瓦联机模块
    Private Sub InitPrepare(Task As LoaderTask(Of Integer, Integer))
        UpdateLoadingPage("正在准备陶瓦联机模块……", "准备联机模块")
        If Not System.IO.Directory.Exists(PathTerracotta) Then System.IO.Directory.CreateDirectory(PathTerracotta)
        Dim Target As String = PathTerracotta & TcExeName
        If Not FileUtils.Exists(Target) Then
            Dim Source As String = Nothing
            For Each Candidate As String In {Paths.Base & "Terracotta\" & TcExeName, Paths.Base & TcExeName}
                If FileUtils.Exists(Candidate) Then
                    Source = Candidate
                    Exit For
                End If
            Next
            If Source Is Nothing Then Throw New Exception($"$找不到陶瓦联机模块（{TcExeName}）。{vbCrLf}请重新安装启动器，或把 terracotta.exe 放到：{vbCrLf}{PathTerracotta}")
            FileUtils.Copy(Source, Target)
            Logger.Info($"已复制陶瓦联机模块：{Source} -> {Target}")
        End If
        '陶瓦依赖 VC++ 运行库，若随包附带则一并就位
        For Each DllName As String In {"VCRUNTIME140.dll", "MSVCP140.dll"}
            If Not FileUtils.Exists(PathTerracotta & DllName) Then
                For Each Candidate As String In {Paths.Base & "Terracotta\" & DllName, Paths.Base & DllName}
                    If FileUtils.Exists(Candidate) Then
                        FileUtils.Copy(Candidate, PathTerracotta & DllName)
                        Exit For
                    End If
                Next
            End If
        Next
        Task.Progress = 0.1
    End Sub

    '3. 启动陶瓦联机模块
    Private Sub InitLaunch(Task As LoaderTask(Of Integer, Integer))
        UpdateLoadingPage("正在启动陶瓦联机……", "启动联机模块")
        '结束上一次残留的实例
        ProcessStop()
        Dim PortFile As String = PathTerracotta & "pcl-port.json"
        Try
            If FileUtils.Exists(PortFile) Then FileUtils.Delete(PortFile)
        Catch ex As Exception
            Logger.Warn(ex, "清理陶瓦端口文件失败")
        End Try
        '--hmcl2 会让陶瓦把本地 API 端口写入指定文件
        Dim Info As New ProcessStartInfo With {
            .FileName = PathTerracotta & TcExeName,
            .Arguments = $"--hmcl2 ""{PortFile}""",
            .WorkingDirectory = PathTerracotta,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8
        }
        Logger.Info($"正在启动陶瓦联机：{Info.FileName} {Info.Arguments}")
        Try
            ProcessTc = Process.Start(Info)
        Catch ex As Exception
            Throw New Exception($"$无法启动陶瓦联机模块：{ex.Message}{vbCrLf}请检查安全软件是否拦截了 {TcExeName}。", ex)
        End Try
        If ProcessTc Is Nothing Then Throw New Exception("$无法启动陶瓦联机模块。")
        TcOwned = True
        AddHandler ProcessTc.OutputDataReceived, Sub(sender, e) TcLogLine(e.Data)
        AddHandler ProcessTc.ErrorDataReceived, Sub(sender, e) TcLogLine(e.Data)
        ProcessTc.BeginOutputReadLine()
        ProcessTc.BeginErrorReadLine()
        '等待端口文件出现
        Dim Start As Long = GetTimeMs()
        Do
            If Task.IsCanceled Then Throw New OperationCanceledException
            If FileUtils.Exists(PortFile) Then
                Try
                    Dim Text As String = System.IO.File.ReadAllText(PortFile).Trim()
                    Dim Port As Integer = 0
                    If Not Integer.TryParse(Text, Port) Then
                        Dim Json = Text.DeserializeJson()
                        If TypeOf Json Is JObject Then Integer.TryParse(CStr(CType(Json, JObject)("port")), Port)
                    End If
                    If Port > 0 AndAlso Port <= 65535 Then
                        TcPort = Port
                        Exit Do
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, "读取陶瓦端口文件失败")
                End Try
            End If
            If ProcessTc.HasExited Then Throw New Exception("$陶瓦联机模块启动后意外退出。" & vbCrLf & "近期日志：" & vbCrLf & String.Join(vbCrLf, LogHistory.ToArray()))
            If GetTimeMs() - Start > TcStartupTimeout * 1000 Then Throw New Exception($"$等待陶瓦联机模块启动超时（{TcStartupTimeout} 秒）。请检查是否有安全软件阻止了 {TcExeName}。")
            Task.Progress = 0.1 + Math.Min((GetTimeMs() - Start) / (TcStartupTimeout * 1000.0), 1) * 0.1
            Thread.Sleep(150)
        Loop
        Logger.Info($"陶瓦本地接口端口：{TcPort}")
        '等待接口就绪
        Dim ApiStart As Long = GetTimeMs()
        Do
            If Task.IsCanceled Then Throw New OperationCanceledException
            If TcGetStateJson(800) IsNot Nothing Then Exit Do
            If GetTimeMs() - ApiStart > 15000 Then Throw New Exception("$陶瓦联机模块的本地接口没有响应，请重试或检查安全软件设置。")
            Thread.Sleep(200)
        Loop
        Task.Progress = 0.25
    End Sub

    '4. 创建或加入房间：只依赖房间码，游戏端口由陶瓦自动扫描
    Private Sub InitEnterRoom(Task As LoaderTask(Of Integer, Integer))
        Dim Start As Long = GetTimeMs()
        Dim LastRequest As Long = 0
        Dim FailCount As Integer = 0
        Dim Succeeded As Boolean = False
        Do
            If Task.IsCanceled Then Throw New OperationCanceledException
            '陶瓦只在 Waiting 状态接受请求，重复下发等价，可自动续上被重置的状态
            If GetTimeMs() - LastRequest > 1000 Then
                LastRequest = GetTimeMs()
                If IsServerSide Then
                    TcApi($"/state/scanning?player={Uri.EscapeDataString(TcPlayerName())}", 1500)
                Else
                    TcApi($"/state/guesting?room={Uri.EscapeDataString(RoomCode)}&player={Uri.EscapeDataString(TcPlayerName())}", 1500)
                End If
            End If
            '读取状态
            Dim Json As JObject = TcGetStateJson(1500)
            If Json Is Nothing Then
                FailCount += 1
                If FailCount >= 25 Then Throw New Exception("$无法与陶瓦联机模块通信，模块可能已被结束。" & vbCrLf & "近期日志：" & vbCrLf & String.Join(vbCrLf, LogHistory.ToArray()))
                Thread.Sleep(250)
                Continue Do
            End If
            FailCount = 0
            Dim State As String = If(Json("state")?.ToString, "")
            LastTcState = State
            Select Case State
                Case "host-scanning", "waiting"
                    If IsServerSide Then
                        Dim RoomInScan As String = If(Json("room")?.ToString, "")
                        If Not String.IsNullOrEmpty(RoomInScan) Then RoomCode = RoomInScan
                        If String.IsNullOrEmpty(RoomCode) Then
                            UpdateLoadingPage("请在游戏内选择「对局域网开放」，陶瓦正在自动扫描游戏端口……", "扫描游戏端口")
                        Else
                            UpdateLoadingPage($"请在游戏内选择「对局域网开放」，陶瓦正在自动扫描游戏端口…… 房间码：{RoomCode}", "扫描游戏端口")
                        End If
                    Else
                        UpdateLoadingPage("正在等待房主响应……", "连接房主")
                    End If
                Case "host-starting"
                    UpdateLoadingPage("正在创建陶瓦联机房间……", "创建联机房间")
                Case "guest-connecting"
                    UpdateLoadingPage("正在连接陶瓦公共节点……", "连接联机节点")
                Case "guest-starting"
                    UpdateLoadingPage("正在与房主建立点对点连接……", "连接房主")
                Case "host-ok"
                    RoomCode = If(Json("room")?.ToString, RoomCode)
                    If String.IsNullOrEmpty(RoomCode) Then Continue Do
                    Succeeded = True
                Case "guest-ok"
                    ClientAddress = If(Json("url")?.ToString, "")
                    If String.IsNullOrEmpty(ClientAddress) Then ClientAddress = "127.0.0.1"
                    Succeeded = True
                Case "exception"
                    Throw New Exception("$" & TcExceptionText(Json))
            End Select
            If Succeeded Then Exit Do
            '超时判定
            Dim Elapsed As Double = (GetTimeMs() - Start) / 1000.0
            If Elapsed > TcRoomTimeout Then
                If IsServerSide Then
                    Throw New Exception($"$等待游戏端口超时。{vbCrLf}请先在游戏内选择「对局域网开放」，然后点击重试。")
                Else
                    Throw New Exception($"$连接房主超时。可能的原因：{vbCrLf}- 房间码输入有误{vbCrLf}- 房主已关闭房间{vbCrLf}- 你或房主的网络环境不佳")
                End If
            End If
            Task.Progress = 0.25 + Math.Min(Elapsed / 20, 1) * 0.7
            Thread.Sleep(300)
        Loop
        Logger.Info($"陶瓦联机就绪：房间码 {RoomCode}，连接地址 {If(ClientAddress, "（房主）")}")
        Telemetry("联机成功", "Server", IsServerSide)
        Task.Progress = 1
    End Sub

    ''' <summary>把陶瓦给出的异常类型翻译成提示文本。</summary>
    Private Shared Function TcExceptionText(Json As JObject) As String
        Dim Kind As Integer = -1
        Integer.TryParse(If(Json("type")?.ToString, ""), Kind)
        Select Case Kind
            Case 0
                Return "无法连接到房主的游戏，房主可能没有在游戏内「对局域网开放」。"
            Case 1
                Return "房主拒绝了连接，请让房主检查游戏内的「对局域网开放」。"
            Case 2
                Return "本机的联机组网组件崩溃了，请点击重试。"
            Case 3
                Return "房主的联机组网组件崩溃了，请让房主重新创建房间。"
            Case 4
                Return "与游戏服务器的连接被重置，请确认游戏仍在运行。"
            Case 5
                Return "陶瓦联机握手失败，请让房主重新创建房间。"
            Case Else
                Return "陶瓦联机出现未知错误，请点击重试。"
        End Select
    End Function

    ''' <summary>传给陶瓦的玩家名，仅用于在房间成员中区分</summary>
    Private Function TcPlayerName() As String
        Return If(IsServerSide, "PCL-Host-", "PCL-Guest-") & Identify.GetStableHashCode().ConvertRadix(36).TrimStart("-"c).EnsureLength("0", 6)
    End Function

#End Region

#Region "接口与进程"

    ''' <summary>调用陶瓦本地 HTTP 接口，失败返回 Nothing</summary>
    Private Shared Function TcApi(Path As String, Optional TimeoutMs As Integer = 1500) As String
        If TcPort <= 0 Then Return Nothing
        Try
            Dim Request As HttpWebRequest = CType(WebRequest.Create($"http://127.0.0.1:{TcPort}{Path}"), HttpWebRequest)
            Request.Method = "GET"
            Request.Timeout = TimeoutMs
            Request.ReadWriteTimeout = TimeoutMs
            Request.Proxy = Nothing
            Request.KeepAlive = False
            Using Response As HttpWebResponse = CType(Request.GetResponse(), HttpWebResponse)
                Using Reader As New System.IO.StreamReader(Response.GetResponseStream(), Encoding.UTF8)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch ex As WebException
            '400 等错误也会带回响应体
            Try
                If ex.Response IsNot Nothing Then
                    Using Reader As New System.IO.StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8)
                        Return Reader.ReadToEnd()
                    End Using
                End If
            Catch
            End Try
            Return Nothing
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    ''' <summary>获取陶瓦状态对象，失败返回 Nothing</summary>
    Private Shared Function TcGetStateJson(Optional TimeoutMs As Integer = 1500) As JObject
        Dim RawText As String = TcApi("/state", TimeoutMs)
        If String.IsNullOrEmpty(RawText) Then Return Nothing
        Try
            Dim Json = RawText.DeserializeJson()
            If TypeOf Json Is JObject Then Return CType(Json, JObject)
        Catch ex As Exception
            Logger.Warn(ex, "解析陶瓦状态失败：" & RawText)
        End Try
        Return Nothing
    End Function

    Private Shared Sub TcLogLine(Line As String)
        If String.IsNullOrWhiteSpace(Line) Then Return
        Logger.Info("[Terracotta] " & Line)
        SyncLock LogHistory
            LogHistory.Enqueue(Line)
            Do While LogHistory.Count > 20
                LogHistory.Dequeue()
            Loop
        End SyncLock
    End Sub

    ''' <summary>
    ''' 结束联机：退出房间，并在需要时结束本启动器拉起的陶瓦进程。
    ''' 即使失败也不会抛出异常。
    ''' </summary>
    Public Shared Sub ProcessStop()
        Try
            If TcPort > 0 Then
                '回到初始状态，结束当前房间
                TcApi("/state/ide", 800)
                If TcOwned Then
                    '仅结束自己拉起的实例，peaceful 让它自行清理虚拟网卡
                    TcApi("/panic?peaceful=true", 800)
                    Thread.Sleep(250)
                End If
            End If
        Catch ex As Exception
            Logger.Warn(ex, "退出陶瓦联机房间失败")
        End Try
        Try
            If ProcessTc IsNot Nothing AndAlso Not ProcessTc.HasExited Then
                Logger.Info($"停止所启动的陶瓦联机，PID：{ProcessTc.Id}")
                ProcessTc.Kill()
                ProcessTc.WaitForExit(2000)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "结束陶瓦联机进程失败")
        End Try
        Try
            If ProcessTc IsNot Nothing Then ProcessTc.Dispose()
        Catch
        End Try
        ProcessTc = Nothing
        TcPort = 0
        TcOwned = False
        LastTcState = Nothing
        TcFailCount = 0
    End Sub

    ''' <summary>
    ''' 出现意外错误，给出错误信息并结束联机。
    ''' </summary>
    Private Sub Panic(Brief As String, Detail As String)
        If LinkState = LinkStates.Loading Then
            FailReason = Brief
            LinkLoader.Failed(New Exception(Detail))
        Else
            MyMsgBox(Detail, "联机出错：" & Brief, IsWarn:=True)
            ChangeState(LinkStates.Waiting)
            Telemetry("联机失败", "Exception", FilterUserName(Brief & "：" & Detail, "*"))
        End If
    End Sub

#End Region

#Region "定时任务"

    '启动
    Private Sub StartTimerThread() Handles Me.Loaded
        Static IsTimerStarted As Boolean = False
        If IsTimerStarted Then Return
        RunInNewThread(
        Sub()
            Dim Counter As Integer = 0
            Do While True
                Try
                    Thread.Sleep(200)
                    Counter += 200
                    '每秒执行一次
                    If Counter >= 1000 Then
                        Counter = 0
                        Update()
                    End If
                    '每 200ms 更新进度条
                    If LinkState = LinkStates.Loading Then RunInUi(AddressOf UpdateProgressBar)
                Catch ex As Exception
                    Logger.Error(ex, "联机模块主时钟出错")
                    Thread.Sleep(10000)
                End Try
            Loop
        End Sub, "Link Timer")
    End Sub

    '每秒触发
    Private Sub Update() Handles Me.Loaded
        If LinkState <> LinkStates.Finished Then Return
        '界面刷新
        If FrmMain.PageCurrent = FormMain.PageType.Link Then
            RunInUi(
            Sub()
                AniStart(AaRotateTransform(ImgFinishLogo, 500, 5000), "Link Logo Rotation")
                LabFinishPing.Text = "已连接"
                LabFinishPlayer.Text = "陶瓦联机"
            End Sub)
        End If
        '读取陶瓦状态：房间被结束后自动回到初始页面
        Dim Json As JObject = TcGetStateJson(1000)
        If Json Is Nothing Then
            TcFailCount += 1
            If TcFailCount >= 5 Then
                '连续 5 秒读不到状态，说明陶瓦已经不在运行
                TcFailCount = 0
                CheckCrash()
            End If
            Return
        End If
        TcFailCount = 0
        Dim State As String = If(Json("state")?.ToString, "")
        Select Case State
            Case "waiting"
                Logger.Info("陶瓦联机房间已结束，回到初始页面")
                ChangeState(LinkStates.Waiting)
            Case "exception"
                Panic("联机出错", TcExceptionText(Json))
            Case Else
                If State <> LastTcState Then Logger.Info($"陶瓦状态：{LastTcState} -> {State}")
                LastTcState = State
        End Select
    End Sub

    ''' <summary>检查联机模块是否崩溃。</summary>
    Private Sub CheckCrash()
        If LinkState <> LinkStates.Finished Then Return
        If TcPort > 0 AndAlso TcGetStateJson(800) IsNot Nothing Then Return
        Panic("联机已断开", "无法与陶瓦联机模块通信，模块可能已被结束。" & vbCrLf & "近期日志：" & vbCrLf & String.Join(vbCrLf, LogHistory.ToArray()))
    End Sub

#End Region

End Class

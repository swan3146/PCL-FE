Imports System.Collections.ObjectModel
Imports System.Globalization
Imports System.IO
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
    ''' <summary>把 128 位房间数值格式化为 (房间码, 网络名, 网络密码)。首字符为最高位，与陶瓦 / HMCL 一致。</summary>
    Private Shared Function TcFormat(Value As BigInteger) As Tuple(Of String, String, String)
        Return TcFormatCore(Value, False)
    End Function
    ''' <summary>1.2.8～1.3.0 生成的房间码是低位在前（解析方向错误），沿用旧排列才能还原它当时的网络名。</summary>
    Private Shared Function TcFormatLegacy(Value As BigInteger) As Tuple(Of String, String, String)
        Return TcFormatCore(Value, True)
    End Function
    Private Shared Function TcFormatCore(Value As BigInteger, LittleEndian As Boolean) As Tuple(Of String, String, String)
        Dim Chars(15) As Char
        If LittleEndian Then
            For i = 0 To 15
                Chars(i) = TC_CHARS(CInt(Value Mod 34))
                Value = BigInteger.Divide(Value, 34)
            Next
        Else
            For i = 15 To 0 Step -1
                Chars(i) = TC_CHARS(CInt(Value Mod 34))
                Value = BigInteger.Divide(Value, 34)
            Next
        End If
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
        '先把每个字符转成 34 进制数值
        Dim Digits(15) As Integer
        For i = 0 To 15
            Dim Ch As Char = Char.ToUpperInvariant(Body(i))
            If Ch = "I"c Then Ch = "1"c
            If Ch = "O"c Then Ch = "0"c
            Dim Pos As Integer = TC_CHARS.IndexOf(Ch)
            If Pos < 0 Then Return Nothing
            Digits(i) = Pos
        Next
        '标准排列：首字符是最高位（陶瓦与 HMCL 发给玩家的房间码都是这种）
        Dim Value As BigInteger = BigInteger.Zero
        For i = 0 To 15
            Value = Value * 34 + Digits(i)
        Next
        If Value Mod 7 = 0 Then Return TcFormat(Value)
        '兼容旧版 PCL  FE 低位在前的房间码（房主升级前创建的房间仍能被新版本加入）
        Value = BigInteger.Zero
        Dim Power As BigInteger = BigInteger.One
        For i = 0 To 15
            Value += Digits(i) * Power
            Power *= 34
        Next
        If Value Mod 7 = 0 Then
            Logger.Info("检测到旧版 PCL  FE 生成的房间码（低位在前），已按旧格式解析")
            Return TcFormatLegacy(Value)
        End If
        Return Nothing
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
        HostGuideToken += 1
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
        '与 HMCL 一致：先确认游戏已经在运行
        If Not TcGameRunning() Then
            If MyMsgBox("还没有检测到正在运行的游戏。" & vbCrLf &
                        "请先进入存档并在游戏里选择「对局域网开放」，聊天栏会显示本地游戏端口。" & vbCrLf &
                        "记下端口后再创建房间，朋友才能连进来。" & vbCrLf & vbCrLf &
                        "是否仍然创建房间？", "创建联机房间", "仍然创建", "取消") = 2 Then Return
        End If
        '精确端口：陶瓦本身不提供端口参数，改为用户填写后由启动器广播给陶瓦
        Dim PortInput As String = MyMsgBoxInput("输入游戏端口",
            "请填写游戏聊天栏「本地游戏已在端口 XXXXX 上开放」里的端口号。",
            DefaultInput:=If(HostPort > 0, HostPort.ToString(), "25565"),
            ValidateRules:=New Collection(Of Validate) From {New ValidateInteger(1, 65535)})
        If PortInput Is Nothing Then Return
        Dim NewPort As Integer
        If Not Integer.TryParse(PortInput.Trim(), NewPort) OrElse NewPort <= 0 OrElse NewPort > 65535 Then Return
        Dim PortAlive As Boolean = TcGamePortAlive(NewPort)
        If Not PortAlive Then
            If MyMsgBox($"端口 {NewPort} 现在没有在监听。" & vbCrLf &
                        "请确认游戏里已经选择了「对局域网开放」，并且端口没有填错。" & vbCrLf & vbCrLf &
                        "是否仍然用这个端口创建房间？", "端口未在监听", "仍然创建", "取消", IsWarn:=True) = 2 Then Return
        End If
        ManualPort = NewPort
        Logger.Info($"手动指定游戏端口 {NewPort}（当前监听：{PortAlive}）")
        '基础信息（房间码使用陶瓦体系，游戏端口由上面的输入决定）
        IsServerSide = True
        '记录创建房间时选中的公共节点
        Dim NodeItem = TryCast(ComboCreateNode.SelectedItem, MyComboBoxItem)
        If NodeItem IsNot Nothing Then TcSelectedNode = TryCast(NodeItem.Tag, String)
        Dim Room = TcRandomRoom()
        RoomCode = Room.Item1
        NetworkName = Room.Item2
        NetworkSecret = Room.Item3
        Logger.Info($"尝试创建陶瓦房间，房间码 {RoomCode}，网络名 {NetworkName}")
        '联机前检查防火墙是否放行了陶瓦与 EasyTier
        ModFirewall.CheckTerracottaFirewall()
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
        '联机前检查防火墙是否放行了陶瓦与 EasyTier
        ModFirewall.CheckTerracottaFirewall()
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
        Code = TcNormalizeWidth(Code)
        Code = Code.Replace(" ", "").Replace(vbCrLf, "").Replace(vbCr, "")
        Dim Index As Integer = Code.IndexOf("U/", StringComparison.Ordinal)
        If Index > 0 Then Code = Code.Substring(Index)
        Return Code
    End Function
    ''' <summary>
    ''' 把全角字符归一化为半角。中文输入法手打房间码时容易出全角的「Ｕ／」、全角连字符，
    ''' 这些字符会让房间码解析直接失败，表现为「房间码有误」。
    ''' </summary>
    Private Shared Function TcNormalizeWidth(Text As String) As String
        If String.IsNullOrEmpty(Text) Then Return Text
        Dim Buffer As New Text.StringBuilder(Text.Length)
        For Each C As Char In Text
            Dim CharCode As Integer = AscW(C)
            Select Case CharCode
                Case &HFF01 To &HFF5E '全角 ASCII 区（含 － ／ 数字与字母）
                    Buffer.Append(ChrW(CharCode - &HFEE0))
                Case &H3000 '全角空格
                    Buffer.Append(" "c)
                Case &H2010, &H2011, &H2013, &H2014, &H2212 '各种连字符、短横线
                    Buffer.Append("-"c)
                Case Else
                    Buffer.Append(C)
            End Select
        Next
        Return Buffer.ToString()
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
        HostGuideToken += 1
        HostPort = 0
        HostNodeText = ""
        ClientAddress = Nothing
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
        HostGuideToken += 1
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
            HostNodeText = TcNodeText()
            RefreshHostGuide()
            StartHostGuideWatch()
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
        Dim CodeText As String = $"在 Plain Craft Launcher Fork Enhanced 启动器中输入房间码【{GetInviteCode()}】，即可加入联机房间！"
        ClipboardSet(CodeText, False)
        Settings.Set("LinkLastAutoJoinInviteCode", CodeText)
        Hint("已复制房间码！", HintType.Green)
    End Sub
    Private Function GetInviteCode() As String
        Return RoomCode
    End Function

    ''' <summary>
    ''' 刷新房主完成页的提示。陶瓦在游戏还没「对局域网开放」时也会给出 host-ok（端口是猜的），
    ''' 所以这里要实测一次本地端口，没开就明确提醒房主，避免把房间码发出去却谁都进不来。
    ''' </summary>
    Private Sub RefreshHostGuide()
        If FrmLinkMain Is Nothing Then Return
        Dim Alive As Boolean = HostPort > 0 AndAlso TcGamePortAlive(HostPort)
        Dim Text As String = $"把房间码发给朋友，让大家加入房间吧！{vbCrLf}房间码：{GetInviteCode()}{vbCrLf}节点：{HostNodeText}"
        If ManualPort > 0 Then Text &= vbCrLf & $"游戏端口：{ManualPort}（手动指定）"
        If HostPort > 0 AndAlso Not Alive Then
            Text &= vbCrLf & vbCrLf &
                    "注意：还没有检测到游戏端口。请现在进入游戏并选择「对局域网开放」，否则朋友会一直卡在「连接房主」。"
        End If
        If Not TcHasReachableNode Then
            Text &= vbCrLf & vbCrLf &
                    "注意：当前没有可用的联机节点，朋友很可能连不进来。请检查网络或代理后重新创建房间。"
        End If
        LabFinishDesc.Text = Text
    End Sub

    ''' <summary>
    ''' 轮询本地游戏端口，房主开放局域网后自动去掉提示。
    ''' 注意：MC 每次「对局域网开放」都会换随机端口，所以不能只用建房时读到的那一次，
    ''' 必须持续从陶瓦的 /state 读 port 字段，端口一变就跟着变。
    ''' </summary>
    Private Sub StartHostGuideWatch()
        '代际 +1：上一次建房留下的观察线程会在下一次循环时自动退出
        HostGuideToken += 1
        Dim MyToken As Integer = HostGuideToken
        '立刻广播一次，陶瓦只需 5 秒内的广播，晚了会先猜一个端口
        If ManualPort > 0 Then TcBroadcastPort(ManualPort)
        RunInNewThread(
        Sub()
            For i = 1 To 600 '每 3 秒一次，最多观察 30 分钟
                Thread.Sleep(3000)
                If MyToken <> HostGuideToken OrElse LinkState <> LinkStates.Finished Then Exit For
                '每轮重申端口：陶瓦只接受 5 秒内的广播，需要持续喂给它
                If ManualPort > 0 Then TcBroadcastPort(ManualPort)
                '跟随陶瓦上报的端口（扫描到新端口时它会自己重建 EasyTier）
                Dim Json As JObject = TcGetStateJson(1500)
                Dim Port As Integer = 0
                If Json IsNot Nothing Then Integer.TryParse(If(Json("port")?.ToString, ""), Port)
                If Port <= 0 Then Port = HostPort
                Dim PortChanged As Boolean = Port <> HostPort
                If PortChanged Then HostPort = Port
                '端口变了才刷新提示：已开放时去掉提醒，游戏退出或换端口后又没监听时重新提醒
                If PortChanged Then
                    RunInUi(Sub()
                                If MyToken <> HostGuideToken Then Return
                                RefreshHostGuide()
                            End Sub)
                End If
            Next
        End Sub, "陶瓦房主端口检测")
    End Sub

    ''' <summary>检查本地游戏端口是否真的在监听（游戏开了「对局域网开放」才会监听）。</summary>
    Private Shared Function TcGamePortAlive(Port As Integer) As Boolean
        If Port <= 0 Then Return False
        Try
            Using Client As New TcpClient()
                Dim ConnectTask = Client.ConnectAsync("127.0.0.1", Port)
                Return ConnectTask.Wait(700) AndAlso Client.Connected
            End Using
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 把用户填写的游戏端口直接告诉陶瓦。陶瓦没有端口参数，只监听 UDP 4445 上的 MC 局域网广播，
    ''' 所以这里伪造一条 [MOTD]…[/MOTD][AD]端口[/AD] 广播发到本机各网卡（陶瓦绑定在这些网卡的 4445）。
    ''' </summary>
    Private Shared Sub TcBroadcastPort(Port As Integer)
        If Port <= 0 OrElse Port > 65535 Then Return
        Try
            Dim Payload As Byte() = System.Text.Encoding.UTF8.GetBytes($"[MOTD]PCL FE 精确端口[/MOTD][AD]{Port}[/AD]")
            Using Udp As New UdpClient()
                Udp.EnableBroadcast = True
                For Each Ip As System.Net.IPAddress In System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName())
                    If Ip.AddressFamily <> AddressFamily.InterNetwork OrElse System.Net.IPAddress.IsLoopback(Ip) Then Continue For
                    If Ip.ToString().StartsWithF("10.144.144.") Then Continue For '陶瓦自己的 EasyTier 网段会被排除
                    Try
                        Udp.Send(Payload, Payload.Length, New System.Net.IPEndPoint(Ip, 4445))
                    Catch
                    End Try
                Next
                Try
                    Udp.Send(Payload, Payload.Length, New System.Net.IPEndPoint(System.Net.IPAddress.Parse("224.0.2.60"), 4445))
                Catch
                End Try
            End Using
        Catch ex As Exception
            Logger.Warn(ex, $"向陶瓦广播游戏端口失败（{Port}）")
        End Try
    End Sub


    ''' <summary>最近一次由启动器启动的游戏是否还在运行（建房前的检查，与 HMCL 的 countManagedProcesses 对应）。</summary>
    Private Shared Function TcGameRunning() As Boolean
        Try
            Dim Game As Process = McLaunchProcess
            If Game Is Nothing Then Return False
            Return Not Game.HasExited
        Catch
            Return False
        End Try
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
    ''' <summary>房主房间使用的游戏端口，由陶瓦上报；未上报时为 0。</summary>
    Private HostPort As Integer = 0
    ''' <summary>建房时用户手动指定的游戏端口，0 表示未指定。</summary>
    Private ManualPort As Integer = 0
    ''' <summary>完成页展示的节点文本（提前取好，避免后台刷新时重复请求网络）。</summary>
    Private HostNodeText As String = ""
    ''' <summary>完成页「等待游戏开放」检测的停止标记。</summary>
    ''' <summary>
    ''' 房主端口观察线程的代际标记。每次开始/停止观察都自增，旧线程发现标记变化后立即退出，
    ''' 避免「快速退出→再建房」时上一轮轮询线程继续跑并改写新房间的端口提示。
    ''' </summary>
    Private Shared HostGuideToken As Integer = 0
    ''' <summary>陶瓦联机的房间码。</summary>
    Private RoomCode As String
    ''' <summary>网络名与网络密码，由房间码推导，仅用于日志。</summary>
    Private NetworkName As String, NetworkSecret As String
    ''' <summary>本启动器拉起的陶瓦进程（复用已有实例时为 Nothing）。</summary>
    Private Shared ProcessTc As Process
    ''' <summary>本次启动使用的唯一端口文件所在目录，退出时清理。</summary>
    Private Shared TcPortDir As String
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

    ''' <summary>
    ''' 从 exe 内嵌的 Terracotta_Module.zip 释放陶瓦联机模块，没有网络也能用。
    ''' </summary>
    Private Shared Function ExtractEmbeddedTerracotta(TargetFolder As String) As Boolean
        Try
            Dim Asm As Reflection.Assembly = Reflection.Assembly.GetExecutingAssembly()
            Dim ResName As String = Nothing
            For Each Name As String In Asm.GetManifestResourceNames()
                If Name.EndsWith("Terracotta_Module.zip", StringComparison.OrdinalIgnoreCase) Then
                    ResName = Name
                    Exit For
                End If
            Next
            If ResName Is Nothing Then Return False
            IO.Directory.CreateDirectory(TargetFolder)
            Using Stream As IO.Stream = Asm.GetManifestResourceStream(ResName)
                Using Zip As New IO.Compression.ZipArchive(Stream, IO.Compression.ZipArchiveMode.Read)
                    For Each Entry As IO.Compression.ZipArchiveEntry In Zip.Entries
                        Dim Dest As String = IO.Path.Combine(TargetFolder, Entry.FullName)
                        IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(Dest))
                        Using EntryStream As IO.Stream = Entry.Open()
                            Using Out As IO.FileStream = IO.File.Create(Dest)
                                EntryStream.CopyTo(Out)
                            End Using
                        End Using
                    Next
                End Using
            End Using
            Logger.Info($"已从内嵌资源释放陶瓦联机模块：{TargetFolder}")
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "释放内嵌陶瓦联机模块失败")
            Return False
        End Try
    End Function

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
            If Source Is Nothing Then
                '随包未附带时，优先从 exe 内嵌的压缩包释放，保证没网也能用
                UpdateLoadingPage("正在准备陶瓦联机模块……", "解压联机模块")
                If ExtractEmbeddedTerracotta(PathTerracotta) Then
                    Source = Target
                End If
            End If
            If Source Is Nothing Then
                '内嵌资源缺失时（例如从很旧的版本升级上来），再从 GitHub Release 下载
                UpdateLoadingPage("正在下载陶瓦联机模块……", "下载联机模块")
                If DownloadTerracottaModule(Paths.Base & "Terracotta\") Then
                    Source = Paths.Base & "Terracotta\" & TcExeName
                Else
                    Throw New Exception($"$找不到陶瓦联机模块（{TcExeName}），自动下载也失败了。{vbCrLf}请检查网络后重试，或手动把 terracotta.exe 放到：{vbCrLf}{PathTerracotta}")
                End If
            End If
            If Not FileUtils.Exists(Target) Then
                FileUtils.Copy(Source, Target)
                Logger.Info($"已复制陶瓦联机模块：{Source} -> {Target}")
            End If
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
        '结束残留实例（含被其他启动器遗留的进程），避免其抢占 UDP 4445 局域网广播端口
        TcKillStaleProcesses()
        '结束上一次残留的实例
        ProcessStop()
        '端口文件放在每次启动唯一的临时目录，避免多开或残留实例互相覆盖（与 HMCL 的做法一致）
        TcPortDir = IO.Path.Combine(IO.Path.GetTempPath(), "PCLPro-Terracotta-" & Guid.NewGuid().ToString("N"))
        If Not IO.Directory.Exists(TcPortDir) Then IO.Directory.CreateDirectory(TcPortDir)
        Dim PortFile As String = IO.Path.Combine(TcPortDir, "http")
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
        '加入房间必须有一个真的能连上的公共节点：陶瓦内置节点早已失效，没有节点时客人只会一直 PingHostFail
        If Not IsServerSide AndAlso Not TcEnsureNodesUsable() Then
            Throw New Exception("$找不到可用的陶瓦联机节点，无法加入房间。" & vbCrLf &
                                "当前网络下陶瓦的公共节点都连不上（它内置的节点已经失效）。" & vbCrLf &
                                "请检查网络、代理或防火墙后点击重试；如果仍然不行，请让房主重新创建房间。")
        End If
        Dim Start As Long = GetTimeMs()
        Dim LastRequest As Long = 0
        Dim FailCount As Integer = 0
        Dim EnterFailCount As Integer = 0
        Dim RecoverCount As Integer = 0
        Dim Succeeded As Boolean = False
        Dim CurState As String = ""
        Dim LastStateIndex As Integer = -1
        Do
            If Task.IsCanceled Then Throw New OperationCanceledException
            '陶瓦只在 Waiting 状态接受状态切换请求，否则会返回 400 或打断当前流程
            If (CurState = "" OrElse CurState = "waiting") AndAlso GetTimeMs() - LastRequest > 1000 Then
                LastRequest = GetTimeMs()
                Dim EnterBody As String = Nothing
                Dim EnterStatus As Integer
                If IsServerSide Then
                    EnterStatus = TcApiStatus($"/state/scanning?player={Uri.EscapeDataString(TcPlayerName())}{TcPublicNodesQuery()}", EnterBody)
                Else
                    EnterStatus = TcApiStatus($"/state/guesting?room={Uri.EscapeDataString(RoomCode)}&player={Uri.EscapeDataString(TcPlayerName())}{TcPublicNodesQuery()}", EnterBody, 4000)
                End If
                '拿到明确的拒绝原因就暴露出来，而不是静默重试到超时
                If EnterStatus >= 400 Then
                    EnterFailCount += 1
                    Logger.Warn($"陶瓦拒绝联机请求（HTTP {EnterStatus}，第 {EnterFailCount} 次）：{EnterBody}")
                    '400 在房主尚未开放局域网时非常常见，不应立刻判死：复位到 Waiting 后带退避重试
                    If EnterFailCount >= 8 Then
                        Throw New Exception("$" & $"陶瓦联机模块拒绝了{If(IsServerSide, "创建房间", "加入房间")}请求（HTTP {EnterStatus}）。{vbCrLf}服务端回应：{If(EnterBody, "（无）")}{vbCrLf}请点击重试；如果反复失败，请让房主重新创建房间后把新的房间码发给你。")
                    End If
                    TcApi("/state/ide", 1200)
                    LastStateIndex = -1 '复位后陶瓦会重新计数，不能再拿旧 index 挡新状态
                    Thread.Sleep(600 + EnterFailCount * 250)
                ElseIf EnterStatus > 0 Then
                    EnterFailCount = 0
                    Logger.Info($"陶瓦已接受联机请求（HTTP {EnterStatus}）：{EnterBody}")
                Else
                    '本地接口无响应（-1）：不累加拒绝计数，交给状态轮询的 FailCount 处理
                    Logger.Warn($"调用陶瓦联机接口无响应：{If(IsServerSide, "/state/scanning", "/state/guesting")}")
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
            '与 HMCL 一致：只处理 index 递增的状态，避免读到滞后响应把状态倒退回去
            '成功状态不受此限制，否则重复读到同一个 host-ok 会被误判为没有进展
            Dim StateIndex As Integer = -1
            Integer.TryParse(If(Json("index")?.ToString, ""), StateIndex)
            If StateIndex >= 0 AndAlso StateIndex <= LastStateIndex AndAlso State <> "host-ok" AndAlso State <> "guest-ok" Then
                Thread.Sleep(300)
                Continue Do
            End If
            If StateIndex >= 0 Then LastStateIndex = StateIndex
            CurState = State
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
                    UpdateLoadingPage($"正在创建陶瓦联机房间……{vbCrLf}节点：{TcNodeText()}", "创建联机房间")
                Case "guest-connecting"
                    UpdateLoadingPage("正在连接陶瓦公共节点……", "连接联机节点")
                Case "guest-starting"
                    '与 HMCL 一致：陶瓦会给出网络穿透难度（仅估计），难度高时先给用户一个预期
                    Dim Difficulty As String = If(Json("difficulty")?.ToString, "").ToUpperInvariant
                    Dim GuestText As String = "正在与房主建立点对点连接……"
                    Select Case Difficulty
                        Case "EASIEST", "SIMPLE"
                            GuestText &= vbCrLf & "网络穿透难度：较低，通常很快就能连上。"
                        Case "MEDIUM", "TOUGH"
                            GuestText &= vbCrLf & "网络穿透难度：较高，可能需要多等一会儿；若长时间连不上，请让房主换个节点重新创建房间。"
                    End Select
                    UpdateLoadingPage(GuestText, "连接房主")
                Case "host-ok"
                    RoomCode = If(Json("room")?.ToString, RoomCode)
                    If String.IsNullOrEmpty(RoomCode) Then Continue Do
                    Integer.TryParse(If(Json("port")?.ToString, ""), HostPort)
                    Succeeded = True
                Case "guest-ok"
                    ClientAddress = If(Json("url")?.ToString, "")
                    If String.IsNullOrEmpty(ClientAddress) Then ClientAddress = "127.0.0.1"
                    Succeeded = True
                Case "exception"
                    Dim Kind As Integer = -1
                    Integer.TryParse(If(Json("type")?.ToString, ""), Kind)
                    'PingHostFail（type 0）每次要等陶瓦自己判定约 14 秒，重试太多次只会拖到总超时
                    If Not IsServerSide AndAlso (Kind = 0 OrElse Kind = 1 OrElse Kind = 5) AndAlso RecoverCount < 10 Then
                        RecoverCount += 1
                        Logger.Info($"陶瓦返回异常 {Kind}，复位后重试第 {RecoverCount} 次")
                        '按异常类型给不同文案：房间码错误时不要再让用户以为只是「房主没开放端口」
                        Dim RetryText As String
                        Select Case Kind
                            Case 1
                                RetryText = $"房主拒绝了连接，正在重试……（第 {RecoverCount} 次尝试）{vbCrLf}请让房主确认游戏内已选择「对局域网开放」。"
                            Case 5
                                RetryText = $"与房主的握手失败，正在重试……（第 {RecoverCount} 次尝试）{vbCrLf}若一直失败，请让房主重新创建房间。"
                            Case Else
                                RetryText = $"正在等待房主开放游戏端口……（第 {RecoverCount} 次尝试）{vbCrLf}若多次失败，请先确认房间码是否输入正确。"
                        End Select
                        UpdateLoadingPage(RetryText, "重试连接")
                        TcApi("/state/ide", 1200)
                        LastStateIndex = -1
                        CurState = ""
                        Thread.Sleep(900)
                        Continue Do
                    End If
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
                Return "无法与房主建立联机通道。" & vbCrLf &
                       "常见原因：" & vbCrLf &
                       "- 房主还没有在游戏内选择「对局域网开放」" & vbCrLf &
                       "- 你和房主之间没有可用的联机节点（可换一个节点重试，并检查代理与防火墙）"
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

    ''' <summary>启动器自行解析的陶瓦公共节点缓存。</summary>
    Private Shared PublicNodeCache As List(Of String) = Nothing
    ''' <summary>节点缓存的有效期（毫秒）：过期后重新获取并测速，避免一直使用一份已经失效的节点。</summary>
    Private Const TcNodeCacheMs As Integer = 10 * 60 * 1000
    Private Shared PublicNodeCacheTime As Long = 0
    ''' <summary>最近一次刷新时，是否至少有一个节点真的能连上。</summary>
    Private Shared TcHasReachableNode As Boolean = False

    ''' <summary>
    ''' 主动获取可用的公共节点。
    ''' 节点清单里给的其实是「解析地址」（例如 https://etnode.../node1），
    ''' 请求后才会返回真正的 tcp:// 中继地址，所以必须再解析一次。
    ''' 解析后逐个实测，剔除连不上的节点：陶瓦内置的 public.easytier.top / public2.easytier.cn 早已失效，
    ''' 仍把死节点交给陶瓦会让客人一直卡在「连接房主」并最终报 PingHostFail。
    ''' </summary>
    Private Shared Function TcFetchPublicNodes() As List(Of String)
        If PublicNodeCache IsNot Nothing AndAlso GetTimeMs() - PublicNodeCacheTime < TcNodeCacheMs Then Return PublicNodeCache
        Dim Nodes As New List(Of String)
        Dim Sources As New List(Of String)
        '1. 官方节点清单
        Try
            Dim Text As String = TcHttpGet("https://terracotta.glavo.site/nodes")
            Dim Json = Text.DeserializeJson()
            If TypeOf Json Is JArray Then
                For Each Item In CType(Json, JArray)
                    Dim Url As String = CStr(Item("url")).Trim()
                    If Url <> "" AndAlso Not Sources.Contains(Url) Then Sources.Add(Url)
                Next
            End If
        Catch ex As Exception
            Logger.Warn(ex, "获取陶瓦节点清单失败")
        End Try
        '2. 清单不可用时退回旧解析地址
        If Sources.Count = 0 Then
            Sources.Add("https://etnode.zkitefly.eu.org/node1")
            Sources.Add("https://etnode.zkitefly.eu.org/node2")
        End If
        '3. 逐个解析成真正的 tcp:// 中继地址
        For Each Source As String In Sources
            Try
                TcResolveNode(Source, Nodes)
            Catch ex As Exception
                Logger.Warn(ex, "解析陶瓦公共节点失败：" & Source)
            End Try
        Next
        '4. 逐个实测，只保留真的能连上的节点
        Dim Reachable As New List(Of String)
        For Each Node As String In Nodes
            If TcNodeReachable(Node) Then Reachable.Add(Node)
        Next
        If Reachable.Count > 0 Then
            TcHasReachableNode = True
            Nodes = Reachable
        Else
            TcHasReachableNode = False
            Logger.Warn("没有可连通的陶瓦公共节点：" & If(Nodes.Count > 0, String.Join("，", Nodes), "（节点清单为空）"))
        End If
        Logger.Info("陶瓦公共节点：" & If(Nodes.Count > 0, String.Join("，", Nodes), "（无）"))
        '空结果不写入缓存：否则用户点「重试」时还会拿到同一份空列表，只能干等 10 分钟
        If Nodes.Count > 0 Then
            PublicNodeCache = Nodes
            PublicNodeCacheTime = GetTimeMs()
        Else
            PublicNodeCache = Nothing
            PublicNodeCacheTime = 0
        End If
        Return Nodes
    End Function

    ''' <summary>节点是否可用：UDP 节点无法用 TCP 探测，直接保留；其余按 TCP 连通性判断。</summary>
    Private Shared Function TcNodeReachable(Node As String) As Boolean
        If Node.StartsWith("udp://", StringComparison.OrdinalIgnoreCase) Then Return True
        Return TcPingNode(Node) >= 0
    End Function

    ''' <summary>确认至少有一个可用的公共节点（加入房间前的预检）。</summary>
    Private Shared Function TcEnsureNodesUsable() As Boolean
        TcFetchPublicNodes()
        Return TcHasReachableNode
    End Function

    ''' <summary>把节点清单里的地址解析成实际的中继地址并加入节点列表。</summary>
    Private Shared Sub TcResolveNode(Source As String, Nodes As List(Of String))
        For Each Prefix As String In {"tcp://", "udp://", "ws://", "wss://"}
            If Source.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) Then
                If Not Nodes.Contains(Source) Then Nodes.Add(Source)
                Return
            End If
        Next
        If Not Source.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then Return
        Dim Text As String = TcHttpGet(Source)
        For Each Line As String In Text.Split({vbCrLf, vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            Dim Node As String = Line.Trim()
            For Each Prefix As String In {"tcp://", "udp://", "ws://", "wss://"}
                If Node.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) Then
                    If Not Nodes.Contains(Node) Then Nodes.Add(Node)
                    Return
                End If
            Next
        Next
        Logger.Warn($"节点解析地址没有返回有效中继：{Source}")
    End Sub

    ''' <summary>带超时的简单文本请求。</summary>
    Private Shared Function TcHttpGet(Url As String) As String
        Dim Request As HttpWebRequest = CType(WebRequest.Create(Url), HttpWebRequest)
        Request.Timeout = 5000
        Request.UserAgent = "PCL"
        Using Response As WebResponse = Request.GetResponse()
            Using Reader As New StreamReader(Response.GetResponseStream(), Text.Encoding.UTF8)
                Return Reader.ReadToEnd()
            End Using
        End Using
    End Function

    ''' <summary>
    ''' 把公共节点拼接成陶瓦 HTTP 接口的 public_nodes 参数。
    ''' 与 HMCL 一致：房主和加入者都带上完整节点列表，否则双方可能走不同中继而互相找不到。
    ''' </summary>
    Private Shared Function TcPublicNodesQuery() As String
        Dim Nodes As List(Of String) = TcFetchPublicNodes()
        Dim Query As New Text.StringBuilder
        If Not String.IsNullOrEmpty(TcSelectedNode) Then Query.Append("&public_nodes=" & Uri.EscapeDataString(TcSelectedNode))
        For Each Node As String In Nodes
            If Node = TcSelectedNode Then Continue For
            Query.Append("&public_nodes=" & Uri.EscapeDataString(Node))
        Next
        Return Query.ToString()
    End Function

    ''' <summary>当前用于创建房间的公共节点。</summary>
    Private Shared TcSelectedNode As String = ""

    ''' <summary>用于界面上展示的节点文本（不暴露 IP，按顺序显示为「一号节点」等）。</summary>
    Private Shared Function TcNodeText() As String
        Dim Node As String = If(TcSelectedNode, "")
        Dim Nodes = TcFetchPublicNodes()
        If Node = "" Then Node = If(Nodes.Count > 0, Nodes(0), "")
        If Node = "" Then Return "未获取到可用节点"
        Dim Index As Integer = Nodes.IndexOf(Node)
        Return TcNodeName(If(Index < 0, 0, Index))
    End Function

    ''' <summary>按序号生成节点名称，如「一号公共节点」。</summary>
    Private Shared Function TcNodeName(Index As Integer) As String
        Const Numerals As String = "一二三四五六七八九十"
        Dim Text As String = If(Index < Numerals.Length, Numerals.Substring(Index, 1), (Index + 1).ToString())
        Return Text & "号公共节点"
    End Function

    ''' <summary>延迟文字颜色：绿色良好、黄色一般、红色较差、灰色不可用。</summary>
    Private Shared Function TcPingColor(Ping As Integer) As String
        If Ping < 0 Then Return "#B8B8B8"
        If Ping <= 80 Then Return "#2FA84F"
        If Ping <= 200 Then Return "#E8A33D"
        Return "#E05B5B"
    End Function

    ''' <summary>延迟文字。</summary>
    Private Shared Function TcPingText(Ping As Integer) As String
        Return If(Ping >= 0, $"{Ping} ms", "不可用")
    End Function

    ''' <summary>测量节点延迟（毫秒），返回 -1 表示不可用或无法测速（如 UDP 节点）。</summary>
    Private Shared Function TcPingNode(Node As String) As Integer
        Try
            If Node.StartsWith("udp://", StringComparison.OrdinalIgnoreCase) Then Return -1
            Dim SchemeEnd As Integer = Node.IndexOf("://", StringComparison.Ordinal)
            If SchemeEnd < 0 Then Return -1
            Dim HostPort As String = Node.Substring(SchemeEnd + 3)
            Dim Colon As Integer = HostPort.LastIndexOf(":"c)
            If Colon <= 0 Then Return -1
            Dim Host As String = HostPort.Substring(0, Colon)
            Dim Port As Integer
            If Not Integer.TryParse(HostPort.Substring(Colon + 1), Port) OrElse Port <= 0 Then Return -1
            Dim Watch As Stopwatch = Stopwatch.StartNew()
            Using Client As New TcpClient()
                Dim Task = Client.ConnectAsync(Host, Port)
                If Not Task.Wait(2500) Then Return -1
                If Not Client.Connected Then Return -1
            End Using
            Return CInt(Math.Min(Watch.ElapsedMilliseconds, Integer.MaxValue))
        Catch
            Return -1
        End Try
    End Function

    '节点下拉框：首次显示时测速并填充
    Private Sub ComboCreateNode_Loaded(sender As Object, e As RoutedEventArgs)
        If ComboCreateNode.Items.Count > 0 Then Return
        ComboCreateNode.Items.Add(New MyComboBoxItem With {.Content = "正在获取公共节点……", .Tag = ""})
        ComboCreateNode.SelectedIndex = 0
        RunInNewThread(
            Sub()
                Dim Nodes = TcFetchPublicNodes()
                Dim Pings = Nodes.Select(Function(N) New KeyValuePair(Of String, Integer)(N, TcPingNode(N))).ToList()
                RunInUi(
                    Sub()
                        ComboCreateNode.Items.Clear()
                        Dim BestIndex As Integer = 0, BestPing As Integer = Integer.MaxValue
                        For i = 0 To Pings.Count - 1
                            Dim Item = Pings(i)
                            Dim Row As New StackPanel With {.Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
                            Row.Children.Add(New TextBlock With {.Text = TcNodeName(i), .VerticalAlignment = VerticalAlignment.Center})
                            Row.Children.Add(New TextBlock With {
                                .Text = TcPingText(Item.Value),
                                .VerticalAlignment = VerticalAlignment.Center,
                                .Margin = New Thickness(10, 0, 0, 0),
                                .FontSize = 12.5,
                                .Foreground = New System.Windows.Media.SolidColorBrush(System.Windows.Media.ColorConverter.ConvertFromString(TcPingColor(Item.Value)))
                            })
                            Dim NodeItem As New MyComboBoxItem With {
                                .Content = Row,
                                .Tag = Item.Key,
                                .ToolTip = If(Item.Value >= 0, $"{TcNodeName(i)}：{Item.Value} ms", $"{TcNodeName(i)}：不可用")
                            }
                            System.Windows.Automation.AutomationProperties.SetName(NodeItem, If(Item.Value >= 0, $"{TcNodeName(i)}，延迟 {Item.Value} ms", $"{TcNodeName(i)}，不可用"))
                            ComboCreateNode.Items.Add(NodeItem)
                            If Item.Value >= 0 AndAlso Item.Value < BestPing Then
                                BestPing = Item.Value
                                BestIndex = i
                            End If
                        Next
                        If Pings.Count = 0 Then
                            LabNodeStatus.Text = "没有可用的公共节点，联机可能无法建立"
                        Else
                            ComboCreateNode.SelectedIndex = BestIndex
                            LabNodeStatus.Text = $"已测速，共 {Pings.Count} 个节点"
                        End If
                    End Sub)
            End Sub, "TcNodePing")
    End Sub

    '节点下拉框：记录选中的节点
    Private Sub ComboCreateNode_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Dim Item = TryCast(ComboCreateNode.SelectedItem, MyComboBoxItem)
        If Item IsNot Nothing Then TcSelectedNode = TryCast(Item.Tag, String)
    End Sub

    ''' <summary>
    ''' 结束残留的陶瓦进程：陶瓦需要占用 UDP 4445 监听局域网广播，
    ''' 残留实例会抢走该端口，导致新房间无法被游戏局域网列表发现。
    ''' 只清理位于本启动器目录下的实例，避免影响其它启动器正在使用的房间。
    ''' </summary>
    Private Shared Sub TcKillStaleProcesses()
        For Each Proc As Process In Process.GetProcesses()
            Try
                If Not Proc.ProcessName.StartsWith("terracotta", StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim FilePath As String = Nothing
                Try
                    FilePath = Proc.MainModule?.FileName
                Catch
                    '无权读取模块信息时按名称清理
                End Try
                If Not String.IsNullOrEmpty(FilePath) Then
                    Dim IsOwnedPath As Boolean =
                        FilePath.StartsWith(Paths.AppData, StringComparison.OrdinalIgnoreCase) OrElse
                        FilePath.StartsWith(Paths.Base, StringComparison.OrdinalIgnoreCase) OrElse
                        FilePath.StartsWith(IO.Path.GetTempPath, StringComparison.OrdinalIgnoreCase)
                    If Not IsOwnedPath Then Continue For
                End If
                Logger.Info($"发现残留的陶瓦进程，正在结束：PID {Proc.Id}，路径：{If(FilePath, "未知")}")
                'peaceful 让陶瓦自行清理虚拟网卡
                Try
                    TcApi("/panic?peaceful=true", 800)
                Catch
                End Try
                Proc.Kill()
                Proc.WaitForExit(1500)
            Catch
            End Try
        Next
        '等待虚拟网卡与端口释放
        Thread.Sleep(300)
    End Sub

#End Region

#Region "接口与进程"

    ''' <summary>调用陶瓦本地 HTTP 接口，失败返回 Nothing</summary>
    Private Shared Function TcApi(Path As String, Optional TimeoutMs As Integer = 1500) As String
        Dim Body As String = Nothing
        TcApiStatus(Path, Body, TimeoutMs)
        Return Body
    End Function

    ''' <summary>
    ''' 调用陶瓦本地 HTTP 接口，返回 HTTP 状态码（通信失败返回 -1），响应正文由 Body 带回。
    ''' 陶瓦用 400 表示「请求被拒绝」，必须区分出来，否则会表现为一直卡着不动。
    ''' </summary>
    Private Shared Function TcApiStatus(Path As String, ByRef Body As String, Optional TimeoutMs As Integer = 1500) As Integer
        Body = Nothing
        If TcPort <= 0 Then Return -1
        Try
            Dim Request As HttpWebRequest = CType(WebRequest.Create($"http://127.0.0.1:{TcPort}{Path}"), HttpWebRequest)
            Request.Method = "GET"
            Request.Timeout = TimeoutMs
            Request.ReadWriteTimeout = TimeoutMs
            Request.Proxy = Nothing
            Request.KeepAlive = False
            Using Response As HttpWebResponse = CType(Request.GetResponse(), HttpWebResponse)
                Using Reader As New System.IO.StreamReader(Response.GetResponseStream(), Encoding.UTF8)
                    Body = Reader.ReadToEnd()
                End Using
                Return CInt(Response.StatusCode)
            End Using
        Catch ex As WebException
            '400 等错误也会带回响应体
            Try
                If ex.Response IsNot Nothing Then
                    Dim ErrorResponse As HttpWebResponse = CType(ex.Response, HttpWebResponse)
                    Using Reader As New System.IO.StreamReader(ErrorResponse.GetResponseStream(), Encoding.UTF8)
                        Body = Reader.ReadToEnd()
                    End Using
                    Return CInt(ErrorResponse.StatusCode)
                End If
            Catch
            End Try
            Return -1
        Catch ex As Exception
            Return -1
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
        Try
            If TcPortDir IsNot Nothing AndAlso IO.Directory.Exists(TcPortDir) Then IO.Directory.Delete(TcPortDir, True)
        Catch ex As Exception
            Logger.Warn(ex, "清理陶瓦端口文件目录失败")
        End Try
        TcPortDir = Nothing
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

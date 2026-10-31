Public Class PageLoginAuth
    Private IsFirstLoad As Boolean = True
    ''' <summary>
    ''' 内置皮肤站预设。
    ''' </summary>
    Public ReadOnly AuthServerPresets As New List(Of String) From {
        "https://littleskin.cn/api/yggdrasil",
        "https://mcskin.ml/api/yggdrasil",
        "https://skin.flowing.top/api/yggdrasil"
    }
    ''' <summary>
    ''' 归一化皮肤站地址。
    ''' </summary>
    Private Function NormalizeServer(Input As String) As String
        Return NormalizeServerShared(Input)
    End Function
    ''' <summary>
    ''' 把皮肤站地址同步到全局缓存与当前版本的独立设置中。
    ''' 启动阶段的参数构建、皮肤获取只读取版本设置，若只写缓存就会拿到空地址并报 URI 无效。
    ''' </summary>
    Public Shared Sub SaveServerSettings(Server As String)
        If AniControlEnabled <> 0 Then Return
        Server = NormalizeServerShared(Server)
        If Server = "" Then Return
        Settings.Set("CacheAuthServerServer", Server)
        If McInstanceSelected IsNot Nothing Then Settings.Set("VersionServerAuthServer", Server, Instance:=McInstanceSelected)
    End Sub
    ''' <summary>
    ''' 刷新页面显示的所有信息。
    ''' </summary>
    Public Sub Reload(KeepInput As Boolean)
        '皮肤站地址（缓存为空时回退到版本设置中的地址，避免输入框空白导致无法登录）
        Dim SavedServer As String = NormalizeServer(Settings.Get(Of String)("CacheAuthServerServer"))
        If SavedServer = "" AndAlso McInstanceSelected IsNot Nothing Then
            SavedServer = NormalizeServer(Settings.Get(Of String)("VersionServerAuthServer", Instance:=McInstanceSelected))
        End If
        If SavedServer <> "" Then Settings.Set("CacheAuthServerServer", SavedServer)
        Dim ServerList As New List(Of String)
        If SavedServer <> "" AndAlso Not AuthServerPresets.Contains(SavedServer) Then ServerList.Add(SavedServer)
        ServerList.AddRange(AuthServerPresets)
        ComboServer.ItemsSource = ServerList
        ComboServer.Text = If(KeepInput AndAlso ComboServer.Text <> "", ComboServer.Text, SavedServer)
        '记住密码
        CheckRemember.Checked = Settings.Get(Of Boolean)("LoginRemember")
        If KeepInput AndAlso Not IsFirstLoad Then '避免第一次就以 KeepInput 的方式加载，导致文本框里没东西
            '保留输入，只刷新下拉框列表
            Dim Input As String = ComboName.Text
            ComboName.ItemsSource = If(Settings.Get(Of String)("LoginAuthEmail") = "", Nothing, Settings.Get(Of String)("LoginAuthEmail").Split("¨"))
            ComboName.Text = Input
        Else
            '不保留输入，刷新列表后自动选择第一项
            If Settings.Get(Of String)("LoginAuthEmail") = "" Then
                ComboName.ItemsSource = Nothing
            Else
                ComboName.ItemsSource = Settings.Get(Of String)("LoginAuthEmail").Split("¨")
                ComboName.Text = Settings.Get(Of String)("LoginAuthEmail").BeforeFirst("¨")
                If Settings.Get(Of Boolean)("LoginRemember") Then TextPass.Password = Settings.Get(Of String)("LoginAuthPass").BeforeFirst("¨").Trim
            End If
        End If
        IsFirstLoad = False
    End Sub
    ''' <summary>
    ''' 获取当前页面的登录信息。
    ''' </summary>
    Public Shared Function GetLoginData() As McLoginServer
        Dim Server As String = GetServerAddress() & "/authserver"
        If FrmLoginAuth Is Nothing Then
            Return New McLoginServer(McLoginType.Auth) With {.Token = "Auth", .BaseUrl = Server, .UserName = "", .Password = "", .Description = "Authlib-Injector", .Type = McLoginType.Auth}
        Else
            Return New McLoginServer(McLoginType.Auth) With {.Token = "Auth", .BaseUrl = Server, .UserName = FrmLoginAuth.ComboName.Text.Replace("¨", "").Trim, .Password = FrmLoginAuth.TextPass.Password.Replace("¨", "").Trim, .Description = "Authlib-Injector", .Type = McLoginType.Auth}
        End If
    End Function
    ''' <summary>
    ''' 当前页面的登录信息是否有效。
    ''' </summary>
    Public Shared Function IsVaild(LoginData As McLoginServer) As String
        '注意：本函数会在后台登录线程中被调用，绝对不能直接读取任何 WPF 控件，
        '否则会抛出“调用线程无法访问此对象”，导致外置登录直接失败、游戏无法启动。
        If GetServerAddress() = "" Then Return "请先填写外置登录地址！"
        If LoginData Is Nothing Then Return "请先填写外置登录地址！"
        If LoginData.UserName = "" Then Return "账号不能为空！"
        If LoginData.Password = "" Then Return "密码不能为空！"
        Return ""
    End Function
    Public Function IsVaild() As String
        Return IsVaild(GetLoginData())
    End Function
    ''' <summary>
    ''' 获取当前实际生效的外置登录地址（不含 /authserver 后缀）。
    ''' 可在任意线程调用：只有在 UI 线程时才读取输入框，其余情况回退到已保存的设置。
    ''' </summary>
    Public Shared Function GetServerAddress() As String
        Dim Server As String = ""
        '优先使用输入框中的地址
        If FrmLoginAuth IsNot Nothing AndAlso FrmLoginAuth.Dispatcher.CheckAccess() Then
            Server = NormalizeServerShared(FrmLoginAuth.ComboServer.Text)
        End If
        '回退到上次保存的地址
        If Server = "" Then Server = NormalizeServerShared(Settings.Get(Of String)("CacheAuthServerServer"))
        '再回退到版本单独设置的地址
        If Server = "" AndAlso McInstanceSelected IsNot Nothing Then
            Server = NormalizeServerShared(Settings.Get(Of String)("VersionServerAuthServer", Instance:=McInstanceSelected))
        End If
        Return Server
    End Function
    Public Shared Function NormalizeServerShared(Input As String) As String
        If Input Is Nothing Then Return ""
        Dim Server As String = Input.Replace("¨", "").Trim
        If Server = "" Then Return ""
        If Not Server.Contains("://") Then Server = "https://" & Server
        Server = Server.TrimEnd("/"c)
        '允许直接粘贴以 /authserver 结尾的完整认证地址，避免拼出 /authserver/authserver
        If Server.EndsWithF("/authserver", True) Then Server = Server.Substring(0, Server.Length - 11).TrimEnd("/"c)
        Return Server
    End Function

    '保存输入信息
    Private Sub ComboName_TextChanged(sender As Object, e As TextChangedEventArgs) Handles ComboName.TextChanged
        If sender.Text = "" Then TextPass.Password = ""
        If AniControlEnabled = 0 Then Settings.Set("CacheAuthAccess", "")  '迫使其不进行 Validate
    End Sub
    Private Sub TextPass_PasswordChanged(sender As Object, e As RoutedEventArgs) Handles TextPass.PasswordChanged
        If AniControlEnabled = 0 Then Settings.Set("CacheAuthAccess", "")
    End Sub
    Private Sub ComboName_SelectionChanged(sender As MyComboBox, e As SelectionChangedEventArgs) Handles ComboName.SelectionChanged
        If sender.SelectedIndex = -1 OrElse Not Settings.Get(Of Boolean)("LoginRemember") Then
            TextPass.Password = ""
        Else
            TextPass.Password = Settings.Get(Of String)("LoginAuthPass").ToString.Split("¨")(sender.SelectedIndex).Trim
        End If
    End Sub
    Private Sub CheckBoxChange(sender As MyCheckBox, e As Object) Handles CheckRemember.Change
        If AniControlEnabled = 0 Then Settings.Set(sender.Tag, sender.Checked)
    End Sub

    '皮肤站地址处理
    Private _serverDebounce As System.Windows.Threading.DispatcherTimer
    ''' <summary>
    ''' 延迟拉取皮肤站信息。避免每输入一个字符就发起一次网络请求（原本会瞬间开出几十个线程）。
    ''' </summary>
    Private Sub QueueFetchServerInfo()
        If AniControlEnabled <> 0 OrElse IsFirstLoad Then Return
        If _serverDebounce Is Nothing Then
            _serverDebounce = New System.Windows.Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(600)}
            AddHandler _serverDebounce.Tick,
                Sub(s As Object, ev As EventArgs)
                    _serverDebounce.Stop()
                    Settings.Set("CacheAuthAccess", "")
                    FetchServerInfo(NormalizeServerShared(ComboServer.Text))
                End Sub
        End If
        _serverDebounce.Stop()
        _serverDebounce.Start()
    End Sub
    Private Sub ComboServer_TextChanged(sender As Object, e As TextChangedEventArgs) Handles ComboServer.TextChanged
        '输入过程中只保存看起来完整的地址，避免半截内容覆盖掉可用地址
        Dim Server As String = NormalizeServerShared(ComboServer.Text)
        If Server.Contains(".") Then SaveServerSettings(Server)
        QueueFetchServerInfo()
    End Sub
    Private Sub ComboServer_SelectionChanged(sender As MyComboBox, e As SelectionChangedEventArgs) Handles ComboServer.SelectionChanged
        QueueFetchServerInfo()
    End Sub
    ''' <summary>
    ''' 拉取皮肤站元数据（名称、注册地址），失败时静默。
    ''' </summary>
    Private Sub FetchServerInfo(Server As String)
        If Server = "" Then Return
        SaveServerSettings(Server)
        RunInNewThread(Sub()
                           Try
                               Dim Info As String = NetRequestByClientRetry(Server, Encoding:=Encoding.UTF8)
                               Dim JsonObject As JObject = JObject.Parse(Info)
                               Dim Name As String = If(JsonObject("meta")?("serverName"), "")
                               Dim Register As String = If(JsonObject("meta")?("links")?("registerHomepage"), "")
                               RunInUi(Sub()
                                           Dim RegisterAddress As String = If(Register, "")
                                           If RegisterAddress = "" AndAlso Info.Contains("/auth/register") Then RegisterAddress = Server.Replace("/api/yggdrasil", "") & "/auth/register"
                                           If Name <> "" Then
                                               Settings.Set("CacheAuthServerName", Name)
                                               If McInstanceSelected IsNot Nothing AndAlso Settings.Get(Of Integer)("VersionServerLogin", Instance:=McInstanceSelected) = 4 Then Settings.Set("VersionServerAuthName", Name, Instance:=McInstanceSelected)
                                           End If
                                           Settings.Set("CacheAuthServerRegister", RegisterAddress)
                                           If RegisterAddress <> "" AndAlso McInstanceSelected IsNot Nothing AndAlso Settings.Get(Of Integer)("VersionServerLogin", Instance:=McInstanceSelected) = 4 Then Settings.Set("VersionServerAuthRegister", RegisterAddress, Instance:=McInstanceSelected)
                                       End Sub)
                           Catch ex As Exception
                               '元数据拉取失败不阻塞登录，登录时会对地址进行实际校验
                           End Try
                       End Sub, "皮肤站元数据")
    End Sub

    '链接处理
    Private Sub ComboName_TextChanged() Handles ComboName.TextChanged
        BtnLink.Content = If(ComboName.Text = "", "注册账号", "找回密码")
    End Sub
    Private Sub Btn_Click(sender As Object, e As EventArgs) Handles BtnLink.Click
        If BtnLink.Content = "注册账号" Then
            OpenWebsite(If(McInstanceSelected IsNot Nothing, Settings.Get(Of String)("VersionServerAuthRegister", Instance:=McInstanceSelected), Settings.Get(Of String)("CacheAuthServerRegister")))
        Else
            Dim Website As String = If(McInstanceSelected IsNot Nothing, Settings.Get(Of String)("VersionServerAuthRegister", Instance:=McInstanceSelected), Settings.Get(Of String)("CacheAuthServerRegister"))
            OpenWebsite(Website.Replace("/auth/register", "/auth/forgot"))
        End If
    End Sub
    '切换注册按钮可见性
    Private Sub ReloadRegisterButton() Handles Me.Loaded
        Dim Address As String = If(McInstanceSelected IsNot Nothing, Settings.Get(Of String)("VersionServerAuthRegister", Instance:=McInstanceSelected), Settings.Get(Of String)("CacheAuthServerRegister"))
        BtnLink.Visibility = If(String.IsNullOrEmpty(New ValidateHttp().Validate(Address)), Visibility.Visible, Visibility.Collapsed)
    End Sub

End Class

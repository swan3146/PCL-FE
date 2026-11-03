'由于包含加解密等安全信息，本文件中的部分代码已被删除

Friend Module ModSecret

    '标注 PCL 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "OpenSource"
    '在开源版的注册表与常规版的注册表隔离，以防数据冲突
    Public Const RegFolder As String = "PCLDebug"
    '用于微软登录的 ClientId（默认使用微软第一方公共客户端，可被环境变量 PCL_MS_CLIENT_ID 覆盖）
    Public OAuthClientId As String = If(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"), "04b07795-8ddb-461a-bbee-02f9e1bf7b46")
    'Microsoft 账户（MSA）第一方客户端，已被 Xbox Live 资源服务器预授权，device code 登录必须用它
    Public OAuthClientIdMsa As String = If(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID_MSA"), "00000000402b5328")
    'MSA device code 流程所需的 Scope（Xbox Live 用户认证）
    Public Const OAuthScopeMsa As String = "service::user.auth.xboxlive.com::MBI_SSL"
    'CurseForge API Key
    Public CurseForgeAPIKey As String = If(Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY"), "")
    '用于匿名数据收集的腾讯云日志服务上报 URL，形如 https://{region}.cls.tencentcs.com/track?topic_id={topic_id}
    Public Const ClsBaseUrl As String = ""

#Region "网络鉴权"

    Public Const RestrictMcDownload As Boolean = False
    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If Not UrlWithMark.EndsWithF("{CDN}") Then Return UrlWithMark
        Return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
    End Function
    ''' <summary>
    ''' 设置 Headers 的 UA、Referer。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        If Not Req.Headers.UserAgent.Any Then
            If Url.Contains("baidupcs.com") OrElse Url.Contains("baidu.com") Then
                Req.Headers.Add("User-Agent", "LogStatistic")  '#4951
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
            End If
        End If
        If Not SimulateBrowserHeaders Then Req.Headers.Add("Referer", $"http://{VersionCode}.open.pcl2.server/")
        If Url.Contains("api.curseforge.com") OrElse Url.Contains("forgecdn.net") Then Req.Headers.Add("x-api-key", CurseForgeAPIKey)
    End Sub

#End Region

#Region "主题"

    Public Color1 As New MyColor(52, 61, 74)
    Public Color2 As New MyColor(11, 91, 203)
    Public Color3 As New MyColor(19, 112, 243)
    Public Color4 As New MyColor(72, 144, 245)
    Public Color5 As New MyColor(150, 192, 249)
    Public Color6 As New MyColor(213, 230, 253)
    Public Color7 As New MyColor(222, 236, 253)
    Public Color8 As New MyColor(234, 242, 254)
    Public ColorBg0 As New MyColor(150, 192, 249)
    Public ColorBg1 As New MyColor(190, Color7)
    Public ColorGray1 As New MyColor(64, 64, 64)
    Public ColorGray2 As New MyColor(115, 115, 115)
    Public ColorGray3 As New MyColor(140, 140, 140)
    Public ColorGray4 As New MyColor(166, 166, 166)
    Public ColorGray5 As New MyColor(204, 204, 204)
    Public ColorGray6 As New MyColor(235, 235, 235)
    Public ColorGray7 As New MyColor(240, 240, 240)
    Public ColorGray8 As New MyColor(245, 245, 245)
    Public ColorSemiTransparent As New MyColor(1, Color8)

    Public ThemeNow As Integer = -1
    Public ColorHue As Integer = 210, ColorSat As Integer = 85, ColorLightAdjust As Integer = 0, ColorHueTopbarDelta As OneOf(Of Integer, Integer()) = 0
    Public ThemeDontClick As Integer = 0

    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1)
        Try
            If ThemeNow = NewTheme AndAlso NewTheme >= 0 Then Return
            If NewTheme >= 0 Then ThemeNow = NewTheme

            '按主题预设色相、饱和度、亮度偏移与顶部栏渐变
            Dim ApplyTheme As Integer = If(ThemeNow >= 0, ThemeNow, 0)
            If ApplyTheme >= 15 Then ApplyTheme = 0
            Select Case ApplyTheme
                Case 0 '龙猫蓝
                    ColorHue = 210 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
                Case 1 '甜柠青
                    ColorHue = 174 : ColorSat = 78 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 14
                Case 2 '小草绿
                    ColorHue = 112 : ColorSat = 58 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 12
                Case 3 '菠萝黄
                    ColorHue = 45 : ColorSat = 92 : ColorLightAdjust = 2 : ColorHueTopbarDelta = 10
                Case 4 '橡木棕
                    ColorHue = 28 : ColorSat = 46 : ColorLightAdjust = -2 : ColorHueTopbarDelta = 8
                Case 5 '玄素黑
                    ColorHue = 215 : ColorSat = 6 : ColorLightAdjust = -8 : ColorHueTopbarDelta = 0
                Case 6 '铁杆粉
                    ColorHue = 336 : ColorSat = 78 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 12
                Case 7 '神秘紫
                    ColorHue = 272 : ColorSat = 62 : ColorLightAdjust = -2 : ColorHueTopbarDelta = 16
                Case 8 '秋仪金
                    ColorHue = 38 : ColorSat = 76 : ColorLightAdjust = -2 : ColorHueTopbarDelta = 10
                Case 9 '活跃橙
                    ColorHue = 22 : ColorSat = 90 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 10
                Case 10 '跳票红
                    ColorHue = 353 : ColorSat = 78 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 12
                Case 11 '极客蓝
                    ColorHue = 220 : ColorSat = 92 : ColorLightAdjust = -8 : ColorHueTopbarDelta = 14
                Case 12 '滑稽彩
                    ColorHue = CInt((Date.Now.Minute * 6 + Date.Now.Hour * 17) Mod 360) : ColorSat = 88 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 46
                Case 13 '欧皇彩
                    ColorHue = RandomInteger(0, 359) : ColorSat = 88 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 32
                Case 14 '自定义
                    ColorHue = Settings.Get(Of Integer)("UiLauncherHue")
                    ColorSat = Settings.Get(Of Integer)("UiLauncherSat")
                    ColorHueTopbarDelta = Settings.Get(Of Integer)("UiLauncherDelta")
                    ColorLightAdjust = Settings.Get(Of Integer)("UiLauncherLight") - 20
                Case Else
                    ColorHue = 210 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
            End Select

            Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.2, 25 + ColorLightAdjust * 0.3)
            Color2 = New MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust)
            Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust)
            Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust)
            Color5 = New MyColor().FromHSL2(ColorHue, ColorSat, 80 + ColorLightAdjust * 0.4)
            Color6 = New MyColor().FromHSL2(ColorHue, ColorSat, 91 + ColorLightAdjust * 0.1)
            Color7 = New MyColor().FromHSL2(ColorHue, ColorSat, 95)
            Color8 = New MyColor().FromHSL2(ColorHue, ColorSat, 97)
            ColorBg0 = Color4 * 0.4 + Color5 * 0.4 + ColorGray4 * 0.2
            ColorBg1 = New MyColor(190, Color7)

            ColorSemiTransparent = New MyColor(1, Color8)
            Application.Current.Resources("ColorBrush1") = New SolidColorBrush(Color1)
            Application.Current.Resources("ColorBrush2") = New SolidColorBrush(Color2)
            Application.Current.Resources("ColorBrush3") = New SolidColorBrush(Color3)
            Application.Current.Resources("ColorBrush4") = New SolidColorBrush(Color4)
            Application.Current.Resources("ColorBrush5") = New SolidColorBrush(Color5)
            Application.Current.Resources("ColorBrush6") = New SolidColorBrush(Color6)
            Application.Current.Resources("ColorBrush7") = New SolidColorBrush(Color7)
            Application.Current.Resources("ColorBrush8") = New SolidColorBrush(Color8)
            Application.Current.Resources("ColorBrushBg0") = New SolidColorBrush(ColorBg0)
            Application.Current.Resources("ColorBrushBg1") = New SolidColorBrush(ColorBg1)
            Application.Current.Resources("ColorObject1") = CType(Color1, Color)
            Application.Current.Resources("ColorObject2") = CType(Color2, Color)
            Application.Current.Resources("ColorObject3") = CType(Color3, Color)
            Application.Current.Resources("ColorObject4") = CType(Color4, Color)
            Application.Current.Resources("ColorObject5") = CType(Color5, Color)
            Application.Current.Resources("ColorObject6") = CType(Color6, Color)
            Application.Current.Resources("ColorObject7") = CType(Color7, Color)
            Application.Current.Resources("ColorObject8") = CType(Color8, Color)
            Application.Current.Resources("ColorObjectBg0") = CType(ColorBg0, Color)
            Application.Current.Resources("ColorObjectBg1") = CType(ColorBg1, Color)
            ThemeRefreshMain()
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub

    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If Not FrmMain.IsLoaded Then Return
            '顶部条背景
            Dim Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
            Dim Deltas = ColorHueTopbarDelta.Switch(Function(d) New Integer() {-d, 0, d}, Function(d) d)
            Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue + Deltas(0), ColorSat, 48 + ColorLightAdjust)})
            Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue + Deltas(1), ColorSat, 54 + ColorLightAdjust)})
            Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue + Deltas(2), ColorSat, 48 + ColorLightAdjust)})
            FrmMain.PanTitle.Background = Brush
            FrmMain.PanTitle.Background.Freeze()
            '主页面背景
            If Settings.Get(Of Boolean)("UiBackgroundColorful") Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8, 91)})
                FrmMain.PanForm.Background = Brush
            Else
                FrmMain.PanForm.Background = New MyColor(245, 245, 245)
            End If
            FrmMain.PanForm.Background.Freeze()
        End Sub)
    End Sub
    Friend Sub ThemeCheckAll(EffectSetup As Boolean)
    End Sub
    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        Return True
    End Function
    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        Return False
    End Function

#End Region

#Region "更新"

    Friend Sub UpdateCheckByButton()
        CheckUpdateManually()
    End Sub

    Friend IsUpdateWaitingRestart As Boolean = False
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
    End Sub
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
    End Sub

    ''' <summary>
    ''' 由临时副本进程执行的自我替换：等待旧进程退出后覆盖程序文件并重启。
    ''' 路径全部通过环境变量传递，不经过命令行或批处理解析，因此能兼容中文、空格与各种特殊符号。
    ''' </summary>
    Public Sub UpdateReplaceFromEnvironment()
        Dim OldPath As String = Environment.GetEnvironmentVariable("PCL_UPDATE_OLD")
        Dim NewPath As String = Environment.GetEnvironmentVariable("PCL_UPDATE_NEW")
        Dim OldPid As String = Environment.GetEnvironmentVariable("PCL_UPDATE_PID")
        Dim SelfPath As String = Environment.GetEnvironmentVariable("PCL_UPDATE_SELF")
        Dim Restart As Boolean = Environment.GetEnvironmentVariable("PCL_UPDATE_RESTART") = "1"
        If String.IsNullOrEmpty(OldPath) OrElse String.IsNullOrEmpty(NewPath) Then
            Environment.Exit(1)
        End If
        '等待旧进程彻底退出
        Try
            Dim OldProcess As Diagnostics.Process = Diagnostics.Process.GetProcessById(CInt(OldPid))
            OldProcess.WaitForExit(120 * 1000)
        Catch
        End Try
        '文件句柄释放可能比进程退出略晚，重试覆盖
        Dim Replaced As Boolean = False
        If IO.File.Exists(NewPath) Then
            For i = 1 To 60
                Try
                    IO.File.Copy(NewPath, OldPath, True)
                    Replaced = True
                    Exit For
                Catch ex As Exception
                    Threading.Thread.Sleep(250)
                End Try
            Next
        End If
        '替换失败时保留旧版本可用，避免用户点完更新后启动器彻底打不开
        If Restart AndAlso IO.File.Exists(OldPath) Then
            Try
                Diagnostics.Process.Start(New Diagnostics.ProcessStartInfo(OldPath) With {
                    .UseShellExecute = True,
                    .WorkingDirectory = IO.Path.GetDirectoryName(OldPath)})
            Catch
            End Try
        End If
        '清理
        If Replaced Then
            Try
                IO.File.Delete(NewPath)
            Catch
            End Try
        End If
        If Not String.IsNullOrEmpty(SelfPath) AndAlso Not String.Equals(SelfPath, OldPath, StringComparison.OrdinalIgnoreCase) Then
            Try
                IO.File.Delete(SelfPath)
            Catch
            End Try
        End If
        Environment.Exit(0)
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    ''' 如果不是，则下载一个。
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        '注意：如果要自行实现这个功能，请换用另一个文件路径，以免与官方版本冲突
    End Sub

#End Region

#Region "联网配置"

    ''' <summary>
    ''' 联网获取的配置信息。
    ''' 若获取失败或仍在获取中，可能为 Nothing。
    ''' </summary>
    Public ServerConfig As JObject

    Public ServerLoader As New LoaderTask(Of Integer, Integer)("PCL 配置更新", AddressOf LoadLocalServerConfig, Priority:=ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1000 * 60 * 60} '超时 1 小时

    ''' <summary>
    ''' 开源版本无法访问官方配置服务器，因此使用内置的本地配置。
    ''' 主要用于联机模块（EasyTier / 陶瓦联机）的版本号与下载地址。
    ''' </summary>
    Private Sub LoadLocalServerConfig()
        ServerConfig = JObject.Parse("{
  ""Link"": {
    ""EasyTierVersion"": 264,
    ""Downloads"": [
      ""https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-windows-{arch}-v2.6.4.zip"",
      ""https://gh-proxy.com/https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-windows-{arch}-v2.6.4.zip"",
      ""https://ghfast.top/https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-windows-{arch}-v2.6.4.zip""
    ],
    ""Argument"": """",
    ""DiscoverPeerId"": 0,
    ""DiscoverPeer"": ""tcp://public.easytier.cn:11010"",
    ""PeersBlackList"": [],
    ""RandomPeer"": 3,
    ""Peers"": [""tcp://public.easytier.cn:11010""],
    ""MandatoryPeers"": []
  }
}")
        Logger.Info("已加载内置的本地服务器配置（联机模块：EasyTier v2.6.4）")
    End Sub

#End Region

#Region "赞助等级"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return DonationRank.None
        End Get
    End Property

    Public Sub InputPotatoCode(IsUpdating As Boolean)
    End Sub
    Friend Sub GeneratePotatoCode()
    End Sub

    ''' <summary>
    ''' 获取设备识别码。
    ''' </summary>
    Friend Function GetIdentify() As String
        Return "0000-0000-0000-0000"
    End Function

#End Region

End Module

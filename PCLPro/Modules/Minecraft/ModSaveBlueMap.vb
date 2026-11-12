Imports System.Diagnostics
Imports System.IO
Imports System.Management
Imports System.Net
Imports System.Net.Sockets
Imports System.Security.Cryptography
Imports System.Text

''' <summary>
''' 调用 BlueMap 官方 CLI（MIT 协议）离线渲染存档，并启动它的网页服务器。
''' </summary>
Public Module ModSaveBlueMap

    Public Const BlueMapVersion As String = "5.29"

    Private ReadOnly ServerProcesses As New Dictionary(Of String, Process)(StringComparer.OrdinalIgnoreCase)
    Private CleanupRegistered As Boolean

    Public ReadOnly Property BlueMapJarPath As String
        Get
            Return Paths.Base & "PCL\Cache\BlueMap\bluemap-" & BlueMapVersion & "-cli.jar"
        End Get
    End Property

    ''' <summary>
    ''' 下载 BlueMap CLI（约 7MB），优先走 GitHub 镜像，失败再回退官方直连。
    ''' </summary>
    Public Function PrepareJar() As Boolean
        If IsValidJar(BlueMapJarPath) Then Return True
        DirectoryUtils.Create(PathUtils.RemoveLastPart(BlueMapJarPath))
        Logger.Info($"开始下载 BlueMap CLI v{BlueMapVersion}")
        Dim DirectUrl As String = $"https://github.com/BlueMap-Minecraft/BlueMap/releases/download/v{BlueMapVersion}/bluemap-{BlueMapVersion}-cli.jar"
        Dim Urls As String() = {
            "https://gh-proxy.com/" & DirectUrl,
            "https://ghproxy.net/" & DirectUrl,
            DirectUrl
        }
        For Each Url As String In Urls
            Try
                Logger.Info($"尝试下载 BlueMap CLI：{Url}")
                NetDownloadByClient(Url, BlueMapJarPath, True)
                If IsValidJar(BlueMapJarPath) Then Return True
                Logger.Warn($"下载到的 BlueMap CLI 不完整，尝试下一个源：{Url}")
            Catch ex As Exception
                Logger.Warn(ex, $"从该源下载 BlueMap CLI 失败：{Url}")
            End Try
            FileUtils.Delete(BlueMapJarPath)
        Next
        Return False
    End Function

    Private Function IsValidJar(FileName As String) As Boolean
        Try
            If Not FileUtils.Exists(FileName) Then Return False
            If New FileInfo(FileName).Length < 4000000 Then Return False
            Using Strm As New FileStream(FileName, FileMode.Open, FileAccess.Read)
                Return Strm.ReadByte() = &H50 AndAlso Strm.ReadByte() = &H4B ' PK
            End Using
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 用 BMCLAPI 镜像预下载 Minecraft 客户端 jar（BlueMap 渲染材质要用），失败则让 BlueMap 自己下载。
    ''' </summary>
    Public Sub PrepareClientJar(Root As String, VersionText As String)
        Try
            If String.IsNullOrEmpty(VersionText) OrElse VersionText = "Unknown" Then Return
            Dim Target As String = Root & "data\minecraft-client-" & VersionText & ".jar"
            If FileUtils.Exists(Target) AndAlso New FileInfo(Target).Length > 1000000 Then Return
            DirectoryUtils.Create(Root & "data\")
            Logger.Info($"正在通过镜像下载 Minecraft {VersionText} 客户端资源")
            NetDownloadByClient($"https://bmclapi2.bangbang93.com/version/{VersionText}/client", Target, True)
            If Not FileUtils.Exists(Target) OrElse New FileInfo(Target).Length < 1000000 Then FileUtils.Delete(Target)
        Catch ex As Exception
            Logger.Warn(ex, "通过镜像下载 Minecraft 客户端资源失败，将交由 BlueMap 自行下载")
            Try
                FileUtils.Delete(Root & "data\minecraft-client-" & VersionText & ".jar")
            Catch
            End Try
        End Try
    End Sub

    ''' <summary>
    ''' 优先使用启动器里检测到的 Java 21+，否则退回 PATH 中的 java。
    ''' </summary>
    Public Function GetJavaExe() As String
        Dim Best As Java = Nothing
        For Each Candidate In Configs.JavaList.Get()
            If Candidate.Version IsNot Nothing AndAlso Candidate.Version.Major >= 21 Then
                If Best Is Nothing OrElse Candidate.Version > Best.Version Then Best = Candidate
            End If
        Next
        If Best IsNot Nothing AndAlso FileUtils.Exists(Best.JavaExePath) Then Return Best.JavaExePath
        Return "java"
    End Function

    ''' <summary>
    ''' 提取 WebView2 原生加载器到启动器目录（内嵌发布，不额外附带 DLL）。
    ''' </summary>
    Public Sub EnsureWebViewLoader()
        Try
            ExtractResources(PathPure.Value & "WebView2Loader.dll", "WebView2Loader")
        Catch ex As Exception
            Logger.Warn(ex, "提取 WebView2Loader 失败")
        End Try
    End Sub

    Public Function GetRoot(Info As ModSave.SaveInfo) As String
        Using Md5 As MD5 = MD5.Create()
            Dim Hash As String = BitConverter.ToString(Md5.ComputeHash(Encoding.UTF8.GetBytes(Info.Path.ToLowerInvariant()))).Replace("-", "")
            Return Paths.Base & "PCL\Cache\BlueMap\Maps\" & Hash.Substring(0, 16) & "\"
        End Using
    End Function

    ''' <summary>
    ''' 写入 BlueMap 配置（每次覆盖，保证与存档信息一致）。
    ''' </summary>
    Private Sub WriteConfigs(Info As ModSave.SaveInfo, Root As String, Instance As McInstance)
        DirectoryUtils.Create(Root & "config\maps\")
        DirectoryUtils.Create(Root & "config\storages\")
        Dim Radius As Integer = Math.Min(Math.Max(GetIntSetting("BlueMapRenderRadius", 512), 64), 2048)
        Dim Threads As Integer = Math.Min(Math.Max(GetIntSetting("BlueMapThreads", 2), 1), 16)
        Dim Hires As Boolean = GetIntSetting("BlueMapQuality", 0) = 0
        FileUtils.Write(Root & "config\core.conf",
            "accept-download: true" & vbCrLf &
            "data: ""data""" & vbCrLf &
            $"render-thread-count: {Threads}" & vbCrLf &
            "metrics: false" & vbCrLf)
        FileUtils.Write(Root & "config\webapp.conf",
            "enabled: true" & vbCrLf &
            "webroot: ""web""" & vbCrLf &
            "update-settings-file: true" & vbCrLf)
        FileUtils.Write(Root & "config\storages\file.conf",
            "storage-type: file" & vbCrLf &
            "root: ""web/maps""" & vbCrLf &
            "compression: gzip" & vbCrLf)
        Dim Name As String = Info.Name.Replace("\", "\\").Replace("""", "\""")
        FileUtils.Write(Root & "config\maps\map.conf",
            $"world: ""{Info.Path.Replace("\", "/")}""{vbCrLf}" &
            "dimension: ""minecraft:overworld""" & vbCrLf &
            $"name: ""{Name}""{vbCrLf}" &
            "sorting: 0" & vbCrLf &
            $"start-pos: {{ x: {Info.SpawnX}, z: {Info.SpawnZ} }}{vbCrLf}" &
            $"enable-hires: {Hires.ToString().ToLowerInvariant()}{vbCrLf}" &
            "storage: ""file""" & vbCrLf &
            "render-mask: [" & vbCrLf & "  {" & vbCrLf &
            $"    min-x: {-Radius}" & vbCrLf & $"    max-x: {Radius}" & vbCrLf &
            $"    min-z: {-Radius}" & vbCrLf & $"    max-z: {Radius}" & vbCrLf &
            "  }" & vbCrLf & "]" & vbCrLf)
    End Sub

    Private Function GetIntSetting(Key As String, DefaultValue As Integer) As Integer
        Try
            Return Settings.Get(Of Integer)(Key)
        Catch
            Return DefaultValue
        End Try
    End Function

    Private Function GetBoolSetting(Key As String, DefaultValue As Boolean) As Boolean
        Try
            Return Settings.Get(Of Boolean)(Key)
        Catch
            Return DefaultValue
        End Try
    End Function

    ''' <summary>
    ''' 调用 BlueMap 渲染存档，返回 webroot。存档未变化时直接跳过渲染。
    ''' </summary>
    Public Function RenderSave(Info As ModSave.SaveInfo, JavaExe As String, Instance As McInstance,
                               Optional OnProgress As Action(Of String) = Nothing,
                               Optional CancellationToken As Threading.CancellationToken = Nothing) As String
        Dim Root As String = GetRoot(Info)
        Dim Marker As String = $"{Info.LastPlayedTicks}|{Info.SizeBytes}"
        Dim MarkerFile As String = Root & "render.marker"
        If GetBoolSetting("BlueMapIncremental", True) AndAlso FileUtils.Exists(Root & "web\settings.json") AndAlso
           FileUtils.Exists(MarkerFile) AndAlso FileUtils.ReadAsString(MarkerFile) = Marker Then
            Logger.Info($"存档未变化，跳过 BlueMap 渲染：{Info.Path}")
            If OnProgress IsNot Nothing Then OnProgress("存档未变化，直接打开上次渲染的地图")
            Return Root & "web\"
        End If

        WriteConfigs(Info, Root, Instance)
        Dim VersionText As String = If(Instance?.Version?.VanillaName, "1.21.11")
        PrepareClientJar(Root, VersionText)
        Dim Args As String = $"-jar ""{BlueMapJarPath}"" -c ""{Root}config"" -v {VersionText} -r -g"
        ' 带模组的版本：让 BlueMap 从 mods 文件夹读取方块模型、材质与语言文件
        Dim ModsFolder As String = If(Instance Is Nothing OrElse String.IsNullOrEmpty(Instance.PathIndie), "", Instance.PathIndie & "mods")
        If ModsFolder <> "" AndAlso DirectoryUtils.Exists(ModsFolder) Then
            Args &= $" -n ""{ModsFolder}"""
        End If
        Logger.Info($"BlueMap 渲染：{Args}")

        Using P As New Process()
            P.StartInfo.FileName = JavaExe
            P.StartInfo.Arguments = Args
            P.StartInfo.WorkingDirectory = Root
            P.StartInfo.UseShellExecute = False
            P.StartInfo.CreateNoWindow = True
            P.StartInfo.RedirectStandardOutput = True
            P.StartInfo.RedirectStandardError = True
            AddHandler P.OutputDataReceived, Sub(s, e) HandleLine(e.Data, OnProgress)
            AddHandler P.ErrorDataReceived, Sub(s, e) HandleLine(e.Data, OnProgress)
            P.Start()
            P.BeginOutputReadLine()
            P.BeginErrorReadLine()
            While Not P.WaitForExit(300)
                If CancellationToken.IsCancellationRequested Then
                    Try
                        P.Kill()
                    Catch
                    End Try
                    Throw New System.OperationCanceledException()
                End If
            End While
            If P.ExitCode <> 0 Then Throw New Exception($"BlueMap 渲染失败（退出码 {P.ExitCode}），详情见日志")
        End Using
        FileUtils.Write(MarkerFile, Marker)
        Return Root & "web\"
    End Function

    Private Sub HandleLine(Line As String, OnProgress As Action(Of String))
        If String.IsNullOrWhiteSpace(Line) Then Return
        Logger.Info("[BlueMap] " & Line)
        If OnProgress Is Nothing Then Return
        If Line.Contains("updating map") Then
            ' 从百分号往前取数字，避免把行首时间戳里的数字当成进度
            Dim PercentMark As Integer = Line.LastIndexOf("%"c)
            If PercentMark > 0 Then
                Dim Start As Integer = PercentMark - 1
                While Start >= 0 AndAlso (Char.IsDigit(Line(Start)) OrElse Line(Start) = "."c)
                    Start -= 1
                End While
                Dim Text As String = Line.Substring(Start + 1, PercentMark - Start - 1).Trim("."c)
                If Text <> "" Then OnProgress($"正在渲染 3D 地图… {Text}%")
            End If
        ElseIf Line.Contains("Loading resources") Then
            OnProgress("正在加载材质与模型…")
        ElseIf Line.Contains("Downloading") Then
            OnProgress("正在下载 Minecraft 客户端资源…")
        End If
    End Sub

    ''' <summary>
    ''' 启动 BlueMap 自带网页服务器，返回访问地址；会先关掉之前启动的服务器。
    ''' </summary>
    Public Function StartServer(Info As ModSave.SaveInfo, JavaExe As String) As String
        Dim Root As String = GetRoot(Info)
        StopServer(Info.Path)
        CleanStaleServers()
        Dim Port As Integer = GetFreePort()
        FileUtils.Write(Root & "config\webserver.conf",
            "enabled: true" & vbCrLf &
            "webroot: ""web""" & vbCrLf &
            $"port: {Port}" & vbCrLf)

        Dim P As New Process()
        P.StartInfo.FileName = JavaExe
        P.StartInfo.Arguments = $"-jar ""{BlueMapJarPath}"" -c ""{Root}config"" -w"
        P.StartInfo.WorkingDirectory = Root
        P.StartInfo.UseShellExecute = False
        P.StartInfo.CreateNoWindow = True
        P.StartInfo.RedirectStandardOutput = True
        P.StartInfo.RedirectStandardError = True
        AddHandler P.OutputDataReceived, Sub(s, e) If Not String.IsNullOrEmpty(e.Data) Then Logger.Info("[BlueMap] " & e.Data)
        AddHandler P.ErrorDataReceived, Sub(s, e) If Not String.IsNullOrEmpty(e.Data) Then Logger.Info("[BlueMap] " & e.Data)
        P.Start()
        P.BeginOutputReadLine()
        P.BeginErrorReadLine()
        ServerProcesses(Info.Path) = P
        RegisterCleanup()

        Dim Deadline As DateTime = Now.AddSeconds(25)
        While Now < Deadline
            If P.HasExited Then Throw New Exception($"BlueMap 网页服务器启动失败（退出码 {P.ExitCode}）")
            Try
                Using Client As New TcpClient()
                    Client.Connect(IPAddress.Loopback, Port)
                    Logger.Info($"BlueMap 网页服务器已启动：http://127.0.0.1:{Port}/")
                    Return $"http://127.0.0.1:{Port}/"
                End Using
            Catch
                Threading.Thread.Sleep(200)
            End Try
        End While
        Throw New Exception("BlueMap 网页服务器启动超时")
    End Function

    Public Sub StopServer(SavePath As String)
        Dim P As Process = Nothing
        If ServerProcesses.TryGetValue(SavePath, P) Then
            Try
                If Not P.HasExited Then
                    P.Kill()
                    P.WaitForExit(3000)
                End If
            Catch
            End Try
            ServerProcesses.Remove(SavePath)
        End If
    End Sub

    ''' <summary>
    ''' 清理以前残留的 BlueMap 网页服务器进程（启动器异常退出时可能留下）。
    ''' </summary>
    Public Sub CleanStaleServers()
        Try
            Using Searcher As New ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'java.exe'")
                For Each Obj As ManagementObject In Searcher.Get()
                    Dim Cmd As String = TryCast(Obj("CommandLine"), String)
                    If String.IsNullOrEmpty(Cmd) Then Continue For
                    If Not Cmd.Contains("bluemap-") OrElse Not Cmd.Contains("PCL\Cache\BlueMap") Then Continue For
                    Dim TargetId As Integer = Convert.ToInt32(Obj("ProcessId"))
                    If ServerProcesses.Values.Any(Function(P) P.Id = TargetId) Then Continue For
                    Try
                        Process.GetProcessById(TargetId).Kill()
                        Logger.Info($"已清理残留的 BlueMap 服务进程：{TargetId}")
                    Catch
                    End Try
                Next
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "清理残留 BlueMap 进程失败")
        End Try
    End Sub

    Public Sub StopAllServers()
        For Each P In ServerProcesses.Values
            Try
                If Not P.HasExited Then
                    P.Kill()
                    P.WaitForExit(3000)
                End If
            Catch
            End Try
        Next
        ServerProcesses.Clear()
    End Sub

    Private Sub RegisterCleanup()
        If CleanupRegistered Then Return
        CleanupRegistered = True
        AddHandler AppDomain.CurrentDomain.ProcessExit, Sub() StopAllServers()
    End Sub

    Private Function GetFreePort() As Integer
        Dim Listener As New TcpListener(IPAddress.Loopback, 0)
        Listener.Start()
        Dim Port As Integer = DirectCast(Listener.LocalEndpoint, IPEndPoint).Port
        Listener.Stop()
        Return Port
    End Function

End Module

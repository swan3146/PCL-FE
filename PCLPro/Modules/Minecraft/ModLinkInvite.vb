Imports Newtonsoft.Json.Linq
Imports System.Text.RegularExpressions

Public Module ModLinkInvite

    Public Const InviteExtension As String = "pclinvite"
    Private Const InviteType As String = "pclfe-link-invite"
    Private Const InviteVersion As Integer = 1
    Private Const InviteMaxSize As Long = 4 * 1024 * 1024
    Private Const InviteSecret As String = "PCLFE::LinkInvite::9f1c7a4e5b2d8c63::2026"

    Public Class LinkInviteMod
        Public FileName As String = ""
        Public Sha1 As String = ""
        Public Size As Long = 0
        Public Url As String = ""
    End Class

    Public Class LinkInviteInfo
        Public RoomCode As String = ""
        Public Port As Integer = 0
        Public InstanceName As String = ""
        Public McVersion As String = ""
        Public LoaderText As String = ""
        Public LoaderType As String = ""
        Public LoaderVersion As String = ""
        Public CreatedAt As Long = 0
        Public Mods As New List(Of LinkInviteMod)

        Public ReadOnly Property VersionText As String
            Get
                Dim Text As String = If(String.IsNullOrEmpty(McVersion), "未知", McVersion)
                If Not String.IsNullOrEmpty(LoaderText) Then Text &= "（" & LoaderText & "）"
                Return Text
            End Get
        End Property
    End Class

    Public Function LinkInviteCreate(Instance As McInstance, RoomCode As String, Port As Integer) As LinkInviteInfo
        If Instance Is Nothing Then Throw New ArgumentNullException(NameOf(Instance))
        If Not Instance.IsLoaded Then Instance.Load()

        Dim Info As New LinkInviteInfo With {
            .RoomCode = If(RoomCode, ""),
            .Port = Port,
            .InstanceName = Instance.Name,
            .McVersion = Instance.Version.VanillaName,
            .LoaderText = LinkInviteGetLoaderText(Instance),
            .LoaderType = LinkInviteGetLoaderType(Instance),
            .LoaderVersion = LinkInviteGetLoaderVersion(Instance),
            .CreatedAt = GetTimeMs()
        }

        Dim ModsFolder As String = PathUtils.AddSlashSuffix(Instance.PathIndie & "mods")
        If DirectoryUtils.Exists(ModsFolder) Then
            For Each File In DirectoryUtils.EnumerateFiles(ModsFolder, False)
                If Not LinkInviteIsModFile(File) Then Continue For
                Dim Entry As New LinkInviteMod With {.FileName = PathUtils.GetLastPart(File)}
                Try
                    Dim Local As New LocalResourceFile(New IO.FileInfo(File))
                    Entry.Sha1 = Local.ModrinthHash
                    Entry.Size = New IO.FileInfo(File).Length
                Catch ex As Exception
                    Logger.Warn(ex, $"计算模组哈希失败：{File}")
                End Try
                Info.Mods.Add(Entry)
            Next
        End If
        Info.Mods = Info.Mods.OrderBy(Function(m) m.FileName).ToList()

        LinkInviteFillUrls(Info)
        Logger.Info($"联机邀请：已收集 {Info.Mods.Count} 个模组，房间码 {Info.RoomCode}，端口 {Info.Port}")
        Return Info
    End Function

    Private Function LinkInviteIsModFile(File As String) As Boolean
        Dim Extension As String = PathUtils.GetExtension(File)
        If {"disabled", "old"}.Contains(Extension) Then File = PathUtils.GetFileNameWithoutExtension(File)
        Return {"jar", "zip", "litemod"}.Contains(PathUtils.GetExtension(File))
    End Function

    Private Function LinkInviteGetLoaderText(Instance As McInstance) As String
        Dim Parts As New List(Of String)
        Dim Version = Instance.Version
        Try
            If Version.ModLoaders.HasFlagF(ModLoaders.Fabric) Then Parts.Add("Fabric " & Version.ModLoaderVersions(ModLoaders.Fabric))
            If Version.ModLoaders.HasFlagF(ModLoaders.Forge) Then Parts.Add("Forge " & Version.ModLoaderVersions(ModLoaders.Forge))
            If Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) Then Parts.Add("NeoForge " & Version.ModLoaderVersions(ModLoaders.NeoForge))
            If Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader) Then Parts.Add("LiteLoader")
        Catch ex As Exception
            Logger.Warn(ex, "读取加载器信息失败")
        End Try
        Return Parts.Join(" / ")
    End Function

    Private Function LinkInviteGetLoaderType(Instance As McInstance) As String
        Dim Version = Instance.Version
        Try
            If Version.ModLoaders.HasFlagF(ModLoaders.Fabric) Then Return "fabric"
            If Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) Then Return "neoforge"
            If Version.ModLoaders.HasFlagF(ModLoaders.Forge) Then Return "forge"
            If Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader) Then Return "liteloader"
        Catch ex As Exception
            Logger.Warn(ex, "读取加载器类型失败")
        End Try
        Return ""
    End Function

    Private Function LinkInviteGetLoaderVersion(Instance As McInstance) As String
        Dim Version = Instance.Version
        Try
            Select Case LinkInviteGetLoaderType(Instance)
                Case "fabric" : Return Version.ModLoaderVersions(ModLoaders.Fabric)
                Case "neoforge" : Return Version.ModLoaderVersions(ModLoaders.NeoForge)
                Case "forge" : Return Version.ModLoaderVersions(ModLoaders.Forge)
            End Select
        Catch ex As Exception
            Logger.Warn(ex, "读取加载器版本失败")
        End Try
        Return ""
    End Function

    Public Function LinkInvitePlanInstanceName(Info As LinkInviteInfo) As String
        If Not String.IsNullOrEmpty(Info.InstanceName) Then Return Info.InstanceName
        Select Case Info.LoaderType
            Case "fabric" : Return $"{Info.McVersion}-Fabric {Info.LoaderVersion}"
            Case "forge" : Return $"{Info.McVersion}-Forge_{Info.LoaderVersion}"
            Case "neoforge" : Return $"{Info.McVersion}-NeoForge_{Info.LoaderVersion}"
            Case Else : Return Info.McVersion
        End Select
    End Function

    Public Function LinkInviteBuildInstallRequest(Info As LinkInviteInfo, JsonUrl As String) As McInstallRequest
        Dim Name As String = LinkInvitePlanInstanceName(Info)
        Dim Loaders As New Dictionary(Of ModLoaders, String)
        Select Case Info.LoaderType
            Case "fabric"
                Loaders(ModLoaders.Fabric) = Info.LoaderVersion
            Case "forge"
                Loaders(ModLoaders.Forge) = Info.LoaderVersion
            Case "neoforge"
                Loaders(ModLoaders.NeoForge) = Info.LoaderVersion
            Case "liteloader"
                Loaders(ModLoaders.LiteLoader) = Info.LoaderVersion
        End Select
        Dim Components As New McComponents(Info.McVersion, Loaders)
        Return New McInstallRequest With {
            .NewInstanceName = Name,
            .InstanceFolder = $"{McFolderSelected}versions\{Name}\",
            .Components = Components,
            .MinecraftJson = JsonUrl
        }
    End Function

    Private Sub LinkInviteFillUrls(Info As LinkInviteInfo)
        Dim Hashes = Info.Mods.Where(Function(m) Not String.IsNullOrEmpty(m.Sha1)).Select(Function(m) m.Sha1).Distinct().ToList()
        If Not Hashes.Any Then Return
        Try
            Dim Requested As Integer = 0
            For Start As Integer = 0 To Hashes.Count - 1 Step 100
                Dim Chunk As List(Of String) = Hashes.Skip(Start).Take(100).ToList()
                Dim Body As String = New JObject From {
                    {"hashes", JArray.FromObject(Chunk)},
                    {"algorithm", "sha1"}
                }.ToString()
                Dim Response As JObject = TryCast(DlModRequest("https://api.modrinth.com/v2/version_files", HttpMethod.Post, Body, "application/json"), JObject)
                If Response Is Nothing Then Continue For
                Requested += 1
                For Each Entry In Info.Mods
                    If String.IsNullOrEmpty(Entry.Sha1) OrElse Not Chunk.Contains(Entry.Sha1) OrElse Not Response.ContainsKey(Entry.Sha1) Then Continue For
                    Dim Files = TryCast(Response(Entry.Sha1)("files"), JArray)
                    If Files Is Nothing OrElse Files.Count = 0 Then Continue For
                    For Each FileItem As JObject In Files
                        Dim Hash As String = FileItem("hashes")?("sha1")?.ToString
                        If Hash IsNot Nothing AndAlso Hash <> Entry.Sha1 Then Continue For
                        Entry.Url = FileItem("url")?.ToString
                        Exit For
                    Next
                Next
            Next
            Logger.Info($"联机邀请：{Info.Mods.Where(Function(m) Not String.IsNullOrEmpty(m.Url)).Count}/{Info.Mods.Count} 个模组已匹配到下载地址（{Requested} 批）")
        Catch ex As Exception
            Logger.Warn(ex, "获取模组下载地址失败，将仅提供文件名清单")
        End Try
    End Sub

    Public Sub LinkInviteSave(Info As LinkInviteInfo, Path As String)
        LinkInviteValidate(Info)
        Dim Json As New JObject From {
            {"type", InviteType},
            {"version", InviteVersion},
            {"room", Info.RoomCode},
            {"port", Info.Port},
            {"instance", Info.InstanceName},
            {"mc", Info.McVersion},
            {"loaders", Info.LoaderText},
            {"loaderType", Info.LoaderType},
            {"loaderVersion", Info.LoaderVersion},
            {"created", Info.CreatedAt},
            {"mods", JArray.FromObject(Info.Mods.Select(Function(m) New JObject From {
                {"name", m.FileName}, {"sha1", m.Sha1}, {"size", m.Size}, {"url", m.Url}
            }).ToList())}
        }
        Json("sign") = LinkInviteComputeSign(Info)
        FileUtils.Write(Path, Json.ToString(Newtonsoft.Json.Formatting.Indented))
        Logger.Info($"联机邀请文件已保存：{Path}")
    End Sub

    Public Function LinkInviteParse(Path As String) As LinkInviteInfo
        Dim FileSize As Long = New IO.FileInfo(Path).Length
        If FileSize > InviteMaxSize Then Throw New Exception("邀请文件体积异常，已拒绝加载。")
        Dim Json As JObject = JObject.Parse(FileUtils.ReadAsString(Path))
        If Json("type")?.ToString <> InviteType Then Throw New Exception("这不是 PCL FE 的联机邀请文件。")
        If Json("version")?.ToObject(Of Integer) <> InviteVersion Then Throw New Exception("邀请文件版本不受支持。")

        Dim Sign As String = If(Json("sign")?.ToString, "")
        If String.IsNullOrEmpty(Sign) Then Throw New Exception("邀请文件缺少签名，可能被修改或来源不可信，已拒绝加载。")

        Dim Port As Integer = 0
        Integer.TryParse(If(Json("port")?.ToString, "0"), Port)
        Dim Created As Long = 0
        Long.TryParse(If(Json("created")?.ToString, "0"), Created)
        Dim Info As New LinkInviteInfo With {
            .RoomCode = If(Json("room")?.ToString, ""),
            .Port = Port,
            .InstanceName = If(Json("instance")?.ToString, ""),
            .McVersion = If(Json("mc")?.ToString, ""),
            .LoaderText = If(Json("loaders")?.ToString, ""),
            .LoaderType = If(Json("loaderType")?.ToString, ""),
            .LoaderVersion = If(Json("loaderVersion")?.ToString, ""),
            .CreatedAt = Created
        }
        Dim Mods = TryCast(Json("mods"), JArray)
        If Mods IsNot Nothing Then
            For Each Item As JObject In Mods
                Dim FileName As String = LinkInviteSafeFileName(If(Item("name")?.ToString, ""))
                If String.IsNullOrEmpty(FileName) Then Continue For
                Dim Size As Long = 0
                Long.TryParse(If(Item("size")?.ToString, "0"), Size)
                Info.Mods.Add(New LinkInviteMod With {
                    .FileName = FileName,
                    .Sha1 = If(Item("sha1")?.ToString, ""),
                    .Size = Size,
                    .Url = If(Item("url")?.ToString, "")
                })
            Next
        End If

        If Not LinkInviteSignEquals(Sign, LinkInviteComputeSign(Info)) Then
            Throw New Exception("邀请文件校验失败：内容已被修改，已拒绝加载。")
        End If
        LinkInviteValidate(Info)
        Return Info
    End Function

    Private Function LinkInviteCanonical(Info As LinkInviteInfo) As String
        Dim Json As New JObject From {
            {"room", Info.RoomCode},
            {"port", Info.Port},
            {"instance", Info.InstanceName},
            {"mc", Info.McVersion},
            {"loaders", Info.LoaderText},
            {"loaderType", Info.LoaderType},
            {"loaderVersion", Info.LoaderVersion},
            {"created", Info.CreatedAt},
            {"mods", JArray.FromObject(Info.Mods.Select(Function(m) New JObject From {
                {"name", m.FileName}, {"sha1", m.Sha1}, {"size", m.Size}, {"url", m.Url}
            }).ToList())}
        }
        Return Json.ToString(Newtonsoft.Json.Formatting.None)
    End Function

    Private Function LinkInviteComputeSign(Info As LinkInviteInfo) As String
        Using Hmac As New Security.Cryptography.HMACSHA256(Text.Encoding.UTF8.GetBytes(InviteSecret))
            Dim Hash As Byte() = Hmac.ComputeHash(Text.Encoding.UTF8.GetBytes(LinkInviteCanonical(Info)))
            Return BitConverter.ToString(Hash).Replace("-", "").ToLowerInvariant()
        End Using
    End Function

    Private Function LinkInviteSignEquals(Left As String, Right As String) As Boolean
        If Left.Length <> Right.Length Then Return False
        Dim Diff As Integer = 0
        For i As Integer = 0 To Left.Length - 1
            Diff = Diff Or (AscW(Left(i)) Xor AscW(Right(i)))
        Next
        Return Diff = 0
    End Function

    Private Sub LinkInviteValidate(Info As LinkInviteInfo)
        If String.IsNullOrEmpty(Info.RoomCode) Then Throw New Exception("邀请文件缺少房间码。")
        If Not LinkInviteTextAllowed(Info.RoomCode, "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789/_-", 64) Then
            Throw New Exception("邀请文件中的房间码格式不正确。")
        End If
        If Info.Port < 0 OrElse Info.Port > 65535 Then Throw New Exception("邀请文件中的端口不正确。")
        If Not Regex.IsMatch(Info.McVersion, "^[0-9][0-9A-Za-z._-]{0,24}$") Then
            Throw New Exception("邀请文件中的 Minecraft 版本不正确。")
        End If
        If Array.IndexOf({"", "fabric", "forge", "neoforge", "liteloader"}, Info.LoaderType) < 0 Then
            Throw New Exception("邀请文件中的模组加载器类型不受支持。")
        End If
        If Not LinkInviteTextAllowed(Info.LoaderVersion, "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz._+-", 32) Then
            Throw New Exception("邀请文件中的模组加载器版本不正确。")
        End If
        If Info.InstanceName.Length > 64 OrElse Info.InstanceName.IndexOfAny(IO.Path.GetInvalidFileNameChars()) >= 0 OrElse
           Info.InstanceName.Contains("\") OrElse Info.InstanceName.Contains("/") Then
            Throw New Exception("邀请文件中的实例名包含非法字符。")
        End If
        For Each Item In Info.Mods
            If Not Regex.IsMatch(If(Item.Sha1, ""), "^[0-9a-f]{40}$") Then Throw New Exception("邀请文件中的模组哈希格式不正确。")
            If Item.Size < 0 OrElse Item.Size > 4294967296L Then Throw New Exception("邀请文件中的模组大小异常。")
            If Not String.IsNullOrEmpty(Item.Url) AndAlso Not LinkInviteUrlAllowed(Item.Url) Then
                Throw New Exception("邀请文件中的下载地址不在允许范围内。")
            End If
        Next
    End Sub

    Private Function LinkInviteTextAllowed(Text As String, Allowed As String, MaxLength As Integer) As Boolean
        If Text Is Nothing OrElse Text.Length > MaxLength Then Return False
        For Each CharItem In Text
            If Not Allowed.Contains(CharItem) Then Return False
        Next
        Return True
    End Function

    Private Function LinkInviteSafeFileName(FileName As String) As String
        If String.IsNullOrWhiteSpace(FileName) Then Return ""
        Dim Name As String = PathUtils.GetLastPart(FileName.Replace("/", "\"))
        If Name = "." OrElse Name = ".." Then Return ""
        For Each CharItem In IO.Path.GetInvalidFileNameChars()
            If Name.Contains(CharItem) Then Return ""
        Next
        If Not LinkInviteIsModFile(Name) Then Return ""
        Return Name
    End Function

    Private Function LinkInviteUrlAllowed(Url As String) As Boolean
        Try
            Dim Uri As New Uri(Url)
            If Uri.Scheme <> Uri.UriSchemeHttps Then Return False
            Dim Host As String = Uri.Host.ToLowerInvariant()
            Return Host = "cdn.modrinth.com" OrElse Host.EndsWith(".modrinth.com") OrElse
                   Host = "mcimirror.top" OrElse Host.EndsWith(".mcimirror.top")
        Catch
            Return False
        End Try
    End Function

    Public Function LinkInviteGetAllInstances() As List(Of McInstance)
        Dim All As New List(Of McInstance)
        For Each List In McInstanceList.Values
            All.AddRange(List)
        Next
        If Not All.Any AndAlso DirectoryUtils.Exists(McFolderSelected & "versions") Then
            For Each Folder In DirectoryUtils.GetInfo(McFolderSelected & "versions").GetDirectories
                Try
                    All.Add(New McInstance(Folder.Name))
                Catch
                End Try
            Next
        End If
        Return All
    End Function

    Public Function LinkInviteIsCompatible(Info As LinkInviteInfo, Candidate As McInstance) As Boolean
        Try
            If Not Candidate.IsLoaded Then Candidate.Load()
            If Candidate.Version.VanillaName <> Info.McVersion Then Return False
            If LinkInviteGetLoaderType(Candidate) <> Info.LoaderType Then Return False
            If Not String.IsNullOrEmpty(Info.LoaderVersion) Then
                If LinkInviteGetLoaderVersion(Candidate) <> Info.LoaderVersion Then Return False
            End If
            Return True
        Catch ex As Exception
            Logger.Warn(ex, $"联机邀请：检查版本兼容性失败 {Candidate.Name}")
            Return False
        End Try
    End Function

    Public Function LinkInviteFindInstance(Info As LinkInviteInfo) As McInstance
        Dim All As List(Of McInstance) = LinkInviteGetAllInstances()
        If Not String.IsNullOrEmpty(Info.InstanceName) Then
            Dim Matched = All.FirstOrDefault(Function(i) i.Name = Info.InstanceName AndAlso LinkInviteIsCompatible(Info, i))
            If Matched IsNot Nothing Then Return Matched
        End If
        Return All.FirstOrDefault(Function(i) LinkInviteIsCompatible(Info, i))
    End Function

    Public Sub LinkInviteWaitInstallFinished(Name As String, JsonPath As String, TimeoutSeconds As Integer, Optional Report As Action(Of String, Double, String) = Nothing)
        Dim TargetLoaderName As String = Name & " 安装"
        Dim InstallLoader As LoaderBase = Nothing
        For Retry As Integer = 1 To 50
            Try
                InstallLoader = LoaderTaskbar.FirstOrDefault(Function(l) l.Name = TargetLoaderName)
            Catch
                InstallLoader = Nothing
            End Try
            If InstallLoader IsNot Nothing Then Exit For
            Thread.Sleep(100)
        Next
        Dim Deadline As Date = Date.Now.AddSeconds(TimeoutSeconds)
        Do While Date.Now < Deadline
            If InstallLoader IsNot Nothing Then
                Select Case InstallLoader.State
                    Case LoadState.Finished
                        Exit Do
                    Case LoadState.Failed
                        Throw New Exception("版本安装失败：" & If(InstallLoader.Error?.Message, "未知错误"))
                    Case LoadState.Canceled
                        Throw New Exception("版本安装已被取消。")
                End Select
                Report?.Invoke($"正在安装 {Name}…", Math.Max(0, Math.Min(1, InstallLoader.Progress)), LinkInviteLoaderStage(InstallLoader))
            ElseIf FileUtils.Exists(JsonPath) Then
                Exit Do
            End If
            Thread.Sleep(500)
        Loop
        If Not FileUtils.Exists(JsonPath) Then Throw New Exception("版本安装超时，请稍后在下载页手动安装。")
        For Retry As Integer = 1 To 20
            If FileUtils.Exists(JsonPath) Then Exit For
            Thread.Sleep(250)
        Next
    End Sub

    Private Function LinkInviteLoaderStage(Loader As LoaderBase) As String
        Try
            Dim LoadersProperty = Loader.GetType().GetProperty("Loaders")
            Dim SubLoaders = TryCast(LoadersProperty?.GetValue(Loader), IEnumerable)
            If SubLoaders Is Nothing Then Return ""
            For Each SubItem As Object In SubLoaders
                Dim SubLoader = TryCast(SubItem, LoaderBase)
                If SubLoader Is Nothing OrElse SubLoader.State <> LoadState.Loading Then Continue For
                Dim Deeper As String = LinkInviteLoaderStage(SubLoader)
                Return If(String.IsNullOrEmpty(Deeper), SubLoader.Name, Deeper)
            Next
        Catch ex As Exception
            Logger.Warn(ex, "联机邀请：读取安装步骤失败")
        End Try
        Return ""
    End Function

    Public Function LinkInviteSync(Info As LinkInviteInfo, Instance As McInstance, Optional Progress As Action(Of String, Integer, Integer, Double) = Nothing) As List(Of String)
        Dim Unresolved As New List(Of String)
        Dim ModsFolder As String = PathUtils.AddSlashSuffix(Instance.PathIndie & "mods")
        DirectoryUtils.Create(ModsFolder)
        If Info.Mods.Any(Function(m) String.IsNullOrEmpty(m.Url)) Then
            Progress?.Invoke("正在批量查询缺失模组的下载地址…", 0, Info.Mods.Count, 0)
            LinkInviteFillUrls(Info)
        End If
        Dim Total As Integer = Info.Mods.Count, Index As Integer = 0
        For Each Entry In Info.Mods
            Index += 1
            If String.IsNullOrEmpty(Entry.FileName) Then Continue For
            Progress?.Invoke($"正在同步模组 {Index}/{Total}：{Entry.FileName}", Index, Total, (Index - 1) / Math.Max(1, Total))
            Dim Target As String = ModsFolder & Entry.FileName
            Try
                If FileUtils.Exists(Target) AndAlso LinkInviteFileMatches(Target, Entry) Then Continue For
                Dim Url As String = If(Entry.Url, "")
                If Not String.IsNullOrEmpty(Url) AndAlso Not LinkInviteUrlAllowed(Url) Then
                    Logger.Warn($"联机邀请：忽略不在允许范围内的下载地址 {Url}")
                    Url = ""
                End If
                If String.IsNullOrEmpty(Url) AndAlso Not String.IsNullOrEmpty(Entry.Sha1) Then Url = LinkInviteQueryUrl(Entry.Sha1)
                If String.IsNullOrEmpty(Url) Then
                    Unresolved.Add(Entry.FileName)
                    Continue For
                End If
                Progress?.Invoke($"正在下载：{Entry.FileName}", Index, Total, (Index - 1) / Math.Max(1, Total))
                NetDownloadByLoader(Url, Target)
                If Not LinkInviteFileMatches(Target, Entry) Then
                    Logger.Warn($"联机邀请：下载的模组校验失败 {Entry.FileName}")
                    Unresolved.Add(Entry.FileName)
                End If
            Catch ex As Exception
                Logger.Warn(ex, $"联机邀请：同步模组失败 {Entry.FileName}")
                Unresolved.Add(Entry.FileName)
            End Try
        Next
        Return Unresolved
    End Function

    Private Function LinkInviteFileMatches(Path As String, Entry As LinkInviteMod) As Boolean
        Try
            Dim Info As New IO.FileInfo(Path)
            If Not Info.Exists Then Return False
            If Entry.Size > 0 AndAlso Info.Length <> Entry.Size Then Return False
            If String.IsNullOrEmpty(Entry.Sha1) Then Return True
            Return New LocalResourceFile(Info).ModrinthHash = Entry.Sha1
        Catch
            Return False
        End Try
    End Function

    Private Function LinkInviteQueryUrl(Sha1 As String) As String
        Try
            Dim Response As JObject = TryCast(DlModRequest("https://api.modrinth.com/v2/version_file/" & Sha1 & "?algorithm=sha1"), JObject)
            If Response Is Nothing Then Return Nothing
            Dim Files = TryCast(Response("files"), JArray)
            If Files Is Nothing Then Return Nothing
            For Each FileItem As JObject In Files
                Dim Url As String = FileItem("url")?.ToString
                If FileItem("hashes")?("sha1")?.ToString = Sha1 AndAlso LinkInviteUrlAllowed(Url) Then Return Url
            Next
        Catch ex As Exception
            Logger.Warn(ex, $"联机邀请：查询模组下载地址失败 {Sha1}")
        End Try
        Return Nothing
    End Function

    Public Function LinkInviteDescribe(Info As LinkInviteInfo) As String
        Dim Text As New Text.StringBuilder()
        Text.AppendLine("房间码：" & If(String.IsNullOrEmpty(Info.RoomCode), "无（仅直连）", Info.RoomCode))
        Text.AppendLine("游戏端口：" & If(Info.Port > 0, Info.Port.ToString(), "未知"))
        Text.AppendLine("需要的版本：" & Info.VersionText)
        Text.AppendLine("模组数量：" & Info.Mods.Count & " 个")
        Return Text.ToString().TrimEnd()
    End Function

End Module

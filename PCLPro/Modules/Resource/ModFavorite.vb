Imports Newtonsoft.Json.Linq




Public Module ModFavorite

    Public Class FavoriteResource

        Public ProjectJson As JObject
        Public Type As ResourceTypes = ResourceTypes.Mod
        Public Folder As String = ""
        Private CachedProject As ResourceProject = Nothing

        Public Sub New(Source As ResourceProject, TargetType As ResourceTypes, Optional TargetFolder As String = "")
            ProjectJson = Source.ToJson()
            Type = TargetType
            Folder = If(TargetFolder, "")
            CachedProject = Source
        End Sub

        Public Sub New(Data As JObject)
            ProjectJson = CType(Data("Project"), JObject)
            If Data("Type") IsNot Nothing Then Type = Data("Type").ToObject(Of Integer)
            If Data("Folder") IsNot Nothing Then Folder = Data("Folder").ToString
        End Sub

        
        
        
        Public ReadOnly Property Project As ResourceProject
            Get
                If CachedProject Is Nothing AndAlso ProjectJson IsNot Nothing Then
                    CachedProject = New ResourceProject(CType(ProjectJson.DeepClone, JObject))
                End If
                Return CachedProject
            End Get
        End Property

        Public ReadOnly Property DisplayName As String
            Get
                Try
                    If Project Is Nothing Then Return "未知资源"
                    Return Project.TranslatedName
                Catch ex As Exception
                    Return "未知资源"
                End Try
            End Get
        End Property

        Public Function ToJson() As JObject
            Dim Result As New JObject
            Result("Project") = ProjectJson
            Result("Type") = CInt(Type)
            Result("Folder") = If(Folder, "")
            Return Result
        End Function

    End Class

#Region "类型规则"

    
    
    
    Public Function SupportsFavorite(TargetType As ResourceTypes) As Boolean
        Select Case TargetType
            Case ResourceTypes.Mod, ResourceTypes.Map, ResourceTypes.ResourcePack, ResourceTypes.Shader, ResourceTypes.DataPack
                Return True
            Case Else
                Return False
        End Select
    End Function

    
    
    
    Public Function NeedsModLoader(TargetType As ResourceTypes) As Boolean
        Return TargetType = ResourceTypes.Mod
    End Function

    
    
    
    Public Function GetTargetFolder(TargetType As ResourceTypes) As String
        Select Case TargetType
            Case ResourceTypes.Mod : Return "mods\"
            Case ResourceTypes.Map : Return "saves\"
            Case ResourceTypes.ResourcePack : Return "resourcepacks\"
            Case ResourceTypes.Shader : Return "shaderpacks\"
            Case ResourceTypes.DataPack : Return "datapacks\"
            Case Else : Return ""
        End Select
    End Function

    Public Function GetTypeName(TargetType As ResourceTypes) As String
        Select Case TargetType
            Case ResourceTypes.Mod : Return "Mod"
            Case ResourceTypes.Map : Return "地图"
            Case ResourceTypes.ResourcePack : Return "资源包"
            Case ResourceTypes.Shader : Return "光影包"
            Case ResourceTypes.DataPack : Return "数据包"
            Case Else : Return "资源"
        End Select
    End Function

    
    
    
    Private Function IsSameItem(Item As FavoriteResource, Project As ResourceProject, TargetType As ResourceTypes) As Boolean
        If Item Is Nothing OrElse Item.ProjectJson Is Nothing Then Return False
        If Item.ProjectJson("Id") Is Nothing Then Return False
        If Item.ProjectJson("Platform") Is Nothing Then Return False
        If Item.Type <> TargetType Then Return False
        Return Item.ProjectJson("Id").ToString = Project.Id AndAlso
               Item.ProjectJson("Platform").ToObject(Of Integer) = CInt(Project.Platform)
    End Function

#End Region

    Public ReadOnly FavoriteList As New List(Of FavoriteResource)
    Public ReadOnly FolderList As New List(Of String)
    Private ReadOnly FavoriteLock As New Object
    Private IsLoaded As Boolean = False

    Public ReadOnly Property FavoriteFilePath As String
        Get
            Return Paths.Base & "PCL\Favorites\FavoriteResources.json"
        End Get
    End Property

    Private ReadOnly Property LegacyFavoriteFilePath As String
        Get
            Return Paths.Base & "PCL\Favorites\FavoriteMods.json"
        End Get
    End Property

#Region "存取"

    Public Sub LoadFavorites()
        SyncLock FavoriteLock
            If IsLoaded Then Return
            IsLoaded = True
            Try
                If FileUtils.Exists(FavoriteFilePath) Then
                    ReadItemsFrom(FileUtils.ReadAsJson(FavoriteFilePath))
                ElseIf FileUtils.Exists(LegacyFavoriteFilePath) Then
                    
                    ReadItemsFrom(FileUtils.ReadAsJson(LegacyFavoriteFilePath))
                    For Each Item In FavoriteList
                        Item.Type = ResourceTypes.Mod
                    Next
                    SaveFavorites()
                Else
                    Return
                End If
                Logger.Info($"已加载 {FavoriteList.Count} 个收藏的资源")
            Catch ex As Exception
                Logger.Warn(ex, "读取收藏的资源列表失败")
            End Try
        End SyncLock
    End Sub

    Private Sub ReadItemsFrom(Json As JObject)
        FavoriteList.Clear()
        FolderList.Clear()
        Dim Folders = TryCast(Json("Folders"), JArray)
        If Folders IsNot Nothing Then
            For Each Token As JToken In Folders
                Dim Name As String = If(Token.ToString, "").Trim
                If Name <> "" AndAlso Not FolderList.Any(Function(f) String.Equals(f, Name, StringComparison.OrdinalIgnoreCase)) Then
                    FolderList.Add(Name)
                End If
            Next
        End If
        Dim Items = TryCast(Json("Items"), JArray)
        If Items Is Nothing Then Return
        For Each Item As JToken In Items
            Try
                Dim NewItem As New FavoriteResource(CType(Item, JObject))
                If NewItem.ProjectJson Is Nothing Then Continue For
                If NewItem.ProjectJson("Id") Is Nothing Then Continue For
                FavoriteList.Add(NewItem)
            Catch ex As Exception
                Logger.Warn(ex, "读取单个收藏的资源时出错")
            End Try
        Next
    End Sub

    Public Sub SaveFavorites()
        SyncLock FavoriteLock
            Try
                Dim Json As New JObject
                Dim Items As New JArray
                For Each Favorite In FavoriteList
                    Items.Add(Favorite.ToJson())
                Next
                Json("Items") = Items
                Dim Folders As New JArray
                For Each Name In FolderList
                    Folders.Add(Name)
                Next
                Json("Folders") = Folders
                DirectoryUtils.Create(PathUtils.RemoveLastPart(FavoriteFilePath))
                FileUtils.Write(FavoriteFilePath, Json.ToString(Newtonsoft.Json.Formatting.Indented))
            Catch ex As Exception
                Logger.Warn(ex, "保存收藏的资源列表失败")
            End Try
        End SyncLock
    End Sub

    Public Function GetFavorites(Optional TargetType As ResourceTypes? = Nothing) As List(Of FavoriteResource)
        LoadFavorites()
        SyncLock FavoriteLock
            If TargetType Is Nothing Then Return FavoriteList.ToList()
            Return FavoriteList.Where(Function(f) f.Type = TargetType.Value).ToList()
        End SyncLock
    End Function

#End Region

#Region "自定义收藏夹"

    Public Function GetFolders() As List(Of String)
        LoadFavorites()
        SyncLock FavoriteLock
            Return FolderList.ToList()
        End SyncLock
    End Function

    Public Function IsFolderNameValid(Name As String) As Boolean
        Name = If(Name, "").Trim
        If Name = "" Then Return False
        If Name = "全部收藏" OrElse Name = "未分类" Then Return False
        Return True
    End Function

    Public Function AddFolder(Name As String) As Integer
        Name = If(Name, "").Trim
        If Not IsFolderNameValid(Name) Then Return 0
        LoadFavorites()
        SyncLock FavoriteLock
            If FolderList.Any(Function(f) String.Equals(f, Name, StringComparison.OrdinalIgnoreCase)) Then Return -1
            FolderList.Add(Name)
        End SyncLock
        SaveFavorites()
        Logger.Info($"新建收藏夹：{Name}")
        Return 1
    End Function

    Public Function RenameFolder(OldName As String, NewName As String) As Integer
        NewName = If(NewName, "").Trim
        If Not IsFolderNameValid(NewName) Then Return 0
        LoadFavorites()
        SyncLock FavoriteLock
            Dim Index As Integer = FolderList.FindIndex(Function(f) String.Equals(f, OldName, StringComparison.OrdinalIgnoreCase))
            If Index < 0 Then Return 0
            If Not String.Equals(OldName, NewName, StringComparison.OrdinalIgnoreCase) AndAlso
               FolderList.Any(Function(f) String.Equals(f, NewName, StringComparison.OrdinalIgnoreCase)) Then Return -1
            FolderList(Index) = NewName
            For Each Item In FavoriteList
                If String.Equals(Item.Folder, OldName, StringComparison.OrdinalIgnoreCase) Then Item.Folder = NewName
            Next
        End SyncLock
        SaveFavorites()
        Logger.Info($"重命名收藏夹：{OldName} → {NewName}")
        Return 1
    End Function

    Public Sub DeleteFolder(Name As String)
        LoadFavorites()
        SyncLock FavoriteLock
            FolderList.RemoveAll(Function(f) String.Equals(f, Name, StringComparison.OrdinalIgnoreCase))
            For Each Item In FavoriteList
                If String.Equals(Item.Folder, Name, StringComparison.OrdinalIgnoreCase) Then Item.Folder = ""
            Next
        End SyncLock
        SaveFavorites()
        Logger.Info($"删除收藏夹：{Name}，其中的收藏已移动到未分类")
    End Sub

    Public Sub MoveToFolder(Item As FavoriteResource, TargetFolder As String)
        If Item Is Nothing Then Return
        LoadFavorites()
        SyncLock FavoriteLock
            Item.Folder = If(TargetFolder, "")
        End SyncLock
        SaveFavorites()
        Logger.Info($"移动收藏：{Item.DisplayName} → {If(String.IsNullOrEmpty(TargetFolder), "未分类", TargetFolder)}")
    End Sub

#End Region

#Region "收藏状态"

    Public Function IsFavorite(Project As ResourceProject, TargetType As ResourceTypes) As Boolean
        If Project Is Nothing Then Return False
        LoadFavorites()
        SyncLock FavoriteLock
            Return FavoriteList.Any(Function(f) IsSameItem(f, Project, TargetType))
        End SyncLock
    End Function

    
    
    
    Public Function ToggleFavorite(Project As ResourceProject, TargetType As ResourceTypes) As Boolean
        If Project Is Nothing Then Return False
        LoadFavorites()
        Dim Result As Boolean
        SyncLock FavoriteLock
            Dim Existing = FavoriteList.FirstOrDefault(Function(f) IsSameItem(f, Project, TargetType))
            If Existing Is Nothing Then
                FavoriteList.Add(New FavoriteResource(Project, TargetType))
                Result = True
                Logger.Info($"已收藏{GetTypeName(TargetType)}：{Project.RawName}（{Project.Platform}，{Project.Id}）")
            Else
                FavoriteList.Remove(Existing)
                Result = False
                Logger.Info($"已取消收藏{GetTypeName(TargetType)}：{Project.RawName}（{Project.Platform}，{Project.Id}）")
            End If
        End SyncLock
        SaveFavorites()
        Return Result
    End Function

#End Region

#Region "批量下载"

    Private Function IsGameVersionMatch(File As ResourceVersion, TargetVersion As String) As Boolean
        If File.GameVersions Is Nothing OrElse Not File.GameVersions.Any() Then Return True
        If File.GameVersions.Any(Function(v) v Is Nothing OrElse v.Contains("未知")) Then Return True
        Return File.GameVersions.Any(Function(v) v = TargetVersion OrElse v.StartsWithF(TargetVersion & " "))
    End Function

    Private Function SelectBestFile(Files As List(Of ResourceVersion), TargetVersion As String, TargetLoaders As ModLoaders, TargetType As ResourceTypes) As ResourceVersion
        If Files Is Nothing OrElse Not Files.Any() Then Return Nothing
        Dim Candidates = Files.Where(Function(f) f.DownloadAvailable AndAlso IsGameVersionMatch(f, TargetVersion))
        
        If NeedsModLoader(TargetType) Then
            Candidates = Candidates.Where(Function(f) f.ModLoaders = ModLoaders.None OrElse
                                                     f.ModLoaders.Flags.Intersect(TargetLoaders.Flags).Any)
        End If
        Dim CandidateList = Candidates.ToList()
        If Not CandidateList.Any() Then Return Nothing
        
        Dim Scored = CandidateList.Select(Function(f) New With {
            .File = f,
            .LoaderScore = If(f.ModLoaders = ModLoaders.None, 0, f.ModLoaders.Flags.Intersect(TargetLoaders.Flags).Count()),
            .VersionScore = If(f.GameVersions IsNot Nothing AndAlso f.GameVersions.Any(Function(v) v = TargetVersion), 1, 0),
            .ReleaseScore = If(f.ReleaseType = ResourceVersion.ReleaseTypes.Release, 1, 0)
        })
        Return Scored.OrderByDescending(Function(s) s.LoaderScore).
            ThenByDescending(Function(s) s.VersionScore).
            ThenByDescending(Function(s) s.ReleaseScore).
            ThenByDescending(Function(s) s.File.ReleaseDate).First.File
    End Function

    Private Function GetDependencyFiles(Project As ResourceProject, File As ResourceVersion,
                                        TargetVersion As String, TargetLoaders As ModLoaders, TargetFolder As String) As List(Of Tuple(Of ResourceProject, ResourceVersion))
        Dim Result As New List(Of Tuple(Of ResourceProject, ResourceVersion))
        If File.Dependencies Is Nothing OrElse Not File.Dependencies.Any() Then Return Result
        For Each DepId As String In File.Dependencies
            Try
                Dim DepProject = ResourceProject.FromProjectId(DepId, Project.Platform)
                Dim DepFile = SelectBestFile(ResourceVersion.FromProjectId(DepId, Project.Platform), TargetVersion, TargetLoaders, ResourceTypes.Mod)
                If DepFile Is Nothing Then Continue For
                If FileUtils.Exists(TargetFolder & DepFile.FileName) Then Continue For
                If Result.Any(Function(d) d.Item2.FileName = DepFile.FileName) Then Continue For
                Result.Add(New Tuple(Of ResourceProject, ResourceVersion)(DepProject, DepFile))
            Catch ex As Exception
                Logger.Warn(ex, $"获取前置 {DepId} 的下载信息失败")
            End Try
        Next
        Return Result
    End Function

    
    
    
    Public Sub DownloadFavoritesToInstance(Items As List(Of FavoriteResource), Instance As McInstance, TargetType As ResourceTypes)
        If Items Is Nothing OrElse Not Items.Any() OrElse Instance Is Nothing Then Return
        If Not SupportsFavorite(TargetType) Then
            RunInUi(Sub() Hint($"{GetTypeName(TargetType)}暂不支持一键下载。", HintType.Red))
            Return
        End If
        Dim TypeName As String = GetTypeName(TargetType)
        Logger.Info($"准备下载 {Items.Count} 个收藏的{TypeName}到版本 {Instance.Name}")
        RunInUi(Sub() Hint(If(TargetType = ResourceTypes.Mod, "正在寻找最新版本……", $"正在寻找最新的{TypeName}版本……"), HintType.Green))
        RunInThread(
        Sub()
            Try
                If Not Instance.IsLoaded Then Instance.Load()
                Dim TargetVersion As String = Instance.Version.VanillaName
                Dim TargetLoaders As ModLoaders = Instance.Version.ModLoaders
                If NeedsModLoader(TargetType) AndAlso Not TargetLoaders.Flags.Any() Then
                    RunInUi(Sub() Hint($"版本 {Instance.Name} 没有安装 Mod 加载器（Forge / NeoForge / Fabric 等），无法安装 Mod。请选择带加载器的版本。", HintType.Red))
                    Logger.Warn($"跳过下载：版本 {Instance.Name} 不含 Mod 加载器")
                    Return
                End If
                Dim TargetFolder As String = Instance.PathIndie & GetTargetFolder(TargetType)
                DirectoryUtils.Create(TargetFolder)
                Dim Files As New List(Of NetFile)
                Dim Planned As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                Dim Failed As New List(Of String)
                Dim Skipped As Integer = 0
                For Each Item In Items
                    Try
                        Dim Project = Item.Project
                        Dim Target = SelectBestFile(ResourceVersion.FromProjectId(Project.Id, Project.Platform), TargetVersion, TargetLoaders, TargetType)
                        If Target Is Nothing Then
                            Failed.Add(Item.DisplayName & "（无适用版本）")
                            Continue For
                        End If
                        If FileUtils.Exists(TargetFolder & Target.FileName) Then
                            Skipped += 1
                            Logger.Info($"已存在，跳过下载：{Target.FileName}")
                            Continue For
                        End If
                        Logger.Info($"已选择文件：{Project.RawName} → {Target.FileName}（文件加载器：{Target.ModLoaders}，目标：{TargetVersion} / {TargetLoaders} / {TypeName}）")
                        Files.Add(Target.ToNetFile(TargetFolder & Target.FileName, ResourceVersion.DownloadReason.Standalone, TargetVersion, TargetLoaders))
                        Planned.Add(Target.FileName)
                        If TargetType = ResourceTypes.Mod Then
                            For Each Dep In GetDependencyFiles(Project, Target, TargetVersion, TargetLoaders, TargetFolder)
                                If Not Planned.Add(Dep.Item2.FileName) Then Continue For
                                Files.Add(Dep.Item2.ToNetFile(TargetFolder & Dep.Item2.FileName, ResourceVersion.DownloadReason.Dependency))
                                Logger.Info($"一并下载前置：{Dep.Item1.RawName}（{Dep.Item2.FileName}）")
                            Next
                        End If
                    Catch ex As Exception
                        Logger.Warn(ex, $"获取收藏{TypeName} {Item.DisplayName} 的下载信息失败")
                        Failed.Add(Item.DisplayName & "（获取失败）")
                    End Try
                Next

                RunInUi(
                Sub()
                    Try
                        If Not Files.Any() Then
                            Dim Message As String = $"没有需要下载的{TypeName}。"
                            If Skipped > 0 Then Message &= vbCrLf & $"其中 {Skipped} 个已存在于目标文件夹。"
                            If Failed.Any() Then Message &= vbCrLf & "未找到适用版本：" & Failed.Join("、")
                            Hint(Message, If(Failed.Any(), HintType.Red, HintType.Green))
                            Logger.Warn($"收藏{TypeName}无需下载：{Message}")
                            Return
                        End If
                        Dim TaskName As String = $"下载收藏的{TypeName}（{Instance.Name}）"
                        Dim Loaders As New List(Of LoaderBase) From {
                            New LoaderDownload($"下载{TypeName}文件", Files) With {.Block = True}
                        }
                        Dim Loader As New LoaderCombo(Of Integer)(TaskName, Loaders) With {.OnStateChanged =
                        Sub(MyLoader)
                            LoaderStateChangedHintOnly(MyLoader)
                            If MyLoader.State = LoadState.Finished Then
                                Dim Message As String = $"已下载 {Items.Count - Failed.Count - Skipped} 个收藏的{TypeName}到 {Instance.Name}"
                                If Skipped > 0 Then Message &= $"，跳过 {Skipped} 个已存在的文件"
                                If Failed.Any() Then Message &= $"，{Failed.Count} 个失败：{Failed.Join("、")}"
                                Hint(Message, If(Failed.Any(), HintType.Blue, HintType.Green))
                            End If
                        End Sub}
                        Loader.Start(1)
                        LoaderTaskbarAdd(Loader)
                        FrmMain.BtnExtraDownload.ShowRefresh()
                        FrmMain.BtnExtraDownload.Ribble()
                    Catch ex As Exception
                        Logger.Error(ex, $"启动收藏{TypeName}下载任务失败")
                    End Try
                End Sub)
            Catch ex As Exception
                Logger.Error(ex, $"准备收藏{TypeName}下载任务失败")
                RunInUi(Sub() Hint($"下载收藏的{TypeName}失败：" & ex.Message, HintType.Red))
            End Try
        End Sub)
    End Sub

#End Region

End Module

Public Class PageDownloadFavorite

    Private ReadOnly FavoriteChecks As New List(Of (Check As MyCheckBox, Item As ModFavorite.FavoriteResource))
    Private SuppressCheckEvent As Boolean = False
    Private CurType As ResourceTypes = ResourceTypes.Mod
    Private AllInstances As New List(Of McInstance)
    Private AvailableInstances As New List(Of McInstance)
    Private SelectedInstance As McInstance = Nothing

    Private Sub PageEnterRefresh() Handles Me.PageEnter
        RefreshList()
    End Sub

    Public Sub New()
        InitializeComponent()
        UpdateTypeTexts()
    End Sub

    Public Sub RefreshList()
        BuildItems()
        RefreshInstances()
    End Sub

#Region "类型"

    Private Sub TabType_Check(sender As Object, e As RouteEventArgs) Handles TabMod.Check, TabMap.Check, TabResourcePack.Check, TabShader.Check, TabDataPack.Check
        Dim NewType As ResourceTypes
        Select Case True
            Case sender Is TabMap : NewType = ResourceTypes.Map
            Case sender Is TabResourcePack : NewType = ResourceTypes.ResourcePack
            Case sender Is TabShader : NewType = ResourceTypes.Shader
            Case sender Is TabDataPack : NewType = ResourceTypes.DataPack
            Case Else : NewType = ResourceTypes.Mod
        End Select
        If NewType = CurType Then Return
        CurType = NewType
        Logger.Info($"收藏页切换到类型：{ModFavorite.GetTypeName(CurType)}")
        UpdateTypeTexts()
        BuildItems()
        FillInstances()
    End Sub

    Private Sub UpdateTypeTexts()
        Dim TypeName As String = ModFavorite.GetTypeName(CurType)
        CardItems.Title = $"我收藏的{TypeName}"
        If CurType = ResourceTypes.Mod Then
            LabTip.Text = "这是一个一键下载收藏mod到上方所选版本的小功能 默认下载最新版本和依赖哦"
            HintNoInstance.Text = "当前没有可安装 Mod 的游戏版本。请先在版本管理中安装或导入一个带有 Forge / NeoForge / Fabric 等 Mod 加载器的版本，原版版本无法安装 Mod。"
        Else
            LabTip.Text = $"这是一个一键下载收藏的{TypeName}到上方所选版本的小功能 默认下载最新版本哦"
            HintNoInstance.Text = "当前没有可用的游戏版本，请先在版本管理中安装或导入一个游戏版本。"
        End If
        HintEmpty.Text = $"还没有收藏的{TypeName}。在{TypeName}下载列表里点击右侧的心形按钮即可收藏，收藏后会出现在这里，可以一键下载到任意版本。"
    End Sub

#End Region

#Region "列表"

    Private Sub BuildItems()
        Try
            Dim Favorites = ModFavorite.GetFavorites(CurType)
            PanItems.Children.Clear()
            FavoriteChecks.Clear()
            HintEmpty.Visibility = If(Favorites.Any(), Visibility.Collapsed, Visibility.Visible)

            For Each Item In Favorites
                Dim Project = Item.Project
                If Project Is Nothing Then Continue For

                Dim RowBorder As New Border With {
                    .CornerRadius = New CornerRadius(5),
                    .Margin = New Thickness(6, 2, 6, 2),
                    .Padding = New Thickness(4, 4, 6, 4),
                    .MinHeight = 54
                }
                RowBorder.SetResourceReference(Border.BackgroundProperty, "ColorBrush8")

                Dim Row As New Grid
                Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(26)})
                Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(46)})
                Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
                Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
                Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

                Dim Check As New MyCheckBox With {
                    .VerticalAlignment = VerticalAlignment.Center,
                    .HorizontalAlignment = HorizontalAlignment.Left,
                    .Margin = New Thickness(2, 0, 4, 0)
                }
                Check.Checked = True
                AddHandler Check.Change, AddressOf Check_Change
                Row.Children.Add(Check)

                Dim LogoBorder As New Border With {
                    .Width = 40,
                    .Height = 40,
                    .CornerRadius = New CornerRadius(5),
                    .VerticalAlignment = VerticalAlignment.Center,
                    .Margin = New Thickness(0, 0, 8, 0),
                    .Clip = New RectangleGeometry(New Rect(0, 0, 40, 40), 5, 5)
                }
                Grid.SetColumn(LogoBorder, 1)
                Dim Img As New MyImage
                Project.ApplyLogoToMyImage(Img)
                LogoBorder.Child = Img
                Row.Children.Add(LogoBorder)

                Dim PanText As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center, .Margin = New Thickness(0, 2, 8, 2)}
                Grid.SetColumn(PanText, 2)
                Dim Titles = Project.GetControlTitle(True)
                Dim LabName As New TextBlock With {
                    .Text = Titles.Title,
                    .FontSize = 13,
                    .TextTrimming = TextTrimming.CharacterEllipsis,
                    .VerticalAlignment = VerticalAlignment.Center
                }
                LabName.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
                PanText.Children.Add(LabName)
                Dim DescText As String = If(Project.Description, "").ReplaceLineEndings(" ").Trim
                If DescText <> "" Then
                    Dim LabDesc As New TextBlock With {
                        .Text = DescText,
                        .FontSize = 11,
                        .Margin = New Thickness(0, 2, 0, 0),
                        .TextTrimming = TextTrimming.CharacterEllipsis,
                        .VerticalAlignment = VerticalAlignment.Center
                    }
                    LabDesc.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3")
                    PanText.Children.Add(LabDesc)
                End If
                Row.Children.Add(PanText)

                Dim PlatformName As String = If(Project.Platform = ResourcePlatforms.CurseForge, "CurseForge", "Modrinth")
                Dim LabSource As New TextBlock With {
                    .Text = PlatformName,
                    .FontSize = 11,
                    .VerticalAlignment = VerticalAlignment.Center,
                    .Margin = New Thickness(4, 0, 6, 0)
                }
                LabSource.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4")
                Grid.SetColumn(LabSource, 3)
                Row.Children.Add(LabSource)

                Dim BtnRemove As New MyIconButton With {
                    .Logo = Logo.IconButtonDelete,
                    .LogoScale = 0.8,
                    .Width = 26,
                    .Height = 26,
                    .Theme = MyIconButton.Themes.Red,
                    .VerticalAlignment = VerticalAlignment.Center,
                    .HorizontalAlignment = HorizontalAlignment.Right,
                    .ToolTip = "取消收藏",
                    .Margin = New Thickness(2, 0, 0, 0)
                }
                Grid.SetColumn(BtnRemove, 4)
                AddHandler BtnRemove.Click, Sub(sender As Object, e As EventArgs) RemoveFavorite(Project, Titles.Title)
                Row.Children.Add(BtnRemove)

                RowBorder.ToolTip = $"{Project.RawName}（{PlatformName}）"
                RowBorder.Child = Row
                AddHandler RowBorder.PreviewMouseLeftButtonUp, Sub(sender As Object, e As MouseButtonEventArgs)
                                                                    Dim Source = TryCast(e.OriginalSource, DependencyObject)
                                                                    If Source IsNot Nothing AndAlso
                                                                       (Check.IsAncestorOf(Source) OrElse Check Is Source OrElse
                                                                        BtnRemove.IsAncestorOf(Source) OrElse BtnRemove Is Source) Then Return
                                                                    OpenDetail(Project)
                                                                End Sub
                PanItems.Children.Add(RowBorder)
                FavoriteChecks.Add((Check, Item))
            Next

            SuppressCheckEvent = True
            Try
                CheckAll.Checked = FavoriteChecks.Any()
            Finally
                SuppressCheckEvent = False
            End Try
            UpdateSummary()
            Logger.Info($"收藏页：{ModFavorite.GetTypeName(CurType)} 共 {Favorites.Count} 项")

        Catch ex As Exception
            Logger.Error(ex, "加载收藏的列表失败", LogBehavior.Toast)
        End Try
    End Sub

    Private Sub RemoveFavorite(Project As ResourceProject, Title As String)
        Try
            ModFavorite.ToggleFavorite(Project, CurType)
            Hint($"已取消收藏：{Title}", HintType.Green)
            BuildItems()
        Catch ex As Exception
            Logger.Error(ex, "取消收藏失败")
            Hint("取消收藏失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    
    
    
    Private Sub OpenDetail(Project As ResourceProject)
        Try
            Dim TargetVersion As String = Nothing
            Dim TargetLoaders As ModLoaders = ModLoaders.None
            If SelectedInstance IsNot Nothing Then
                TargetVersion = SelectedInstance.Version.VanillaName
                TargetLoaders = SelectedInstance.Version.ModLoaders
            End If
            Logger.Info($"从收藏页打开资源详情：{Project.RawName}（{CurType}）")
            FrmMain.PageChange(New FormMain.PageStackData With {
                .Page = FormMain.PageType.ResourceDetail,
                .Additional = {Project, New List(Of String), TargetVersion, TargetLoaders, CurType}})
        Catch ex As Exception
            Logger.Error(ex, "打开资源详情失败")
            Hint("打开资源详情失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

#End Region

#Region "版本与选择"

    Private Sub RefreshInstances()
        RunInThread(
        Sub()
            Try
                If McInstanceListLoader.State <> LoadState.Finished Then
                    RunInUi(Sub() Hint("正在查找可用的游戏版本……"))
                    LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\", WaitForExit:=True)
                End If
                Dim Instances = McInstanceList.Values.SelectMany(Function(l) l).
                    GroupBy(Function(i) i.Name).Select(Function(g) g.First()).
                    OrderByDescending(Function(i) i.ReleaseTime).ToList()
                RunInUi(Sub()
                            AllInstances = Instances
                            FillInstances()
                        End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "加载游戏版本列表失败")
                RunInUi(Sub()
                            AllInstances = New List(Of McInstance)
                            FillInstances()
                        End Sub)
            End Try
        End Sub)
    End Sub

    
    
    
    Private Sub FillInstances()
        Try
            Dim RequireLoader As Boolean = ModFavorite.NeedsModLoader(CurType)
            AvailableInstances = AllInstances.Where(Function(i) Not RequireLoader OrElse i.Version.ModLoaders.Flags.Any()).ToList()

            
            If SelectedInstance IsNot Nothing Then
                Dim StillAvailable = AvailableInstances.FirstOrDefault(Function(i) i.Name = SelectedInstance.Name)
                SelectedInstance = If(StillAvailable IsNot Nothing, StillAvailable, Nothing)
            End If

            HintNoInstance.Visibility = If(AvailableInstances.Any(), Visibility.Collapsed, Visibility.Visible)
            BtnChangeInstance.IsEnabled = AvailableInstances.Any()
            UpdateInstanceText()
        Catch ex As Exception
            Logger.Warn(ex, "显示游戏版本列表失败")
        End Try
    End Sub

    Private Sub UpdateInstanceText()
        If SelectedInstance Is Nothing Then
            LabInstance.Text = "请选择要将资源下载到的版本"
            LabInstance.Opacity = 0.6
        Else
            Dim VersionText As String = SelectedInstance.Version.VanillaName
            Dim LoaderText As String = If(SelectedInstance.Version.ModLoaders = ModLoaders.None, "原版", SelectedInstance.Version.ModLoaders.ToString)
            LabInstance.Text = $"{SelectedInstance.Name}（{VersionText}，{LoaderText}）"
            LabInstance.Opacity = 1
        End If
        UpdateSummary()
    End Sub

    Private Sub BtnChangeInstance_Click(sender As Object, e As EventArgs) Handles BtnChangeInstance.Click
        ChooseInstance()
    End Sub

    
    
    
    Private Sub ChooseInstance()
        If Not AvailableInstances.Any() Then
            Hint("当前没有可用的游戏版本", HintType.Red)
            Return
        End If
        Try
            Dim Choices As New List(Of IMyRadio)
            For Each Instance In AvailableInstances
                Dim VersionText As String = Instance.Version.VanillaName
                Dim LoaderText As String = If(Instance.Version.ModLoaders = ModLoaders.None, "原版", Instance.Version.ModLoaders.ToString)
                Choices.Add(New MyRadioBox With {
                    .Text = $"{Instance.Name}（{VersionText}，{LoaderText}）",
                    .Checked = Instance Is SelectedInstance
                })
            Next
            Dim Result As Integer? = MyMsgBoxSelect(Choices, "选择下载到的版本", "确定", "取消")
            If Result Is Nothing Then Return
            If Result.Value < 0 OrElse Result.Value >= AvailableInstances.Count Then Return
            SelectedInstance = AvailableInstances(Result.Value)
            UpdateInstanceText()
            Logger.Info($"收藏页选择下载版本：{SelectedInstance.Name}")
        Catch ex As Exception
            Logger.Error(ex, "选择下载版本失败")
            Hint("选择下载版本失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub Check_Change(sender As Object, user As Boolean)
        If SuppressCheckEvent Then Return
        UpdateSummary()
        SuppressCheckEvent = True
        Try
            CheckAll.Checked = FavoriteChecks.Any() AndAlso FavoriteChecks.All(Function(p) p.Check.Checked)
        Finally
            SuppressCheckEvent = False
        End Try
    End Sub

    Private Sub CheckAll_Change(sender As Object, user As Boolean) Handles CheckAll.Change
        If SuppressCheckEvent Then Return
        SuppressCheckEvent = True
        Try
            For Each Pair In FavoriteChecks
                Pair.Check.Checked = CheckAll.Checked
            Next
            UpdateSummary()
        Finally
            SuppressCheckEvent = False
        End Try
    End Sub

    Private Sub UpdateSummary()
        Dim SelectedCount As Integer = FavoriteChecks.Where(Function(p) p.Check.Checked).Count()
        LabSummary.Text = $"已选 {SelectedCount} / {FavoriteChecks.Count} 项"
        BtnDownload.IsEnabled = SelectedCount > 0 AndAlso SelectedInstance IsNot Nothing
    End Sub

    Private Sub BtnDownload_Click(sender As Object, e As EventArgs) Handles BtnDownload.Click
        Dim Selected = FavoriteChecks.Where(Function(p) p.Check.Checked).Select(Function(p) p.Item).ToList()
        If Not Selected.Any() Then
            Hint("请先勾选要下载的内容", HintType.Blue)
            Return
        End If
        If SelectedInstance Is Nothing Then
            MyMsgBox("请选择你要将资源下载到的版本", "提示")
            Return
        End If
        If Not ModFavorite.SupportsFavorite(CurType) Then
            Hint($"{ModFavorite.GetTypeName(CurType)}暂不支持一键下载", HintType.Red)
            Return
        End If
        ModFavorite.DownloadFavoritesToInstance(Selected, SelectedInstance, CurType)
    End Sub

#End Region

End Class

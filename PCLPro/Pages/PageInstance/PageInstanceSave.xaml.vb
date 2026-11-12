Imports System.Windows.Media.Imaging

Public Class PageInstanceSave

    Private Const PageSize As Integer = 40

    Private AllSaves As New List(Of ModSave.SaveInfo)
    Private DisplaySaves As New List(Of ModSave.SaveInfo)
    Private RenderedCount As Integer = 0
    Private IsScanning As Boolean = False
    Private SuppressFilterEvent As Boolean = False
    Private ReadOnly ThumbCache As New System.Collections.Concurrent.ConcurrentDictionary(Of String, BitmapSource)(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly OpenMapWindows As New Dictionary(Of String, SaveMapBrowserWindow)(StringComparer.OrdinalIgnoreCase)

    Private Sub PageEnterRefresh() Handles Me.PageEnter
        RefreshAll()
    End Sub

    Public Sub RefreshAll()
        If IsScanning Then Return
        IsScanning = True
        BtnRefresh.IsEnabled = False
        LabStatus.Text = "正在扫描存档…"
        ModSaveBlueMap.CleanStaleServers()
        RunInThread(
        Sub()
            Try
                Dim Result = ModSave.ScanSaves(PageInstanceLeft.Instance)
                RunInUi(
                Sub()
                    AllSaves = Result
                    IsScanning = False
                    BtnRefresh.IsEnabled = True
                    ApplyFilter()
                End Sub)
            Catch ex As Exception
                Logger.Error(ex, "扫描存档失败")
                RunInUi(
                Sub()
                    IsScanning = False
                    BtnRefresh.IsEnabled = True
                    LabStatus.Text = "扫描失败"
                    Hint("扫描存档失败：" & ex.Message, HintType.Red)
                End Sub)
            End Try
        End Sub)
    End Sub

#Region "筛选与列表"

    Private Sub ComboSort_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboSort.SelectionChanged
        If Not IsLoaded Then Return
        ApplyFilter()
    End Sub

    Private Sub Filter_Check(sender As Object, raiseByMouse As Boolean) Handles BtnFilterAll.Check, BtnFilterAbandoned.Check
        If SuppressFilterEvent OrElse Not IsLoaded Then Return
        SuppressFilterEvent = True
        Try
            BtnFilterAll.Checked = (sender Is BtnFilterAll)
            BtnFilterAbandoned.Checked = (sender Is BtnFilterAbandoned)
        Finally
            SuppressFilterEvent = False
        End Try
        ApplyFilter()
    End Sub

    Private Sub ApplyFilter()
        Dim Filtered As IEnumerable(Of ModSave.SaveInfo) = AllSaves
        If BtnFilterAbandoned.Checked Then Filtered = Filtered.Where(Function(S) S.Abandoned)

        Select Case ComboSort.SelectedIndex
            Case 1 : Filtered = Filtered.OrderByDescending(Function(S) S.PlaySeconds)
            Case 2 : Filtered = Filtered.OrderByDescending(Function(S) S.SizeBytes)
            Case 3 : Filtered = Filtered.OrderBy(Function(S) S.Name, StringComparer.CurrentCulture)
            Case Else : Filtered = Filtered.OrderByDescending(Function(S) S.LastPlayedTicks)
        End Select

        DisplaySaves = Filtered.ToList()
        RenderedCount = 0
        PanItems.Children.Clear()
        Dim AbandonedCount As Integer = AllSaves.Where(Function(S) S.Abandoned).Count()
        LabStatus.Text = $"共 {AllSaves.Count} 个存档 · 疑似废弃 {AbandonedCount} 个"
        HintEmpty.Visibility = If(AllSaves.Any(), Visibility.Collapsed, Visibility.Visible)
        LoadMore()
    End Sub

    Private Sub BtnLoadMore_Click(sender As Object, e As EventArgs) Handles BtnLoadMore.Click
        LoadMore()
    End Sub

    Private Sub LoadMore()
        Dim EndIndex As Integer = Math.Min(RenderedCount + PageSize, DisplaySaves.Count)
        Dim NewRows As New List(Of (Info As ModSave.SaveInfo, Img As Image, Placeholder As TextBlock))
        For i As Integer = RenderedCount To EndIndex - 1
            Dim Row = BuildRow(DisplaySaves(i), NewRows)
            PanItems.Children.Add(Row)
        Next
        RenderedCount = EndIndex
        BtnLoadMore.Visibility = If(RenderedCount < DisplaySaves.Count, Visibility.Visible, Visibility.Collapsed)
        LoadThumbs(NewRows)
    End Sub

    Private Function BuildRow(Info As ModSave.SaveInfo, Pending As List(Of (Info As ModSave.SaveInfo, Img As Image, Placeholder As TextBlock))) As FrameworkElement
        Dim Card As New Border With {
            .CornerRadius = New CornerRadius(6),
            .Margin = New Thickness(0, 0, 0, 9),
            .Padding = New Thickness(8, 8, 10, 8)
        }
        Card.SetResourceReference(Border.BackgroundProperty, "ColorBrush8")

        Dim Layout As New Grid
        Layout.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(104)})
        Layout.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Layout.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        Dim ThumbBorder As New Border With {
            .Width = 96,
            .Height = 96,
            .CornerRadius = New CornerRadius(5),
            .Clip = New RectangleGeometry(New Rect(0, 0, 96, 96), 5, 5),
            .VerticalAlignment = VerticalAlignment.Top,
            .HorizontalAlignment = HorizontalAlignment.Left
        }
        ThumbBorder.SetResourceReference(Border.BackgroundProperty, "ColorBrush7")
        Dim ThumbGrid As New Grid
        Dim Img As New Image With {.Stretch = Stretch.UniformToFill}
        ThumbGrid.Children.Add(Img)
        Dim Placeholder As New TextBlock With {
            .Text = "无预览",
            .FontSize = 11,
            .HorizontalAlignment = HorizontalAlignment.Center,
            .VerticalAlignment = VerticalAlignment.Center,
            .Visibility = Visibility.Collapsed
        }
        Placeholder.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3")
        ThumbGrid.Children.Add(Placeholder)
        ThumbBorder.Child = ThumbGrid
        Layout.Children.Add(ThumbBorder)

        Dim PanText As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center, .Margin = New Thickness(10, 0, 10, 0)}
        Grid.SetColumn(PanText, 1)
        Dim LabName As New TextBlock With {
            .Text = Info.Name,
            .FontSize = 14,
            .TextTrimming = TextTrimming.CharacterEllipsis
        }
        LabName.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
        PanText.Children.Add(LabName)
        Dim LabLine1 As New TextBlock With {
            .Text = Info.Summary1,
            .FontSize = 11.5,
            .Margin = New Thickness(0, 5, 0, 0),
            .TextTrimming = TextTrimming.CharacterEllipsis
        }
        LabLine1.SetResourceReference(TextBlock.ForegroundProperty, If(Info.Abandoned, "ColorBrushRed", "ColorBrushGray3"))
        PanText.Children.Add(LabLine1)
        Dim LabLine2 As New TextBlock With {
            .Text = Info.Summary2,
            .FontSize = 11.5,
            .Margin = New Thickness(0, 3, 0, 0),
            .TextTrimming = TextTrimming.CharacterEllipsis
        }
        LabLine2.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3")
        PanText.Children.Add(LabLine2)
        Dim LabPath As New TextBlock With {
            .Text = Info.FolderName,
            .FontSize = 10.5,
            .Margin = New Thickness(0, 4, 0, 0),
            .TextTrimming = TextTrimming.CharacterEllipsis
        }
        LabPath.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray4")
        PanText.Children.Add(LabPath)
        Layout.Children.Add(PanText)

        Dim PanButtons As New StackPanel With {.Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Grid.SetColumn(PanButtons, 2)
        Dim BtnBag As New MyButton With {
            .Text = "背包",
            .MinWidth = 68,
            .Padding = New Thickness(10, 0, 10, 0),
            .Margin = New Thickness(0, 0, 8, 0),
            .ToolTip = "读取玩家数据，查看这个世界背包里的物品列表"
        }
        AddHandler BtnBag.Click, Sub(sender As Object, e As EventArgs) ShowInventory(Info)
        PanButtons.Children.Add(BtnBag)
        Dim BtnMap As New MyButton With {
            .Text = "3D 预览",
            .MinWidth = 84,
            .Padding = New Thickness(10, 0, 10, 0),
            .Margin = New Thickness(0, 0, 8, 0),
            .ToolTip = "用独立窗口打开可旋转、缩放的 3D 地形预览（首次需要读取区块）"
        }
        AddHandler BtnMap.Click, Sub(sender As Object, e As EventArgs) OpenMapPreview(Info, BtnMap)
        PanButtons.Children.Add(BtnMap)
        Dim BtnOpen As New MyButton With {
            .Text = "打开",
            .MinWidth = 68,
            .Padding = New Thickness(10, 0, 10, 0),
            .Margin = New Thickness(0, 0, 8, 0),
            .ToolTip = "在资源管理器中打开该存档文件夹"
        }
        AddHandler BtnOpen.Click, Sub(sender As Object, e As EventArgs) OpenSaveFolder(Info)
        PanButtons.Children.Add(BtnOpen)
        Dim BtnDelete As New MyIconButton With {
            .Logo = Logo.IconButtonDelete,
            .LogoScale = 0.8,
            .Width = 28,
            .Height = 28,
            .Theme = MyIconButton.Themes.Red,
            .ToolTip = "删除这个存档"
        }
        AddHandler BtnDelete.Click, Sub(sender As Object, e As EventArgs) DeleteSave(Info)
        PanButtons.Children.Add(BtnDelete)
        Layout.Children.Add(PanButtons)

        Card.Child = Layout
        Pending.Add((Info, Img, Placeholder))
        Return Card
    End Function

    Private Sub LoadThumbs(Rows As List(Of (Info As ModSave.SaveInfo, Img As Image, Placeholder As TextBlock)))
        If Not Rows.Any() Then Return
        RunInThread(
        Sub()
            For Each Row In Rows
                Try
                    Dim Bmp As BitmapSource = Nothing
                    If Not ThumbCache.TryGetValue(Row.Info.Path, Bmp) Then
                        Bmp = ModSave.LoadCachedThumb(Row.Info)
                        If Bmp IsNot Nothing Then ThumbCache(Row.Info.Path) = Bmp
                    End If
                    If Bmp IsNot Nothing Then
                        Dim TargetImg As Image = Row.Img
                        Dim TargetBmp As BitmapSource = Bmp
                        RunInUi(Sub()
                                    TargetImg.Source = TargetBmp
                                End Sub)
                    Else
                        Dim TargetPlaceholder As TextBlock = Row.Placeholder
                        RunInUi(Sub()
                                    TargetPlaceholder.Visibility = Visibility.Visible
                                End Sub)
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, $"加载存档缩略图失败：{Row.Info.Path}")
                End Try
            Next
        End Sub)
    End Sub

    Private Sub ShowInventory(Info As ModSave.SaveInfo)
        Hint("正在读取背包物品…")
        RunInThread(
        Sub()
            Dim Text As String = ModSave.ReadInventoryText(Info, PageInstanceLeft.Instance)
            RunInUi(Sub() MyMsgBox(Text, $"背包 - {Info.Name}"))
        End Sub)
    End Sub

    Private Const MapRadiusChunks As Integer = 8

    ''' <summary>
    ''' 优先使用 BlueMap 官方渲染器在浏览器中查看，失败时退回内置 3D 预览。
    ''' </summary>
    Private Sub OpenMapPreview(Info As ModSave.SaveInfo, Button As MyButton)
        ' 已经打开过这个存档的预览：直接激活原窗口，不重新渲染
        Dim Existing As SaveMapBrowserWindow = Nothing
        If OpenMapWindows.TryGetValue(Info.Path, Existing) Then
            If Existing IsNot Nothing AndAlso Existing.IsLoaded Then
                If Existing.WindowState = WindowState.Minimized Then Existing.WindowState = WindowState.Normal
                Existing.Activate()
                Existing.Topmost = True
                Existing.Topmost = False
                Hint("3D 地图预览已打开。", HintType.Blue)
                Return
            End If
            OpenMapWindows.Remove(Info.Path)
        End If

        If Not Settings.Get(Of Boolean)("BlueMapAcceptDownload") Then
            If MyMsgBox("3D 地图预览使用 BlueMap 官方渲染器（MIT 开源）。" & vbCrLf &
                        "首次使用需要从 GitHub 下载渲染器（约 7MB），并从 Mojang 官方下载 Minecraft 客户端资源（约 25MB）用于生成方块模型和材质。" & vbCrLf & vbCrLf &
                        "继续即表示你已阅读并接受 Minecraft EULA，且拥有 Minecraft Java 版正版授权。是否继续？",
                        "启用 BlueMap 3D 地图", "同意并继续", "取消", IsWarn:=True) <> 1 Then
                Hint("已取消。", HintType.Blue)
                Return
            End If
            Settings.Set("BlueMapAcceptDownload", True)
        End If

        Button.IsEnabled = False
        Hint("正在准备 BlueMap 渲染器…")
        RunInThread(
        Sub()
            Try
                If Not ModSaveBlueMap.PrepareJar() Then Throw New Exception("渲染器下载失败")
                Dim JavaExe As String = ModSaveBlueMap.GetJavaExe()
                ModSaveBlueMap.RenderSave(Info, JavaExe, PageInstanceLeft.Instance,
                                          Sub(Text) RunInUi(Sub() Hint(Text)))
                RunInUi(Sub() Hint("渲染完成，正在启动网页服务器…"))
                Dim Url As String = ModSaveBlueMap.StartServer(Info, JavaExe)
                RunInUi(
                Sub()
                    Button.IsEnabled = True
                    Dim MapWindow As New SaveMapBrowserWindow(Url, Info.Name)
                    MapWindow.Owner = Window.GetWindow(Me)
                    OpenMapWindows(Info.Path) = MapWindow
                    AddHandler MapWindow.Closed,
                    Sub()
                        OpenMapWindows.Remove(Info.Path)
                        ModSaveBlueMap.StopServer(Info.Path)
                    End Sub
                    MapWindow.Show()
                    Hint("已打开 BlueMap 3D 地图预览。", HintType.Green)
                End Sub)
            Catch ex As Exception
                Logger.Error(ex, $"BlueMap 渲染失败：{Info.Path}")
                RunInUi(
                Sub()
                    Button.IsEnabled = True
                    Hint("BlueMap 渲染失败：" & ex.Message & "，已改用内置 3D 预览。", HintType.Red)
                    OpenMap3DWindow(Info, Button)
                End Sub)
            End Try
        End Sub)
    End Sub

    Private Sub OpenMap3DWindow(Info As ModSave.SaveInfo, Button As MyButton)
        Button.IsEnabled = False
        Hint("正在读取地图区块，首次打开需要一点时间……")
        RunInThread(
        Sub()
            Try
                Dim Coverage As Integer = 0
                Dim Map = ModSave.BuildTerrain(Info, MapRadiusChunks, Coverage)
                Logger.Info($"存档 3D 地图：{Info.Name}，覆盖率 {Coverage}%，高度 {If(Map Is Nothing, 0, Map.MinY)} ~ {If(Map Is Nothing, 0, Map.MaxY)}")
                If Map Is Nothing Then
                    RunInUi(
                    Sub()
                        Button.IsEnabled = True
                        Hint("这个存档出生点附近还没有已生成的地形，无法显示 3D 地图。", HintType.Red)
                    End Sub)
                    Return
                End If
                Dim Model = SaveMapWindow.CreateModel(Map)
                RunInUi(
                Sub()
                    Button.IsEnabled = True
                    Try
                        Dim W As New SaveMapWindow(Model, Map, Info.Name)
                        If FrmMain IsNot Nothing AndAlso FrmMain.IsLoaded Then W.Owner = FrmMain
                        W.Show()
                        Hint("3D 地图预览已打开。", HintType.Green)
                    Catch ex As Exception
                        Logger.Error(ex, "打开 3D 地图窗口失败")
                        Hint("打开 3D 地图窗口失败：" & ex.Message, HintType.Red)
                    End Try
                End Sub)
            Catch ex As Exception
                Logger.Error(ex, $"生成 3D 地图失败：{Info.Path}")
                RunInUi(
                Sub()
                    Button.IsEnabled = True
                    Hint("生成 3D 地图失败：" & ex.Message, HintType.Red)
                End Sub)
            End Try
        End Sub)
    End Sub

    Private Sub OpenSaveFolder(Info As ModSave.SaveInfo)
        Try
            OpenExplorer(Info.Path)
        Catch ex As Exception
            Logger.Error(ex, "打开存档文件夹失败")
            Hint("打开存档文件夹失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub DeleteSave(Info As ModSave.SaveInfo)
        If MyMsgBox($"确定要删除存档「{Info.Name}」吗？" & vbCrLf & "该操作会直接删除整个存档文件夹，无法撤销。",
                    "删除存档", "删除", "取消", IsWarn:=True) <> 1 Then Return
        Try
            DirectoryUtils.Delete(Info.Path)
            AllSaves.RemoveAll(Function(S) String.Equals(S.Path, Info.Path, StringComparison.OrdinalIgnoreCase))
            ThumbCache.Remove(Info.Path)
            Hint($"已删除存档：{Info.Name}", HintType.Green)
            ApplyFilter()
        Catch ex As Exception
            Logger.Error(ex, $"删除存档失败：{Info.Path}")
            Hint("删除存档失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

#End Region

#Region "工具栏"

    Private Sub BtnRefresh_Click(sender As Object, e As EventArgs) Handles BtnRefresh.Click
        RefreshAll()
    End Sub

    Private Sub BtnOpenFolder_Click(sender As Object, e As EventArgs) Handles BtnOpenFolder.Click
        Try
            Dim Folder As String = PageInstanceLeft.Instance.PathIndie & "saves\"
            DirectoryUtils.Create(Folder)
            OpenExplorer(Folder)
        Catch ex As Exception
            Logger.Error(ex, "打开存档文件夹失败")
            Hint("打开存档文件夹失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

#End Region

End Class

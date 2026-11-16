Public Class LinkInviteWindow
    Inherits Window

    Private Class TargetOption
        Public Text As String
        Public Instance As McInstance
        Public Compatible As Boolean
        Public IsAuto As Boolean
    End Class

    Private Class LinkInviteCancelledException
        Inherits Exception
    End Class

    Private ReadOnly Info As LinkInviteInfo
    Private ReadOnly LabStatus As TextBlock
    Private ReadOnly LabTargetHint As TextBlock
    Private ReadOnly Loading As MyLoading
    Private ReadOnly BarProgress As ProgressBar
    Private ReadOnly BtnApply As MyButton
    Private ReadOnly BtnClose As MyButton
    Private ReadOnly BtnMin As MyButton
    Private ReadOnly CmbTarget As New MyComboBox With {
        .Width = 340,
        .Height = 32,
        .FontSize = 12.5,
        .HorizontalAlignment = HorizontalAlignment.Left,
        .IsEnabled = False
    }
    Private IsBusy As Boolean
    Private IsCancelled As Boolean
    Private StatusBase As String = ""
    Private ReadOnly StatusTimer As New Threading.DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(700)}
    Private ReadOnly LabSpeed As New TextBlock With {.Width = 140, .FontSize = 12.5, .TextAlignment = TextAlignment.Right, .Text = "速度 —"}
    Private ReadOnly LabLiveMods As New TextBlock With {.Width = 260, .FontSize = 12.5, .TextAlignment = TextAlignment.Left, .Margin = New Thickness(14, 0, 0, 0), .Text = ""}
    Private SpeedSmooth As Long = 0
    Private SyncIndex As Integer = -1
    Private SyncTotal As Integer = 0
    Private IsInstallPhase As Boolean = False
    Private InstallStage As String = ""
    Private PanLiveStatus As StackPanel
    Private MainBorder As Border
    Private MainRoot As Grid
    Private Ball As Window
    Private IsClosingConfirmed As Boolean = False
    Private CardBrush As SolidColorBrush
    Private BallGlyph As TextBlock
    Private ReadOnly CardColor As Color = Color.FromRgb(&HFB, &HFB, &HFB)
    Private ReadOnly AccentColor As Color = Color.FromRgb(&H13, &H70, &HF3)
    Private CardFullWidth As Double
    Private CardFullHeight As Double
    Private ContentPanel As Grid
    Private IsMorphing As Boolean = False
    Private ReadOnly MainScale As New ScaleTransform(1, 1)
    Private CardShadowEffect As System.Windows.Media.Effects.DropShadowEffect
    Private MorphScaleFrom As Double = 1
    Private MorphScaleTo As Double = 1
    Private MorphRadiusFrom As Double = 7
    Private MorphRadiusTo As Double = 7

    Public Shared Sub Import(Path As String)
        Dim Info As LinkInviteInfo
        Try
            Info = LinkInviteParse(Path)
        Catch ex As Exception
            Logger.Warn(ex, $"读取联机邀请文件失败：{Path}")
            MyMsgBox("无法读取这个联机邀请文件：" & vbCrLf & ex.Message, "联机邀请", IsWarn:=True)
            Return
        End Try
        If String.IsNullOrEmpty(Info.RoomCode) Then
            MyMsgBox("这个邀请文件没有房间码，无法加入联机房间。" & vbCrLf &
                     "请让房主先在联机页创建房间，再点击「分享邀请文件」重新生成。", "联机邀请", IsWarn:=True)
            Return
        End If
        Dim Window As New LinkInviteWindow(Info)
        If Application.Current.MainWindow IsNot Nothing AndAlso Application.Current.MainWindow.IsVisible Then Window.Owner = Application.Current.MainWindow
        Window.Show()
    End Sub

    Public Sub New(Info As LinkInviteInfo)
        Me.Info = Info
        Style = TryCast(Application.Current.TryFindResource("MyWindow"), Style)
        Title = "联机邀请"
        WindowStyle = WindowStyle.None
        AllowsTransparency = True
        Background = Brushes.Transparent
        ResizeMode = ResizeMode.NoResize
        SizeToContent = SizeToContent.Height
        Width = 660
        WindowStartupLocation = WindowStartupLocation.CenterOwner
        ShowInTaskbar = True

        CardBrush = New SolidColorBrush(CardColor)
        CardShadowEffect = New System.Windows.Media.Effects.DropShadowEffect With {
            .Color = Color.FromRgb(&H34, &H3D, &H4A),
            .BlurRadius = 20,
            .ShadowDepth = 4,
            .Opacity = 0.5,
            .RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
        }
        MainBorder = New Border With {
            .CornerRadius = New CornerRadius(7),
            .Margin = New Thickness(30),
            .Background = CardBrush,
            .RenderTransform = MainScale,
            .RenderTransformOrigin = New Point(0.5, 0.5),
            .Effect = CardShadowEffect
        }
        Dim PanBorder As Border = MainBorder
        Dim PanMain As New Grid With {.Margin = New Thickness(22, 20, 22, 22)}
        ContentPanel = PanMain
        PanMain.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
        PanMain.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(2)})
        PanMain.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(13)})
        PanMain.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
        PanMain.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})

        Dim LabTitle As New TextBlock With {
            .Text = "联机邀请",
            .FontSize = 23,
            .HorizontalAlignment = HorizontalAlignment.Left,
            .Margin = New Thickness(4, -1, 0, 9)
        }
        LabTitle.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
        PanMain.Children.Add(LabTitle)
        Dim ShapeLine As New System.Windows.Shapes.Rectangle With {.Height = 2, .Fill = LabTitle.Foreground}
        Grid.SetRow(ShapeLine, 1)
        PanMain.Children.Add(ShapeLine)

        Dim Body As New StackPanel()
        Dim Intro As New TextBlock With {
            .Text = "房主邀请你加入联机房间。点击「一键同步并加入」后，启动器会自动准备所需版本、同步模组并进入房间。",
            .FontSize = 14,
            .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(2, 0, 2, 16)
        }
        Intro.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        Body.Children.Add(Intro)

        Dim InfoGrid As New Grid()
        InfoGrid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        InfoGrid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        For RowIndex As Integer = 0 To 2
            InfoGrid.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
        Next

        Dim AddCell As Action(Of Integer, Integer, String, String) =
        Sub(Row As Integer, Column As Integer, Label As String, Value As String)
            Dim Cell As New StackPanel With {.Margin = New Thickness(2, 0, 18, 13)}
            Dim Lab As New TextBlock With {.Text = Label, .FontSize = 12}
            Lab.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3")
            Dim Val As New TextBlock With {
                .Text = If(String.IsNullOrEmpty(Value), "—", Value),
                .FontSize = 14,
                .Margin = New Thickness(0, 2, 0, 0),
                .TextWrapping = TextWrapping.Wrap
            }
            Val.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
            Cell.Children.Add(Lab)
            Cell.Children.Add(Val)
            Grid.SetRow(Cell, Row)
            Grid.SetColumn(Cell, Column)
            InfoGrid.Children.Add(Cell)
        End Sub
        AddCell(0, 0, "房间码", Info.RoomCode)
        AddCell(0, 1, "游戏端口", If(Info.Port > 0, Info.Port.ToString(), "未检测到"))
        AddCell(1, 0, "需要的版本", Info.McVersion)
        AddCell(1, 1, "模组加载器", If(String.IsNullOrEmpty(Info.LoaderText), "原版", Info.LoaderText))
        AddCell(2, 0, "模组数量", Info.Mods.Count & " 个")
        AddCell(2, 1, "目标版本名", Info.InstanceName)
        Body.Children.Add(InfoGrid)

        Dim TargetPanel As New StackPanel With {.Margin = New Thickness(2, 2, 0, 0)}
        Dim LabTarget As New TextBlock With {.Text = "目标版本", .FontSize = 12}
        LabTarget.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3")
        TargetPanel.Children.Add(LabTarget)
        TargetPanel.Children.Add(CmbTarget)
        LabTargetHint = New TextBlock With {
            .Text = "正在读取本机版本列表…",
            .FontSize = 12,
            .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(0, 5, 0, 0)
        }
        LabTargetHint.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3")
        TargetPanel.Children.Add(LabTargetHint)
        Body.Children.Add(TargetPanel)

        Loading = New MyLoading With {
            .Height = 80,
            .Text = "正在处理",
            .Visibility = Visibility.Collapsed
        }
        Loading.SetResourceReference(MyLoading.ForegroundProperty, "ColorBrush3")
        Body.Children.Add(Loading)
        LabStatus = New TextBlock With {
            .Text = If(Info.Mods.Count >= 200,
                       $"模组较多（{Info.Mods.Count} 个），首次同步需要一些时间。",
                       "选择目标版本后点击「一键同步并加入」。"),
            .FontSize = 13,
            .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(2, 10, 2, 0)
        }
        LabStatus.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        Body.Children.Add(LabStatus)
        BarProgress = New ProgressBar With {
            .Height = 4,
            .Minimum = 0,
            .Maximum = 1,
            .Value = 0,
            .BorderThickness = New Thickness(0),
            .Visibility = Visibility.Collapsed,
            .Margin = New Thickness(2, 8, 2, 0)
        }
        BarProgress.SetResourceReference(ProgressBar.ForegroundProperty, "ColorBrush3")
        BarProgress.SetResourceReference(ProgressBar.BackgroundProperty, "ColorBrush7")
        Body.Children.Add(BarProgress)
        LabSpeed.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        LabLiveMods.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        Dim PanLive As New StackPanel With {
            .Orientation = Orientation.Horizontal,
            .Margin = New Thickness(2, 5, 2, 0),
            .Visibility = Visibility.Collapsed
        }
        PanLive.Children.Add(LabSpeed)
        PanLive.Children.Add(LabLiveMods)
        Body.Children.Add(PanLive)
        PanLiveStatus = PanLive
        Grid.SetRow(Body, 3)
        PanMain.Children.Add(Body)

        Dim PanBtn As New StackPanel With {
            .Orientation = Orientation.Horizontal,
            .HorizontalAlignment = HorizontalAlignment.Right,
            .Margin = New Thickness(0, 20, 0, 0)
        }
        Dim BtnList As New MyButton With {
            .Text = $"查看模组清单（{Info.Mods.Count}）",
            .Height = 30,
            .TextPadding = New Thickness(12, 0, 12, 0),
            .Margin = New Thickness(0, 0, 10, 0)
        }
        AddHandler BtnList.Click, Sub() ShowModList()
        Dim BtnCopy As New MyButton With {
            .Text = "复制房间码",
            .Height = 30,
            .TextPadding = New Thickness(12, 0, 12, 0),
            .Margin = New Thickness(0, 0, 10, 0)
        }
        AddHandler BtnCopy.Click, Sub() ClipboardSet(Info.RoomCode, False)
        BtnApply = New MyButton With {
            .Text = "一键同步并加入",
            .Height = 30,
            .TextPadding = New Thickness(16, 0, 16, 0),
            .ColorType = MyButton.ColorState.Highlight,
            .Margin = New Thickness(0, 0, 10, 0)
        }
        AddHandler BtnApply.Click, Sub(sender, e) BtnApply_Click(sender, e)
        BtnMin = New MyButton With {
            .Text = "最小化",
            .Height = 30,
            .TextPadding = New Thickness(12, 0, 12, 0),
            .Margin = New Thickness(0, 0, 10, 0)
        }
        AddHandler BtnMin.Click, Sub() MinimizeToBall()
        BtnClose = New MyButton With {
            .Text = "关闭",
            .Height = 30,
            .TextPadding = New Thickness(12, 0, 12, 0)
        }
        AddHandler BtnClose.Click, Sub() Close()
        PanBtn.Children.Add(BtnList)
        PanBtn.Children.Add(BtnCopy)
        PanBtn.Children.Add(BtnApply)
        PanBtn.Children.Add(BtnMin)
        PanBtn.Children.Add(BtnClose)
        Grid.SetRow(PanBtn, 4)
        PanMain.Children.Add(PanBtn)

        MainRoot = New Grid With {.Background = Brushes.Transparent}
        MainRoot.Children.Add(MainBorder)
        AddHandler MainRoot.MouseLeftButtonDown,
            Sub(sender, e)
                If e.ButtonState <> MouseButtonState.Pressed Then Return
                If Not Object.ReferenceEquals(e.OriginalSource, MainRoot) Then Return
                DragMove()
            End Sub
        AddHandler Closing,
            Sub(sender, e)
                If Not IsBusy OrElse IsClosingConfirmed Then Return
                e.Cancel = True
                RunInUi(
                Sub()
                    If MyMsgBox("当前正在下载或安装，关闭会中断后续流程（已下载的文件会保留）。" & vbCrLf & "确定要关闭吗？",
                                "正在下载", "关闭", "继续等待", IsWarn:=True) = 1 Then
                        IsCancelled = True
                        IsClosingConfirmed = True
                        Logger.Info("联机邀请：用户确认关闭，已请求中止后续操作")
                        Close()
                    End If
                End Sub)
            End Sub

        BallGlyph = New TextBlock With {
            .Text = "邀",
            .FontSize = 21,
            .FontWeight = FontWeights.SemiBold,
            .Foreground = Brushes.White,
            .HorizontalAlignment = HorizontalAlignment.Center,
            .VerticalAlignment = VerticalAlignment.Center,
            .Opacity = 0,
            .IsHitTestVisible = False
        }
        PanBorder.Child = PanMain
        MainRoot.Children.Add(BallGlyph)
        Content = MainRoot
        AddHandler StatusTimer.Tick, Sub() RefreshLiveStatus()
        AddHandler Closed,
            Sub()
                If Ball IsNot Nothing Then
                    Ball.Close()
                    Ball = Nothing
                End If
            End Sub
        AddHandler Loaded,
            Sub()
                Dim FinalWidth As Double = ActualWidth
                Dim FinalHeight As Double = ActualHeight
                SizeToContent = SizeToContent.Manual
                Width = FinalWidth
                Height = FinalHeight
                CardFullWidth = Math.Max(52, FinalWidth - 60)
                CardFullHeight = Math.Max(52, FinalHeight - 60)
                MainBorder.Margin = New Thickness(0)
                MainBorder.HorizontalAlignment = HorizontalAlignment.Center
                MainBorder.VerticalAlignment = VerticalAlignment.Center
                MainBorder.Width = CardFullWidth
                MainBorder.Height = CardFullHeight
                LoadTargets()
            End Sub
    End Sub

    Private Sub LoadTargets()
        RunInNewThread(
        Sub()
            Try
                LoadTargetsCore()
            Catch ex As Exception
                Logger.Warn(ex, "联机邀请：加载本机版本列表失败")
                RunInUi(
                Sub()
                    OptionsClear()
                    CmbTarget.Items.Add(New MyComboBoxItem With {
                        .Content = $"自动下载安装：{Info.McVersion} / {If(String.IsNullOrEmpty(Info.LoaderText), "原版", Info.LoaderText)}",
                        .Tag = New TargetOption With {
                            .Text = "自动下载安装",
                            .IsAuto = True
                        }
                    })
                    CmbTarget.SelectedIndex = 0
                    CmbTarget.IsEnabled = True
                    LabTargetHint.Text = "读取本机版本列表失败，已改为自动下载安装。"
                End Sub)
            End Try
        End Sub)
    End Sub

    Private Sub OptionsClear()
        CmbTarget.Items.Clear()
    End Sub

    Private Sub LoadTargetsCore()
        Dim Candidates As New List(Of McInstance)
        Dim SeenName As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Try
            For Each Pair In McInstanceList.ToList()
                Try
                    Dim Items As List(Of McInstance)
                    Try
                        Items = Pair.Value.ToList()
                    Catch ex As Exception
                        Logger.Warn(ex, "联机邀请：读取本机版本分组失败")
                        Continue For
                    End Try
                    For Each Item In Items
                        If Item IsNot Nothing Then Candidates.Add(Item)
                    Next
                Catch ex As Exception
                    Logger.Warn(ex, "联机邀请：读取本机版本分组失败")
                End Try
            Next
        Catch ex As Exception
            Logger.Warn(ex, "联机邀请：读取本机版本列表失败")
        End Try

        Try
            Dim VersionRoot As String = McFolderSelected & "versions\"
            If DirectoryUtils.Exists(VersionRoot) Then
                For Each Folder In DirectoryUtils.GetInfo(VersionRoot).GetDirectories
                    Dim Existed As Boolean = False
                    For Each Item In Candidates
                        If String.Equals(Item.Name, Folder.Name, StringComparison.OrdinalIgnoreCase) Then
                            Existed = True
                            Exit For
                        End If
                    Next
                    If Existed Then Continue For
                    Try
                        Candidates.Add(New McInstance(Folder.Name))
                    Catch ex As Exception
                        Logger.Warn(ex, $"联机邀请：读取版本文件夹失败 {Folder.Name}")
                    End Try
                Next
            End If
        Catch ex As Exception
            Logger.Warn(ex, "联机邀请：扫描 versions 文件夹失败")
        End Try

        Dim Options As New List(Of TargetOption)
        For Each Item In Candidates
            If String.IsNullOrEmpty(Item.Name) OrElse Not SeenName.Add(Item.Name) Then Continue For
            Dim Compatible As Boolean = False
            Try
                Compatible = LinkInviteIsCompatible(Info, Item)
            Catch ex As Exception
                Logger.Warn(ex, $"联机邀请：检查版本兼容性失败 {Item.Name}")
            End Try
            Options.Add(New TargetOption With {
                .Text = $"{Item.Name}（{If(Compatible, "匹配", "不匹配")}）",
                .Instance = Item,
                .Compatible = Compatible
            })
        Next
        Options = Options.OrderByDescending(Function(o) o.Compatible).ToList()

        Dim PlannedName As String = LinkInvitePlanInstanceName(Info)
        Dim AutoName As String = LinkInviteGetAvailableName(PlannedName)
        Dim AutoText As String = $"自动下载安装：{Info.McVersion} / {If(String.IsNullOrEmpty(Info.LoaderText), "原版", Info.LoaderText)}"
        If Not String.Equals(AutoName, PlannedName, StringComparison.OrdinalIgnoreCase) Then
            AutoText &= $"（将安装为 {AutoName}）"
        End If
        Dim AutoOption As New TargetOption With {
            .Text = AutoText,
            .IsAuto = True
        }

        RunInUi(
        Sub()
            OptionsClear()
            For Each TargetItem In Options
                CmbTarget.Items.Add(New MyComboBoxItem With {.Content = TargetItem.Text, .Tag = TargetItem})
            Next
            CmbTarget.Items.Add(New MyComboBoxItem With {.Content = AutoText, .Tag = AutoOption})

            Dim Matched = Options.Where(Function(o) o.Compatible).ToList()
            If Matched.Any Then
                CmbTarget.SelectedIndex = Options.IndexOf(Matched.First())
                LabTargetHint.Text = $"本机共有 {Options.Count} 个版本，其中 {Matched.Count} 个与邀请完全匹配，已自动选中。只有匹配的版本会被同步模组。"
            Else
                CmbTarget.SelectedIndex = Options.Count
                LabTargetHint.Text = If(Options.Count = 0,
                    "本机还没有任何版本，已选择自动下载安装。",
                    $"本机共有 {Options.Count} 个版本，但没有完全匹配的版本，已选择自动下载安装。不匹配的版本不会被同步模组。")
            End If
            CmbTarget.IsEnabled = True
            Logger.Info($"联机邀请：本机版本 {Options.Count} 个，匹配 {Matched.Count} 个")
        End Sub)
    End Sub

    Private Sub BtnApply_Click(sender As Object, e As RoutedEventArgs)
        Dim SelectedItem As MyComboBoxItem = TryCast(CmbTarget.SelectedItem, MyComboBoxItem)
        Dim Selected As TargetOption = If(SelectedItem Is Nothing, Nothing, TryCast(SelectedItem.Tag, TargetOption))
        If Selected Is Nothing Then
            Hint("请先选择要将资源下载到的目标版本。", HintType.Red)
            Return
        End If
        SetBusy(True)
        IsCancelled = False
        RunInNewThread(
        Sub()
            Try
                SetStatus("正在准备目标版本…")
                Dim Target As McInstance = Selected.Instance

                If Selected.IsAuto Then
                    SetStatus("正在获取版本下载地址…")
                    Dim JsonUrl As String = DlClientListGet(Info.McVersion)
                    If String.IsNullOrEmpty(JsonUrl) Then Throw New Exception("无法获取该 MC 版本的下载地址，请检查网络。")
                    Dim Request As McInstallRequest = LinkInviteBuildInstallRequest(Info, JsonUrl)
                    Dim Name As String = Request.NewInstanceName
                    Dim PlannedName As String = LinkInvitePlanInstanceName(Info)
                    If Not String.Equals(Name, PlannedName, StringComparison.OrdinalIgnoreCase) Then
                        SetStatus($"版本 {PlannedName} 已存在，本次将安装为 {Name} …")
                        RunInUi(Sub() Hint($"版本 {PlannedName} 已存在，本次安装为 {Name}", HintType.Blue))
                    End If
                    Dim Started As Boolean = False
                    RunInUiWait(Sub() Started = McInstallStart(Request))
                    If Not Started Then Throw New Exception("启动版本安装失败，请查看任务管理器中的安装日志。")
                    SetStatus($"正在安装 {Name}，可以最小化窗口，关闭窗口会中止后续加入流程…")
                    RunInUi(Sub()
                                IsInstallPhase = True
                                InstallStage = "准备安装"
                                RefreshLiveStatus()
                            End Sub)

                    Dim JsonPath As String = $"{McFolderSelected}versions\{Name}\{Name}.json"
                    LinkInviteWaitInstallFinished(Name, JsonPath, 1200,
                        Sub(Text As String, Percent As Double, Stage As String)
                            SetProgress(Text & "  " & Math.Floor(Percent * 100) & "%", Percent)
                            RunInUi(Sub()
                                        IsInstallPhase = True
                                        If Not String.IsNullOrEmpty(Stage) Then InstallStage = Stage
                                        RefreshLiveStatus()
                                    End Sub)
                        End Sub)
                    RunInUi(Sub()
                                IsInstallPhase = False
                                InstallStage = ""
                            End Sub)
                    SetProgress("安装完成，正在校验版本文件…", 1)
                    CheckCancel()
                    Thread.Sleep(500)
                    Target = New McInstance(Name)
                    Target.Load()
                    If Not Target.IsLoaded OrElse String.IsNullOrEmpty(Target.Version.VanillaName) Then
                        Throw New Exception("版本安装完成后仍无法读取版本信息，请重试或在下载页手动安装。")
                    End If
                    If Target.Version.VanillaName <> Info.McVersion Then
                        Throw New Exception($"安装后的版本为 {Target.Version.VanillaName}，与邀请要求的 {Info.McVersion} 不一致，已停止后续操作。")
                    End If
                    Logger.Info($"联机邀请：自动安装完成 {Name}")
                Else
                    If Not LinkInviteIsCompatible(Info, Target) Then
                        RunInUi(Sub() MyMsgBox(
                            $"所选版本与本邀请不匹配，不会向它同步模组，也无法保证能进入房间。" & vbCrLf & vbCrLf &
                            $"邀请要求：{Info.McVersion} / {If(String.IsNullOrEmpty(Info.LoaderText), "原版", Info.LoaderText)}" & vbCrLf &
                            $"已选版本：{Target.Name}" & vbCrLf & vbCrLf &
                            "请改选标记为「匹配」的版本，或选择「自动下载安装」由启动器安装正确的版本。",
                            "版本不匹配", "我知道了", IsWarn:=True))
                        SetStatus("所选版本与邀请不匹配，已停止。")
                        Return
                    End If
                End If

                SetStatus("正在同步模组…")
                CheckCancel()
                If Not Target.IsLoaded Then Throw New Exception("目标版本尚未就绪，已停止后续操作。")
                Dim Missing As List(Of String) = LinkInviteSync(Info, Target,
                    Sub(Text As String, Done As Integer, Total As Integer, Percent As Double)
                        SetProgress(Text, Percent)
                        RunInUi(
                        Sub()
                            SyncIndex = Done
                            SyncTotal = Total
                            RefreshLiveStatus()
                        End Sub)
                    End Sub)
                SetProgress("模组同步完成", 1)
                RunInUi(
                Sub()
                    SyncIndex = -1
                    SyncTotal = 0
                    RefreshLiveStatus()
                End Sub)
                CheckCancel()
                If Missing.Any Then
                    Dim Content As String = $"有 {Missing.Count} 个模组需要手动安装（没有可用的自动下载地址）：" & vbCrLf &
                                            Missing.Take(10).Join(vbCrLf) & If(Missing.Count > 10, vbCrLf & "……", "") & vbCrLf & vbCrLf &
                                            "可以继续加入，但版本不一致可能导致无法进入服务器。是否仍然尝试？"
                    Dim Choice As Integer = 0
                    RunInUiWait(Sub() Choice = MyMsgBox(Content, "模组未同步完整", "仍然加入", "取消", IsWarn:=True))
                    If Choice = 2 Then
                        SetStatus("已取消加入。")
                        Return
                    End If
                End If

                CheckCancel()
                If PageLinkMain.LinkState = PageLinkMain.LinkStates.Finished AndAlso PageLinkMain.GetCurrentRoomCode() <> Info.RoomCode Then
                    Throw New Exception("你当前在另一个联机房间，请先在联机页离开房间后再试。")
                End If
                If PageLinkMain.LinkState <> PageLinkMain.LinkStates.Finished Then
                    SetStatus("正在加入联机房间…")
                    RunInUiWait(Sub() PageLinkMain.Join(Info.RoomCode))
                    Dim Deadline As Date = Date.Now.AddSeconds(120)
                    Do While Date.Now < Deadline
                        CheckCancel()
                        Select Case PageLinkMain.LinkState
                            Case PageLinkMain.LinkStates.Finished
                                Exit Do
                            Case PageLinkMain.LinkStates.Failed
                                Throw New Exception("加入联机房间失败，房间可能已经关闭，请让房主重新创建房间并分享。")
                        End Select
                        Thread.Sleep(500)
                    Loop
                    If PageLinkMain.LinkState <> PageLinkMain.LinkStates.Finished Then
                        Throw New Exception("加入联机房间超时，房间可能已经关闭，请稍后重试。")
                    End If
                End If

                CheckCancel()
                Dim Address As String = If(Info.Port > 0, "127.0.0.1:" & Info.Port, "")
                SetStatus("正在启动游戏…")
                Logger.Info($"联机邀请：启动游戏 {Target.Name}，服务器地址 {If(Address, "（未指定）")}")
                RunInUi(
                Sub()
                    If McLaunchStart(New McLaunchOptions With {.Instance = Target, .ServerIp = Address}) Then
                        IsCancelled = True
                        Close()
                    End If
                End Sub)
            Catch ex As LinkInviteCancelledException
                Logger.Info("联机邀请：窗口已关闭，后续操作已中止")
            Catch ex As Exception
                Logger.Warn(ex, "联机邀请处理失败")
                SetStatus("处理失败：" & ex.Message)
                Hint("联机邀请处理失败：" & ex.Message, HintType.Red)
            Finally
                SetBusy(False)
            End Try
        End Sub)
    End Sub

    Private Sub ShowModList()
        Dim Win As New Window With {
            .Title = "模组清单",
            .Width = 720,
            .Style = TryCast(Application.Current.TryFindResource("MyWindow"), Style),
            .WindowStyle = WindowStyle.None,
            .AllowsTransparency = True,
            .Background = Brushes.Transparent,
            .ResizeMode = ResizeMode.NoResize,
            .SizeToContent = SizeToContent.Height,
            .WindowStartupLocation = WindowStartupLocation.CenterOwner,
            .Owner = Me,
            .ShowInTaskbar = False
        }
        Dim PanBorder As New Border With {
            .CornerRadius = New CornerRadius(7),
            .Margin = New Thickness(30),
            .Background = New SolidColorBrush(Color.FromRgb(&HFB, &HFB, &HFB)),
            .Effect = New System.Windows.Media.Effects.DropShadowEffect With {
                .Color = Color.FromRgb(&H34, &H3D, &H4A),
                .BlurRadius = 20,
                .ShadowDepth = 4,
                .Opacity = 0.5,
                .RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
            }
        }
        Dim PanMain As New Grid With {.Margin = New Thickness(22, 20, 22, 22)}
        PanMain.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
        PanMain.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(2)})
        PanMain.RowDefinitions.Add(New RowDefinition With {.Height = New GridLength(13)})
        PanMain.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
        PanMain.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})

        Dim LabTitle As New TextBlock With {
            .Text = "模组清单",
            .FontSize = 23,
            .HorizontalAlignment = HorizontalAlignment.Left,
            .Margin = New Thickness(4, -1, 0, 9)
        }
        LabTitle.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush2")
        PanMain.Children.Add(LabTitle)
        Dim ShapeLine As New System.Windows.Shapes.Rectangle With {.Height = 2, .Fill = LabTitle.Foreground}
        Grid.SetRow(ShapeLine, 1)
        PanMain.Children.Add(ShapeLine)

        Dim AutoCount As Integer = Info.Mods.Where(Function(m) Not String.IsNullOrEmpty(m.Url)).Count
        Dim Body As New StackPanel()
        Dim Summary As New TextBlock With {
            .Text = $"共 {Info.Mods.Count} 个模组，其中 {AutoCount} 个可自动下载，{Info.Mods.Count - AutoCount} 个需要手动安装。",
            .FontSize = 14,
            .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(2, 0, 2, 12)
        }
        Summary.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray2")
        Body.Children.Add(Summary)

        Dim ListPanel As New StackPanel()
        For Each Item In Info.Mods
            Dim SizeText As String = If(Item.Size >= 1048576, $"{Math.Round(Item.Size / 1048576, 1)} MB", $"{Math.Max(1, Math.Round(Item.Size / 1024))} KB")
            ListPanel.Children.Add(New MyListItem With {
                .Title = Item.FileName,
                .Info = $"{If(String.IsNullOrEmpty(Item.Url), "需手动", "可自动")} · {SizeText}",
                .Height = 32,
                .FontSize = 13,
                .Margin = New Thickness(0, 0, 0, 2)
            })
        Next
        Dim Scroller As New MyScrollViewer With {
            .Height = 400,
            .VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            .HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            .Content = ListPanel
        }
        Body.Children.Add(Scroller)
        Grid.SetRow(Body, 3)
        PanMain.Children.Add(Body)

        Dim PanBtn As New StackPanel With {
            .Orientation = Orientation.Horizontal,
            .HorizontalAlignment = HorizontalAlignment.Right,
            .Margin = New Thickness(0, 16, 0, 0)
        }
        Dim BtnOk As New MyButton With {
            .Text = "关闭",
            .Height = 30,
            .TextPadding = New Thickness(12, 0, 12, 0)
        }
        AddHandler BtnOk.Click, Sub() Win.Close()
        PanBtn.Children.Add(BtnOk)
        Grid.SetRow(PanBtn, 4)
        PanMain.Children.Add(PanBtn)

        AddHandler PanBorder.MouseLeftButtonDown,
            Sub(sender, e)
                If e.ButtonState <> MouseButtonState.Pressed Then Return
                If Not Object.ReferenceEquals(e.OriginalSource, PanBorder) Then Return
                Win.DragMove()
            End Sub
        PanBorder.Child = PanMain
        Win.Content = PanBorder
        Win.ShowDialog()
    End Sub

    Private Sub MinimizeToBall()
        If Ball IsNot Nothing OrElse Not IsVisible Then Return
        Dim CenterX As Double = Left + ActualWidth / 2
        Dim CenterY As Double = Top + ActualHeight / 2
        Dim ContentFade As New System.Windows.Media.Animation.DoubleAnimation(1, 0, New Duration(TimeSpan.FromMilliseconds(90)))
        AddHandler ContentFade.Completed, Sub() ContentPanel.Visibility = Visibility.Collapsed
        ContentPanel.BeginAnimation(UIElement.OpacityProperty, ContentFade)
        MainBorder.Effect = Nothing
        Dim Duration As New Duration(TimeSpan.FromMilliseconds(380))
        Dim Ease As New System.Windows.Media.Animation.CubicEase With {.EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn}
        Dim TargetScaleX As Double = 52 / CardFullWidth
        Dim TargetScaleY As Double = 52 / CardFullHeight
        MorphScaleFrom = 1
        MorphScaleTo = TargetScaleX
        MorphRadiusFrom = 7
        MorphRadiusTo = 26
        MainBorder.CornerRadius = New CornerRadius(7)
        StartCornerMorph()
        CardBrush.BeginAnimation(SolidColorBrush.ColorProperty,
            New System.Windows.Media.Animation.ColorAnimation(CardColor, AccentColor, Duration) With {
                .BeginTime = TimeSpan.FromMilliseconds(120),
                .EasingFunction = Ease
            })
        BallGlyph.BeginAnimation(UIElement.OpacityProperty,
            New System.Windows.Media.Animation.DoubleAnimation(0, 1, New Duration(TimeSpan.FromMilliseconds(170))) With {
                .BeginTime = TimeSpan.FromMilliseconds(230)
            })
        MainScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            New System.Windows.Media.Animation.DoubleAnimation(1, TargetScaleX, Duration) With {.EasingFunction = Ease})
        Dim ScaleAnimY As New System.Windows.Media.Animation.DoubleAnimation(1, TargetScaleY, Duration) With {.EasingFunction = Ease}
        AddHandler ScaleAnimY.Completed,
            Sub()
                Hide()
                ResetCardState()
                ShowBall(CenterX, CenterY)
            End Sub
        MainScale.BeginAnimation(ScaleTransform.ScaleYProperty, ScaleAnimY)
    End Sub

    Private Sub StartCornerMorph()
        IsMorphing = True
        RemoveHandler System.Windows.Media.CompositionTarget.Rendering, AddressOf OnCornerMorphRendering
        AddHandler System.Windows.Media.CompositionTarget.Rendering, AddressOf OnCornerMorphRendering
    End Sub

    Private Sub OnCornerMorphRendering(sender As Object, e As EventArgs)
        If Not IsMorphing Then Return
        Dim Span As Double = MorphScaleTo - MorphScaleFrom
        If Math.Abs(Span) < 0.0001 Then Return
        Dim T As Double = (MainScale.ScaleX - MorphScaleFrom) / Span
        T = Math.Max(0, Math.Min(1, T))
        Dim VisualRadius As Double = MorphRadiusFrom + (MorphRadiusTo - MorphRadiusFrom) * T
        Dim ScaleNow As Double = Math.Max(0.02, MainScale.ScaleX)
        MainBorder.CornerRadius = New CornerRadius(VisualRadius / ScaleNow)
    End Sub

    Private Sub StopCornerMorph()
        IsMorphing = False
        RemoveHandler System.Windows.Media.CompositionTarget.Rendering, AddressOf OnCornerMorphRendering
    End Sub

    Private Sub ResetCardState()
        StopCornerMorph()
        MainScale.BeginAnimation(ScaleTransform.ScaleXProperty, Nothing)
        MainScale.BeginAnimation(ScaleTransform.ScaleYProperty, Nothing)
        MainScale.ScaleX = 1
        MainScale.ScaleY = 1
        CardBrush.BeginAnimation(SolidColorBrush.ColorProperty, Nothing)
        BallGlyph.BeginAnimation(UIElement.OpacityProperty, Nothing)
        ContentPanel.BeginAnimation(UIElement.OpacityProperty, Nothing)
        MainBorder.CornerRadius = New CornerRadius(7)
        CardBrush.Color = CardColor
        MainBorder.Effect = CardShadowEffect
        BallGlyph.Opacity = 0
        ContentPanel.Visibility = Visibility.Visible
        ContentPanel.Opacity = 1
    End Sub

    Private Sub ShowBall(CenterX As Double, CenterY As Double)
        If Ball IsNot Nothing Then Return
        Const BallSize As Double = 52
        Const Size As Double = 76
        Dim BallScale As New ScaleTransform(0.82, 0.82)
        Dim BallGrid As New Grid With {
            .Width = BallSize,
            .Height = BallSize,
            .Background = Brushes.Transparent,
            .Cursor = Cursors.Hand,
            .ToolTip = "点击展开联机邀请，可拖动",
            .RenderTransform = BallScale,
            .RenderTransformOrigin = New Point(0.5, 0.5)
        }
        Dim BallShadow As New System.Windows.Media.Effects.DropShadowEffect With {
            .Color = Color.FromRgb(&H13, &H40, &H80),
            .BlurRadius = 16,
            .ShadowDepth = 3,
            .Opacity = 0.35
        }
        Dim BallBorder As New Border With {
            .CornerRadius = New CornerRadius(26),
            .Effect = BallShadow
        }
        BallBorder.SetResourceReference(Border.BackgroundProperty, "ColorBrush3")
        BallGrid.Children.Add(BallBorder)
        BallGrid.Children.Add(New Border With {
            .CornerRadius = New CornerRadius(26),
            .Background = New LinearGradientBrush(Color.FromArgb(&H38, &HFF, &HFF, &HFF), Color.FromArgb(0, &HFF, &HFF, &HFF), New Point(0.15, 0), New Point(0.85, 1)),
            .IsHitTestVisible = False
        })
        Dim Halo As New System.Windows.Shapes.Ellipse With {
            .Width = BallSize,
            .Height = BallSize,
            .Stroke = Brushes.White,
            .StrokeThickness = 2,
            .Opacity = 0,
            .IsHitTestVisible = False,
            .RenderTransformOrigin = New Point(0.5, 0.5)
        }
        BallGrid.Children.Add(Halo)
        BallGrid.Children.Add(New TextBlock With {
            .Text = "邀",
            .FontSize = 22,
            .FontWeight = FontWeights.SemiBold,
            .Foreground = Brushes.White,
            .HorizontalAlignment = HorizontalAlignment.Center,
            .VerticalAlignment = VerticalAlignment.Center,
            .IsHitTestVisible = False
        })
        Ball = New Window With {
            .Width = Size,
            .Height = Size,
            .WindowStyle = WindowStyle.None,
            .AllowsTransparency = True,
            .Background = Brushes.Transparent,
            .ResizeMode = ResizeMode.NoResize,
            .ShowInTaskbar = False,
            .Topmost = True,
            .Left = CenterX - Size / 2,
            .Top = CenterY - Size / 2,
            .Content = BallGrid
        }

        AddHandler BallGrid.MouseEnter,
            Sub()
                If BallGrid.IsMouseCaptured Then Return
                AnimateBallVisual(BallScale, BallShadow, False, 1.08, 170)
            End Sub
        AddHandler BallGrid.MouseLeave,
            Sub()
                If BallGrid.IsMouseCaptured Then Return
                AnimateBallVisual(BallScale, BallShadow, False, 1.0, 190)
            End Sub
        AddHandler BallGrid.MouseLeftButtonDown,
            Sub(sender, e)
                If Ball Is Nothing Then Return
                e.Handled = True
                BallGrid.Cursor = Cursors.SizeAll
                AnimateBallVisual(BallScale, BallShadow, True, 0.93, 90)
                Dim OldLeft As Double = Ball.Left, OldTop As Double = Ball.Top
                Try
                    Ball.DragMove()
                Catch
                End Try
                BallGrid.Cursor = Cursors.Hand
                Dim Moved As Boolean = Math.Abs(Ball.Left - OldLeft) >= 3 OrElse Math.Abs(Ball.Top - OldTop) >= 3
                AnimateBallVisual(BallScale, BallShadow, False, If(BallGrid.IsMouseOver, 1.08, 1.0), 260)
                If Not Moved Then RestoreFromBall()
            End Sub

        Ball.Show()
        BtnBallEnterAnimation(BallGrid, BallScale)
        If IsBusy AndAlso Not IsCancelled Then StartBallPulse(Halo)
    End Sub

    Private Sub BtnBallEnterAnimation(BallGrid As Grid, BallScale As ScaleTransform)
        BallGrid.BeginAnimation(UIElement.OpacityProperty,
            New System.Windows.Media.Animation.DoubleAnimation(0, 1, New Duration(TimeSpan.FromMilliseconds(120))))
        Dim Pop As New System.Windows.Media.Animation.BackEase With {
            .EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            .Amplitude = 0.45
        }
        BallScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            New System.Windows.Media.Animation.DoubleAnimation(0.82, 1, New Duration(TimeSpan.FromMilliseconds(240))) With {.EasingFunction = Pop})
        BallScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            New System.Windows.Media.Animation.DoubleAnimation(0.82, 1, New Duration(TimeSpan.FromMilliseconds(240))) With {.EasingFunction = Pop})
    End Sub

    Private Sub AnimateBallVisual(BallScale As ScaleTransform,
                                  Shadow As System.Windows.Media.Effects.DropShadowEffect,
                                  Pressed As Boolean,
                                  TargetScale As Double,
                                  DurationMs As Integer)
        Dim Span As New Duration(TimeSpan.FromMilliseconds(DurationMs))
        Dim Ease As System.Windows.Media.Animation.EasingFunctionBase
        If Pressed Then
            Ease = New System.Windows.Media.Animation.QuadraticEase With {.EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut}
        Else
            Ease = New System.Windows.Media.Animation.BackEase With {
                .EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                .Amplitude = 0.35
            }
        End If
        BallScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            New System.Windows.Media.Animation.DoubleAnimation(TargetScale, Span) With {.EasingFunction = Ease})
        BallScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            New System.Windows.Media.Animation.DoubleAnimation(TargetScale, Span) With {.EasingFunction = Ease})
        Shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty,
            New System.Windows.Media.Animation.DoubleAnimation(If(Pressed, 9, 16), Span) With {.EasingFunction = Ease})
        Shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.ShadowDepthProperty,
            New System.Windows.Media.Animation.DoubleAnimation(If(Pressed, 1, 3), Span) With {.EasingFunction = Ease})
        Shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
            New System.Windows.Media.Animation.DoubleAnimation(If(Pressed, 0.18, 0.35), Span) With {.EasingFunction = Ease})
    End Sub

    Private Sub StartBallPulse(Halo As System.Windows.Shapes.Ellipse)
        Dim HaloScale As New ScaleTransform(1, 1)
        Halo.RenderTransform = HaloScale
        Halo.BeginAnimation(UIElement.OpacityProperty,
            New System.Windows.Media.Animation.DoubleAnimation(0.55, 0, New Duration(TimeSpan.FromMilliseconds(1400))) With {
                .RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            })
        HaloScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            New System.Windows.Media.Animation.DoubleAnimation(1, 1.5, New Duration(TimeSpan.FromMilliseconds(1400))) With {
                .RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            })
        HaloScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            New System.Windows.Media.Animation.DoubleAnimation(1, 1.5, New Duration(TimeSpan.FromMilliseconds(1400))) With {
                .RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
            })
    End Sub

    Private Sub RestoreFromBall()
        If Ball Is Nothing Then Return
        Dim BallCenterX As Double = Ball.Left + Ball.Width / 2
        Dim BallCenterY As Double = Ball.Top + Ball.Height / 2
        Ball.Close()
        Ball = Nothing
        Dim Area As Rect = SystemParameters.WorkArea
        Left = Math.Max(Area.Left, Math.Min(BallCenterX - Width / 2, Area.Right - Width))
        Top = Math.Max(Area.Top, Math.Min(BallCenterY - Height / 2, Area.Bottom - Height))
        Dim TargetScaleX As Double = 52 / CardFullWidth
        Dim TargetScaleY As Double = 52 / CardFullHeight
        MainScale.BeginAnimation(ScaleTransform.ScaleXProperty, Nothing)
        MainScale.BeginAnimation(ScaleTransform.ScaleYProperty, Nothing)
        MainScale.ScaleX = TargetScaleX
        MainScale.ScaleY = TargetScaleY
        MainBorder.CornerRadius = New CornerRadius(26 / TargetScaleX)
        MainBorder.Effect = Nothing
        CardBrush.Color = AccentColor
        BallGlyph.Opacity = 1
        ContentPanel.Visibility = Visibility.Visible
        ContentPanel.Opacity = 0
        Show()
        WindowState = WindowState.Normal
        Activate()
        Dim Duration As New Duration(TimeSpan.FromMilliseconds(400))
        Dim Ease As New System.Windows.Media.Animation.CubicEase With {.EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut}
        MorphScaleFrom = TargetScaleX
        MorphScaleTo = 1
        MorphRadiusFrom = 26
        MorphRadiusTo = 7
        StartCornerMorph()
        CardBrush.BeginAnimation(SolidColorBrush.ColorProperty,
            New System.Windows.Media.Animation.ColorAnimation(AccentColor, CardColor, Duration) With {
                .BeginTime = TimeSpan.FromMilliseconds(60),
                .EasingFunction = Ease
            })
        BallGlyph.BeginAnimation(UIElement.OpacityProperty,
            New System.Windows.Media.Animation.DoubleAnimation(1, 0, New Duration(TimeSpan.FromMilliseconds(110))))
        ContentPanel.BeginAnimation(UIElement.OpacityProperty,
            New System.Windows.Media.Animation.DoubleAnimation(0, 1, New Duration(TimeSpan.FromMilliseconds(300))) With {
                .BeginTime = TimeSpan.FromMilliseconds(90)
            })
        MainScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            New System.Windows.Media.Animation.DoubleAnimation(TargetScaleX, 1, Duration) With {.EasingFunction = Ease})
        Dim ScaleAnimY As New System.Windows.Media.Animation.DoubleAnimation(TargetScaleY, 1, Duration) With {.EasingFunction = Ease}
        AddHandler ScaleAnimY.Completed, Sub() ResetCardState()
        MainScale.BeginAnimation(ScaleTransform.ScaleYProperty, ScaleAnimY)
    End Sub

    Private Sub CheckCancel()
        If IsCancelled Then Throw New LinkInviteCancelledException()
    End Sub

    Private Sub SetBusy(Busy As Boolean)
        RunInUi(
        Sub()
            IsBusy = Busy
            Loading.Visibility = If(Busy, Visibility.Visible, Visibility.Collapsed)
            BtnApply.IsEnabled = Not Busy
            If Busy Then
                BarProgress.Value = 0
                BarProgress.Visibility = Visibility.Visible
                PanLiveStatus.Visibility = Visibility.Visible
                SpeedSmooth = 0
                SyncIndex = -1
                SyncTotal = 0
                IsInstallPhase = False
                InstallStage = ""
                LabSpeed.Text = "速度 —"
                LabLiveMods.Text = ""
                StatusTimer.Start()
            Else
                BarProgress.Visibility = Visibility.Collapsed
                PanLiveStatus.Visibility = Visibility.Collapsed
                StatusTimer.Stop()
            End If
            FitCardHeight(Not Busy)
        End Sub)
    End Sub

    Private Sub FitCardHeight(AllowShrink As Boolean)
        Try
            If ContentPanel Is Nothing OrElse Not IsLoaded OrElse CardFullHeight <= 0 Then Return
            ContentPanel.Measure(New Size(CardFullWidth, Double.PositiveInfinity))
            Dim Needed As Double = Math.Ceiling(ContentPanel.DesiredSize.Height) + 6
            Needed = Math.Max(52, Needed)
            If Needed <= CardFullHeight + 1 Then
                If Not AllowShrink OrElse Needed >= CardFullHeight - 1 Then Return
            End If
            Dim Delta As Double = Needed - CardFullHeight
            CardFullHeight = Needed
            MainBorder.Height = Needed
            Height = Needed + 60
            Top -= Delta / 2
        Catch ex As Exception
            Logger.Warn(ex, "联机邀请：调整窗口高度失败")
        End Try
    End Sub

    Private Sub RefreshLiveStatus()
        If Not IsBusy Then Return
        Try
            Dim Raw As Long = NetManager.Speed
            If Raw > 0 Then
                SpeedSmooth = If(SpeedSmooth = 0, Raw, (SpeedSmooth * 2 + Raw) \ 3)
            Else
                SpeedSmooth = SpeedSmooth * 0.85
            End If
            If SpeedSmooth < 1024 Then SpeedSmooth = 0
            LabSpeed.Text = If(SpeedSmooth > 0, "速度 " & StringUtils.FormatByteSize(SpeedSmooth) & "/s", "速度 —")
            If IsInstallPhase Then
                Dim FileRemain As Integer = NetManager.FileRemain
                Dim Parts As New List(Of String)
                If Not String.IsNullOrEmpty(InstallStage) Then Parts.Add(InstallStage)
                If FileRemain > 0 Then Parts.Add("剩余 " & FileRemain & " 个文件")
                LabLiveMods.Text = Parts.Join("　")
            ElseIf SyncIndex >= 0 AndAlso SyncTotal > 0 Then
                LabLiveMods.Text = $"模组 {SyncIndex}/{SyncTotal}　剩余 {Math.Max(1, SyncTotal - SyncIndex + 1)} 个"
            Else
                LabLiveMods.Text = ""
            End If
        Catch ex As Exception
            Logger.Warn(ex, "联机邀请：读取下载速度失败")
        End Try
    End Sub

    Private Sub SetProgress(Text As String, Percent As Double)
        RunInUi(
        Sub()
            StatusBase = Text
            LabStatus.Text = Text
            Loading.Text = Text
            BarProgress.Visibility = Visibility.Visible
            BarProgress.Value = Math.Max(0, Math.Min(1, Percent))
            Logger.Info("[联机邀请] " & Text)
            FitCardHeight(False)
        End Sub)
    End Sub

    Private Sub SetStatus(Text As String)
        RunInUi(
        Sub()
            StatusBase = Text
            LabStatus.Text = Text
            Loading.Text = Text
            Logger.Info("[联机邀请] " & Text)
            FitCardHeight(False)
        End Sub)
    End Sub
End Class

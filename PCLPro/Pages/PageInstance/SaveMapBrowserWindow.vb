Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Web.WebView2.Wpf

Public Class SaveMapBrowserWindow
    Inherits Window

    Private ReadOnly View As New WebView2()
    Private ReadOnly LabStatus As New TextBlock With {
        .Foreground = Brushes.White,
        .FontSize = 15,
        .HorizontalAlignment = HorizontalAlignment.Center,
        .VerticalAlignment = VerticalAlignment.Center,
        .Text = "正在启动地图…"
    }

    Public Sub New(Url As String, SaveName As String)
        Title = $"3D 地图预览 - {SaveName}"
        Width = 1180
        Height = 760
        MinWidth = 640
        MinHeight = 420
        WindowStartupLocation = WindowStartupLocation.CenterOwner
        Background = New SolidColorBrush(Color.FromRgb(&H20, &H20, &H20))

        ModSaveBlueMap.EnsureWebViewLoader()
        View.CreationProperties = New CoreWebView2CreationProperties With {
            .UserDataFolder = Paths.Base & "PCL\Cache\BlueMap\WebView2"
        }
        Dim Root As New Grid()
        Root.Children.Add(View)
        Root.Children.Add(LabStatus)
        Content = Root

        AddHandler Loaded, Async Sub() Await LoadPageAsync(Url)
        AddHandler View.NavigationCompleted, Sub() LabStatus.Visibility = Visibility.Collapsed
        AddHandler Closed,
        Sub()
            Try
                View.Dispose()
            Catch ex As Exception
                Logger.Warn(ex, "释放 WebView2 失败")
            End Try
        End Sub
    End Sub

    Private Async Function LoadPageAsync(Url As String) As Task
        Try
            If String.IsNullOrEmpty(ModWebView2.GetRuntimeVersion()) Then
                LabStatus.Text = "首次使用需要安装 WebView2 运行时，正在准备…"
                Dim Installed As Boolean = Await Task.Run(Function() ModWebView2.EnsureRuntime(Sub(Text As String) RunInUi(Sub() LabStatus.Text = Text)))
                If Not Installed Then
                    LabStatus.Text = "WebView2 运行时安装失败"
                    Logger.Error("WebView2 运行时自动安装失败")
                    MyMsgBox("3D 地图预览需要 Microsoft Edge WebView2 运行时，自动下载安装失败。" & vbCrLf & vbCrLf &
                             "请手动下载安装后重试：" & vbCrLf &
                             "https://developer.microsoft.com/microsoft-edge/webview2/", "WebView2 运行时缺失", IsWarn:=True)
                    Return
                End If
            End If

            LabStatus.Text = "正在启动地图…"
            Await View.EnsureCoreWebView2Async()
            View.CoreWebView2.Settings.AreDevToolsEnabled = False
            View.CoreWebView2.Settings.IsStatusBarEnabled = False
            View.Source = New Uri(Url)
        Catch ex As Exception
            Logger.Error(ex, "WebView2 初始化失败")
            LabStatus.Text = "内置浏览器初始化失败：" & ex.Message
            MyMsgBox("内置浏览器初始化失败：" & vbCrLf & ex.Message, "3D 地图预览", IsWarn:=True)
        End Try
    End Function
End Class

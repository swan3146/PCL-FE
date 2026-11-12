Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Web.WebView2.Wpf

''' <summary>
''' 在启动器内部用 WebView2 显示 BlueMap 3D 地图。
''' </summary>
Public Class SaveMapBrowserWindow
    Inherits Window

    Private ReadOnly View As New WebView2()

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
        Content = View
        AddHandler Loaded, Async Sub() Await LoadPageAsync(Url)
        AddHandler Closed,
        Sub()
            ' 关闭窗口时释放 WebView2，避免后台进程继续占用内存
            Try
                View.Dispose()
            Catch ex As Exception
                Logger.Warn(ex, "释放 WebView2 失败")
            End Try
        End Sub
    End Sub

    Private Async Function LoadPageAsync(Url As String) As Task
        Try
            Await View.EnsureCoreWebView2Async()
            View.CoreWebView2.Settings.AreDevToolsEnabled = False
            View.CoreWebView2.Settings.IsStatusBarEnabled = False
            View.Source = New Uri(Url)
        Catch ex As Exception
            Logger.Error(ex, "WebView2 初始化失败，改用系统浏览器")
            Close()
            OpenWebsite(Url)
            Hint("内置浏览器不可用，已改用系统浏览器打开。", HintType.Red)
        End Try
    End Function
End Class

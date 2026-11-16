Imports Microsoft.Web.WebView2.Core

Public Module ModWebView2

    Public Function GetRuntimeVersion() As String
        Try
            Return If(CoreWebView2Environment.GetAvailableBrowserVersionString(), "")
        Catch
            Return ""
        End Try
    End Function

    Public Function EnsureRuntime(Optional Report As Action(Of String) = Nothing) As Boolean
        If Not String.IsNullOrEmpty(GetRuntimeVersion()) Then Return True

        Dim TempFolder As String = PathTemp & "WebView2\"
        DirectoryUtils.Create(TempFolder)
        Dim SetupPath As String = TempFolder & "MicrosoftEdgeWebview2Setup.exe"

        If Not FileUtils.Exists(SetupPath) Then
            Report?.Invoke("正在下载 WebView2 运行时…")
            Dim Urls As String() = {
                "https://go.microsoft.com/fwlink/p/?LinkId=2124701",
                "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
            }
            Dim Downloaded As Boolean = False
            For Each Url In Urls
                Try
                    Logger.Info($"正在下载 WebView2 运行时：{Url}")
                    NetDownloadByClient(Url, SetupPath)
                    If FileUtils.Exists(SetupPath) AndAlso New IO.FileInfo(SetupPath).Length > 100000 Then
                        Downloaded = True
                        Exit For
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, $"下载 WebView2 运行时失败（{Url}）")
                End Try
            Next
            If Not Downloaded Then
                Logger.Warn("WebView2 运行时下载失败")
                Return False
            End If
        End If

        Report?.Invoke("正在安装 WebView2 运行时…")
        RunInstaller(SetupPath, False)
        Thread.Sleep(1500)
        If Not String.IsNullOrEmpty(GetRuntimeVersion()) Then Return True

        Report?.Invoke("正在请求管理员权限安装 WebView2…")
        RunInstaller(SetupPath, True)
        Thread.Sleep(1500)
        Return Not String.IsNullOrEmpty(GetRuntimeVersion())
    End Function

    Private Sub RunInstaller(SetupPath As String, Elevate As Boolean)
        Try
            Dim StartInfo As New ProcessStartInfo(SetupPath, "/silent /install") With {
                .UseShellExecute = Elevate,
                .CreateNoWindow = Not Elevate
            }
            If Elevate Then StartInfo.Verb = "runas"
            Using ProcessItem As Process = Process.Start(StartInfo)
                If ProcessItem IsNot Nothing Then ProcessItem.WaitForExit(300000)
            End Using
        Catch ex As Exception
            Logger.Warn(ex, $"安装 WebView2 运行时失败（提权={Elevate}）")
        End Try
    End Sub

End Module

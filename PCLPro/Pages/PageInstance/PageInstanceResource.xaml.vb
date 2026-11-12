Public Class PageInstanceResource

    Private Sub PageEnterRefresh() Handles Me.PageEnter
        PanBack.ScrollToHome()
        RefreshContentUI()
    End Sub

#Region "卡片：内容管理"

    Private Sub RefreshContentUI()
        If LabContentScreenshot Is Nothing Then Return
        Try
            Dim PathIndie As String = PageInstanceLeft.Instance.PathIndie
            LabContentScreenshot.Text = ContentSummary(PathIndie & "screenshots\", {".png", ".jpg", ".jpeg", ".bmp", ".gif"})
            LabContentResourcePack.Text = ContentSummary(PathIndie & "resourcepacks\", {".zip"})
            LabContentShaderPack.Text = ContentSummary(PathIndie & "shaderpacks\", {".zip"})
            LabContentSchematic.Text = ContentSummary(PathIndie & "schematics\", {".litematic", ".schematic", ".schem", ".nbt"})
        Catch ex As Exception
            Logger.Warn(ex, "刷新内容管理信息失败")
        End Try
    End Sub

    Private Shared Function ContentSummary(Folder As String, Extensions As String()) As String
        Try
            If Not DirectoryUtils.Exists(Folder) Then Return "文件夹不存在"
            Dim Count As Integer = 0
            Dim Size As Long = 0
            For Each File As String In DirectoryUtils.EnumerateFiles(Folder)
                If Not Extensions.Contains(IO.Path.GetExtension(File).ToLowerInvariant()) Then Continue For
                Count += 1
                Size += New IO.FileInfo(File).Length
            Next
            If Count = 0 Then Return "暂无文件"
            Return $"{Count} 个文件 · {FormatSize(Size)}"
        Catch ex As Exception
            Logger.Warn(ex, $"统计内容文件夹失败（{Folder}）")
            Return "读取失败"
        End Try
    End Function

    Private Shared Function FormatSize(Size As Long) As String
        If Size >= 1073741824L Then Return $"{Math.Round(Size / 1073741824.0, 1)} GB"
        If Size >= 1048576L Then Return $"{Math.Round(Size / 1048576.0, 1)} MB"
        If Size >= 1024L Then Return $"{Math.Round(Size / 1024.0, 0)} KB"
        Return $"{Size} B"
    End Function

    Private Sub BtnContentScreenshotOpen_Click() Handles BtnContentScreenshotOpen.Click
        OpenContentFolder("screenshots")
    End Sub
    Private Sub BtnContentResourcePackOpen_Click() Handles BtnContentResourcePackOpen.Click
        OpenContentFolder("resourcepacks")
    End Sub
    Private Sub BtnContentShaderPackOpen_Click() Handles BtnContentShaderPackOpen.Click
        OpenContentFolder("shaderpacks")
    End Sub
    Private Sub BtnContentSchematicOpen_Click() Handles BtnContentSchematicOpen.Click
        OpenContentFolder("schematics")
    End Sub

    Private Sub OpenContentFolder(FolderName As String)
        Try
            Dim Folder As String = PageInstanceLeft.Instance.PathIndie & FolderName & "\"
            DirectoryUtils.Create(Folder)
            OpenExplorer(Folder)
        Catch ex As Exception
            Logger.Error(ex, $"打开内容文件夹失败（{FolderName}）", LogBehavior.Alert)
        End Try
    End Sub

    Private Sub BtnContentSchematicPreview_Click() Handles BtnContentSchematicPreview.Click
        Dim Folder As String = PageInstanceLeft.Instance.PathIndie & "schematics\"
        Dim FileName As String = ""
        Try
            DirectoryUtils.Create(Folder)
            FileName = Dialogs.SelectFile("选择投影原理图", False, Folder,
                                          {({"litematic", "schematic", "schem", "nbt"}, "投影文件")}).FirstOrDefault()
        Catch ex As Exception
            Logger.Error(ex, "选择投影原理图失败", LogBehavior.Alert)
            Return
        End Try
        If String.IsNullOrEmpty(FileName) Then Return
        Hint("正在读取投影文件…")
        RunInNewThread(
        Sub()
            Try
                Dim Info As NbtPreview = ReadNbtPreview(FileName)
                Dim Text As String = BuildSchematicText(FileName, Info)
                RunInUi(Sub() MyMsgBox(Text, "投影原理图预览", HighLight:=False))
            Catch ex As Exception
                Logger.Error(ex, $"读取投影原理图失败（{FileName}）")
                RunInUi(Sub() Hint("读取投影原理图失败：" & ex.Message, HintType.Red))
            End Try
        End Sub, "投影原理图预览")
    End Sub

    Private Shared Function BuildSchematicText(FileName As String, Info As NbtPreview) As String
        Dim Lines As New List(Of String)
        Dim FileSize As String = ""
        Try
            FileSize = FormatSize(New IO.FileInfo(FileName).Length)
        Catch
        End Try
        Lines.Add($"文件：{IO.Path.GetFileName(FileName)}" & If(FileSize = "", "", $"（{FileSize}）"))

        Dim Name As String = Info.FirstValue("Metadata/Name")
        If Name <> "" Then Lines.Add("名称：" & Name)
        Dim Author As String = Info.FirstValue("Metadata/Author")
        If Author <> "" Then Lines.Add("作者：" & Author)
        Dim Description As String = Info.FirstValue("Metadata/Description")
        If Description <> "" Then
            If Description.Length > 160 Then Description = Description.Substring(0, 160) & "…"
            Lines.Add("描述：" & Description.ReplaceLineEndings(" "))
        End If

        Dim SizeText As String = ""
        If Info.Regions.Count > 0 Then
            Dim Region As String = Info.Regions(0)
            Dim X As String = Info.FirstValue($"Regions/{Region}/Size/x")
            Dim Y As String = Info.FirstValue($"Regions/{Region}/Size/y")
            Dim Z As String = Info.FirstValue($"Regions/{Region}/Size/z")
            If X <> "" AndAlso Y <> "" AndAlso Z <> "" Then SizeText = $"{AbsNumber(X)} × {AbsNumber(Y)} × {AbsNumber(Z)}"
        End If
        If SizeText = "" Then SizeText = Info.FirstValue("size")
        If SizeText = "" Then
            Dim W As String = Info.FirstValue("Width", "Schematic/Width")
            Dim H As String = Info.FirstValue("Height", "Schematic/Height")
            Dim L As String = Info.FirstValue("Length", "Schematic/Length")
            If W <> "" AndAlso H <> "" AndAlso L <> "" Then SizeText = $"{AbsNumber(W)} × {AbsNumber(H)} × {AbsNumber(L)}"
        End If
        If SizeText <> "" Then Lines.Add($"尺寸：{SizeText}（宽 × 高 × 长，单位：方块）")

        Dim RegionCount As String = Info.FirstValue("Metadata/RegionCount")
        If RegionCount <> "" Then Lines.Add($"区域数：{RegionCount}")
        Dim TotalVolume As String = Info.FirstValue("Metadata/TotalVolume")
        If TotalVolume <> "" Then Lines.Add($"总体积：{TotalVolume} 方块")
        Dim TotalBlocks As String = Info.FirstValue("Metadata/TotalBlocks")
        If TotalBlocks <> "" Then Lines.Add($"方块总数：{TotalBlocks}")

        If Lines.Count = 1 Then Lines.Add("未能识别该投影文件的尺寸信息，可能不是标准的投影或结构文件。")
        Return String.Join(vbCrLf, Lines)
    End Function

    Private Shared Function AbsNumber(Text As String) As String
        Try
            Return Math.Abs(CLng(Text)).ToString()
        Catch
            Return Text
        End Try
    End Function

    Private Shared Function ReadNbtPreview(FileName As String) As NbtPreview
        Using Reader As New NbtReader(FileName)
            Try
                Reader.ReadRoot()
            Catch ex As StopPreviewException

            End Try
            Return Reader.Info
        End Using
    End Function

    Private Class NbtPreview
        Public ReadOnly Values As New Dictionary(Of String, String)
        Public ReadOnly Regions As New List(Of String)

        Public Sub SetValue(Path As String, Value As String)
            Values(Path) = Value
        End Sub

        Public Function FirstValue(ParamArray Paths() As String) As String
            For Each Path As String In Paths
                If Values.ContainsKey(Path) Then Return Values(Path)
            Next
            Return ""
        End Function
    End Class

    Private Class StopPreviewException
        Inherits Exception
    End Class

    Private Class NbtReader
        Implements IDisposable

        Private ReadOnly Strm As IO.Stream
        Public ReadOnly Info As New NbtPreview()

        Public Sub New(FileName As String)
            Dim Raw As IO.Stream = IO.File.OpenRead(FileName)
            Dim Head(1) As Byte
            Dim ReadCount As Integer = Raw.Read(Head, 0, 2)
            Raw.Position = 0
            If ReadCount = 2 AndAlso Head(0) = &H1F AndAlso Head(1) = &H8B Then
                Strm = New IO.Compression.GZipStream(Raw, IO.Compression.CompressionMode.Decompress)
            Else
                Strm = Raw
            End If
        End Sub

        Public Sub ReadRoot()
            Dim RootType As Byte = ReadByte()
            If RootType <> 10 Then Throw New IO.InvalidDataException("不是有效的 NBT 文件")
            ReadString()
            ReadCompound("")
        End Sub

        Private Sub ReadCompound(Path As String)
            Do
                Dim TagType As Byte = ReadByte()
                If TagType = 0 Then Exit Do
                Dim TagName As String = ReadString()
                Dim ChildPath As String = If(Path = "", TagName, Path & "/" & TagName)
                Select Case TagType
                    Case 1, 2, 3, 4
                        Info.SetValue(ChildPath, ReadNumber(If(TagType = 1, 1, If(TagType = 2, 2, If(TagType = 3, 4, 8)))).ToString())
                    Case 5, 6

                        Skip(TagType, 0)
                    Case 7
                        SkipBytes(ReadNumber(4))
                    Case 8
                        Dim Value As String = ReadString()
                        Info.SetValue(ChildPath, Value)

                        If (Path = "" OrElse Path = "Schematic") AndAlso TagName = "Length" Then Throw New StopPreviewException()
                    Case 9
                        Dim ItemType As Byte = ReadByte()
                        Dim Count As Long = ReadNumber(4)
                        If Count < 0 OrElse Count > 100000000L Then Throw New IO.InvalidDataException("NBT 列表长度异常")
                        If Path = "" AndAlso TagName = "size" AndAlso ItemType = 3 AndAlso Count >= 3 Then
                            Info.SetValue(ChildPath, $"{ReadNumber(4)} × {ReadNumber(4)} × {ReadNumber(4)}")
                            If Count = 3 Then Throw New StopPreviewException()
                            For i As Long = 4 To Count
                                Skip(ItemType, 1)
                            Next
                        Else
                            For i As Long = 1 To Count
                                Skip(ItemType, 1)
                            Next
                        End If
                    Case 10
                        If Path = "Regions" Then

                            Info.Regions.Add(TagName)
                            ReadCompound(ChildPath)
                            Throw New StopPreviewException()
                        Else
                            ReadCompound(ChildPath)
                        End If
                    Case 11
                        SkipBytes(ReadNumber(4) * 4)
                    Case 12
                        SkipBytes(ReadNumber(4) * 8)
                    Case Else
                        Throw New IO.InvalidDataException($"未知的 NBT 标签类型 {TagType}")
                End Select
            Loop
        End Sub

        Private Function ReadByte() As Byte
            Return ReadBytes(1)(0)
        End Function

        Private Function ReadNumber(Size As Integer) As Long
            Dim Buffer As Byte() = ReadBytes(Size)
            Dim Value As Long = 0
            For i As Integer = 0 To Size - 1
                Value = (Value << 8) Or Buffer(i)
            Next
            If Size = 2 AndAlso Value >= 32768L Then Value -= 65536L
            If Size = 4 AndAlso Value >= 2147483648L Then Value -= 4294967296L
            Return Value
        End Function

        Private Function ReadString() As String
            Dim Length As Integer = CInt(ReadNumber(2))
            If Length < 0 OrElse Length > 1048576 Then Throw New IO.InvalidDataException("NBT 字符串长度异常")
            If Length = 0 Then Return ""
            Return System.Text.Encoding.UTF8.GetString(ReadBytes(Length))
        End Function

        Private Function ReadBytes(Count As Integer) As Byte()
            Dim Buffer(Count - 1) As Byte
            Dim Done As Integer = 0
            While Done < Count
                Dim N As Integer = Strm.Read(Buffer, Done, Count - Done)
                If N <= 0 Then Throw New IO.EndOfStreamException()
                Done += N
            End While
            Return Buffer
        End Function

        Private Sub SkipBytes(Count As Long)
            If Count <= 0 Then Return
            Const ChunkSize As Integer = 4096
            Dim Buffer(ChunkSize - 1) As Byte
            While Count > 0
                Dim Want As Integer = CInt(Math.Min(Count, ChunkSize))

                Dim Done As Integer = 0
                While Done < Want
                    Dim N As Integer = Strm.Read(Buffer, Done, Want - Done)
                    If N <= 0 Then Throw New IO.EndOfStreamException()
                    Done += N
                End While
                Count -= Want
            End While
        End Sub

        Private Sub Skip(TagType As Byte, Depth As Integer)
            If Depth > 256 Then Throw New IO.InvalidDataException("NBT 嵌套层级过深")
            Select Case TagType
                Case 1, 2, 3, 4
                    ReadNumber(If(TagType = 1, 1, If(TagType = 2, 2, If(TagType = 3, 4, 8))))
                Case 5
                    ReadBytes(4)
                Case 6
                    ReadBytes(8)
                Case 7
                    SkipBytes(ReadNumber(4))
                Case 8
                    ReadString()
                Case 9
                    Dim ItemType As Byte = ReadByte()
                    Dim Count As Long = ReadNumber(4)
                    If Count < 0 OrElse Count > 100000000L Then Throw New IO.InvalidDataException("NBT 列表长度异常")
                    For i As Long = 1 To Count
                        Skip(ItemType, Depth + 1)
                    Next
                Case 10
                    Do
                        Dim ChildType As Byte = ReadByte()
                        If ChildType = 0 Then Exit Do
                        ReadString()
                        Skip(ChildType, Depth + 1)
                    Loop
                Case 11
                    SkipBytes(ReadNumber(4) * 4)
                Case 12
                    SkipBytes(ReadNumber(4) * 8)
                Case Else
                    Throw New IO.InvalidDataException($"未知的 NBT 标签类型 {TagType}")
            End Select
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Strm.Dispose()
        End Sub
    End Class

#End Region

End Class

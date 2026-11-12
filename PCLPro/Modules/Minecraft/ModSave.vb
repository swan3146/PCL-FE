Imports System.IO
Imports System.IO.Compression
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports Newtonsoft.Json.Linq

Public Module ModSave

#Region "存档信息"

    Public Class SaveInfo
        Public Path As String = ""
        Public FolderName As String = ""
        Public Name As String = ""
        Public VersionName As String = ""
        Public PlaySeconds As Long = -1
        Public Deaths As Long = -1
        Public DayTime As Long = -1
        Public LastPlayedTicks As Long = 0
        Public SizeBytes As Long = 0
        Public LevelDatTicks As Long = 0
        Public LevelDatSize As Long = 0
        Public SpawnX As Integer = 0
        Public SpawnZ As Integer = 0
        Public SeedText As String = ""
        Public Abandoned As Boolean = False

        Public ReadOnly Property PlayText As String
            Get
                If PlaySeconds < 0 Then Return "游玩时长未知"
                If PlaySeconds < 60 Then Return $"{PlaySeconds} 秒"
                Dim Hours As Long = PlaySeconds \ 3600
                Dim Minutes As Long = (PlaySeconds Mod 3600) \ 60
                If Hours > 0 Then Return $"{Hours} 小时 {Minutes} 分"
                Return $"{Minutes} 分钟"
            End Get
        End Property

        Public ReadOnly Property DeathsText As String
            Get
                If Deaths < 0 Then Return "死亡次数未知"
                Return $"死亡 {Deaths} 次"
            End Get
        End Property

        Public ReadOnly Property DaysText As String
            Get
                If DayTime < 0 Then Return "天数未知"
                Return $"第 {DayTime \ 24000 + 1} 天"
            End Get
        End Property

        Public ReadOnly Property LastPlayedText As String
            Get
                If LastPlayedTicks <= 0 Then Return "最后游玩时间未知"
                Return $"最后游玩 {New DateTime(LastPlayedTicks, DateTimeKind.Utc).ToLocalTime():yyyy-MM-dd HH:mm}"
            End Get
        End Property

        Public ReadOnly Property SizeText As String
            Get
                Return FormatSizeValue(SizeBytes)
            End Get
        End Property

        Public ReadOnly Property Summary1 As String
            Get
                Return $"{PlayText} · {DeathsText} · {DaysText}" & If(Abandoned, " · 疑似废弃", "")
            End Get
        End Property

        Public ReadOnly Property Summary2 As String
            Get
                Dim Parts As New List(Of String) From {LastPlayedText, SizeText}
                If Not String.IsNullOrWhiteSpace(VersionName) Then Parts.Add(VersionName)
                If Not String.IsNullOrWhiteSpace(SeedText) Then Parts.Add("种子 " & SeedText)
                Return String.Join(" · ", Parts)
            End Get
        End Property

        Public Function ToJson() As JObject
            Dim Result As New JObject
            Result("Path") = Path
            Result("FolderName") = FolderName
            Result("Name") = Name
            Result("VersionName") = VersionName
            Result("PlaySeconds") = PlaySeconds
            Result("Deaths") = Deaths
            Result("DayTime") = DayTime
            Result("LastPlayedTicks") = LastPlayedTicks
            Result("SizeBytes") = SizeBytes
            Result("LevelDatTicks") = LevelDatTicks
            Result("LevelDatSize") = LevelDatSize
            Result("SpawnX") = SpawnX
            Result("SpawnZ") = SpawnZ
            Result("SeedText") = SeedText
            Result("Abandoned") = Abandoned
            Return Result
        End Function

        Public Shared Function FromJson(Data As JObject) As SaveInfo
            Dim Info As New SaveInfo With {
                .Path = GetJsonString(Data, "Path"),
                .FolderName = GetJsonString(Data, "FolderName"),
                .Name = GetJsonString(Data, "Name"),
                .VersionName = GetJsonString(Data, "VersionName"),
                .PlaySeconds = GetJsonLong(Data, "PlaySeconds", -1),
                .Deaths = GetJsonLong(Data, "Deaths", -1),
                .DayTime = GetJsonLong(Data, "DayTime", -1),
                .LastPlayedTicks = GetJsonLong(Data, "LastPlayedTicks", 0),
                .SizeBytes = GetJsonLong(Data, "SizeBytes", 0),
                .LevelDatTicks = GetJsonLong(Data, "LevelDatTicks", 0),
                .LevelDatSize = GetJsonLong(Data, "LevelDatSize", 0),
                .SpawnX = CInt(GetJsonLong(Data, "SpawnX", 0)),
                .SpawnZ = CInt(GetJsonLong(Data, "SpawnZ", 0)),
                .SeedText = GetJsonString(Data, "SeedText"),
                .Abandoned = If(Data("Abandoned") Is Nothing, False, Data("Abandoned").ToObject(Of Boolean)())
            }
            Return Info
        End Function
    End Class

    Private Function GetJsonString(Data As JObject, Key As String) As String
        If Data(Key) Is Nothing Then Return ""
        Return Data(Key).ToString
    End Function

    Private Function GetJsonLong(Data As JObject, Key As String, DefaultValue As Long) As Long
        Try
            If Data(Key) Is Nothing Then Return DefaultValue
            Return Data(Key).ToObject(Of Long)()
        Catch
            Return DefaultValue
        End Try
    End Function

    Public Function FormatSizeValue(Size As Long) As String
        If Size >= 1073741824L Then Return $"{Math.Round(Size / 1073741824.0, 2)} GB"
        If Size >= 1048576L Then Return $"{Math.Round(Size / 1048576.0, 1)} MB"
        If Size >= 1024L Then Return $"{Math.Round(Size / 1024.0, 0)} KB"
        Return $"{Size} B"
    End Function

#End Region

#Region "扫描与缓存"

    Public ReadOnly Property SaveCacheFolder As String
        Get
            Return Paths.Base & "PCL\Cache\SavePreview\"
        End Get
    End Property

    Public Function GetCacheFile(InstancePath As String) As String
        Return Paths.Base & "PCL\Cache\SaveIndex\" & HashText(InstancePath.ToLowerInvariant()) & ".json"
    End Function

    Private Function HashText(Text As String) As String
        Using Md5 As MD5 = MD5.Create()
            Dim Bytes = Md5.ComputeHash(Encoding.UTF8.GetBytes(Text))
            Return BitConverter.ToString(Bytes).Replace("-", "").ToLowerInvariant()
        End Using
    End Function

    Public Function ScanSaves(Instance As McInstance) As List(Of SaveInfo)
        Dim SavesRoot As String = Instance.PathIndie & "saves\"
        If Not DirectoryUtils.Exists(SavesRoot) Then Return New List(Of SaveInfo)

        Dim CacheFile As String = GetCacheFile(Instance.PathIndie)
        Dim Cached As New Dictionary(Of String, SaveInfo)(StringComparer.OrdinalIgnoreCase)
        Try
            If FileUtils.Exists(CacheFile) Then
                Dim Json = TryCast(FileUtils.ReadAsJson(CacheFile), JObject)
                Dim CacheVersion As Integer = If(Json Is Nothing OrElse Json("Version") Is Nothing, 0, Json("Version").ToObject(Of Integer)())
                Dim Items = If(Json Is Nothing OrElse CacheVersion <> 2, Nothing, TryCast(Json("Saves"), JArray))
                If Items IsNot Nothing Then
                    For Each Token As JToken In Items
                        Try
                            Dim Info = SaveInfo.FromJson(CType(Token, JObject))
                            If Not String.IsNullOrEmpty(Info.Path) Then Cached(Info.Path) = Info
                        Catch ex As Exception
                            Logger.Warn(ex, "读取单条存档缓存失败")
                        End Try
                    Next
                End If
            End If
        Catch ex As Exception
            Logger.Warn(ex, "读取存档缓存失败")
        End Try

        Dim Folders As New List(Of String)
        Try
            Folders = DirectoryUtils.EnumerateDirectories(SavesRoot).ToList()
        Catch ex As Exception
            Logger.Warn(ex, "枚举存档文件夹失败")
            Return New List(Of SaveInfo)
        End Try

        Dim Result As New System.Collections.Concurrent.ConcurrentDictionary(Of String, SaveInfo)(StringComparer.OrdinalIgnoreCase)
        Parallel.ForEach(Folders, New ParallelOptions With {.MaxDegreeOfParallelism = 4},
        Sub(Folder)
            Try
                Dim LevelDatPath As String = IO.Path.Combine(Folder, "level.dat")
                If Not FileUtils.Exists(LevelDatPath) Then Return
                Dim LevelDat As New IO.FileInfo(LevelDatPath)
                Dim Old As SaveInfo = Nothing
                If Cached.TryGetValue(Folder, Old) AndAlso
                   Old.LevelDatTicks = LevelDat.LastWriteTimeUtc.Ticks AndAlso
                   Old.LevelDatSize = LevelDat.Length Then
                    Result(Folder) = Old
                    Return
                End If
                Result(Folder) = ParseSave(Folder, LevelDat)
            Catch ex As Exception
                Logger.Warn(ex, $"解析存档失败：{Folder}")
            End Try
        End Sub)

        Dim List = Result.Values.OrderByDescending(Function(S) S.LastPlayedTicks).ToList()
        Try
            Dim Json As New JObject
            Json("Version") = 2
            Dim Items As New JArray
            For Each Info In List
                Items.Add(Info.ToJson())
            Next
            Json("Saves") = Items
            DirectoryUtils.Create(PathUtils.RemoveLastPart(CacheFile))
            FileUtils.Write(CacheFile, Json.ToString(Newtonsoft.Json.Formatting.None))
        Catch ex As Exception
            Logger.Warn(ex, "保存存档缓存失败")
        End Try
        Logger.Info($"扫描到 {List.Count} 个存档（{Instance.Name}）")
        Return List
    End Function

    Private Function ParseSave(Folder As String, LevelDat As IO.FileInfo) As SaveInfo
        Dim Info As New SaveInfo With {
            .Path = Folder,
            .FolderName = IO.Path.GetFileName(Folder),
            .LevelDatTicks = LevelDat.LastWriteTimeUtc.Ticks,
            .LevelDatSize = LevelDat.Length
        }
        Try
            Dim Root = ReadNbtFile(LevelDat.FullName)
            Dim Data = TryCast(GetNbtValue(Root, "Data"), Dictionary(Of String, Object))
            If Data IsNot Nothing Then
                Info.Name = StripMinecraftFormatting(GetNbtString(Data, "LevelName"))
                Info.VersionName = ReadVersionName(Data)
                Info.DayTime = GetNbtLong(Data, "DayTime", -1)
                Dim LastPlayed As Long = GetNbtLong(Data, "LastPlayed", 0)
                If LastPlayed > 0 Then
                    Info.LastPlayedTicks = New DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(LastPlayed).Ticks
                End If
                Info.SpawnX = CInt(GetNbtLong(Data, "SpawnX", 0))
                Info.SpawnZ = CInt(GetNbtLong(Data, "SpawnZ", 0))
                Info.SeedText = ReadSeed(Data)
            End If
        Catch ex As Exception
            Logger.Warn(ex, $"读取 level.dat 失败：{Folder}")
        End Try
        If String.IsNullOrWhiteSpace(Info.Name) Then Info.Name = Info.FolderName
        If Info.LastPlayedTicks <= 0 Then Info.LastPlayedTicks = LevelDat.LastWriteTimeUtc.Ticks

        ReadStats(Folder, Info)

        Try
            Info.SizeBytes = GetFolderSize(Folder)
        Catch ex As Exception
            Logger.Warn(ex, $"统计存档大小失败：{Folder}")
        End Try

        Dim LastPlayedTime As DateTime = New DateTime(Info.LastPlayedTicks, DateTimeKind.Utc).ToLocalTime()
        Info.Abandoned = (Info.PlaySeconds >= 0 AndAlso Info.PlaySeconds < 600) OrElse
                         (Info.PlaySeconds >= 0 AndAlso Info.PlaySeconds < 3600 AndAlso LastPlayedTime < DateTime.Now.AddDays(-180))
        Return Info
    End Function

    Public Function StripMinecraftFormatting(Text As String) As String
        If String.IsNullOrEmpty(Text) OrElse Not Text.Contains("§") Then Return Text
        Dim Builder As New System.Text.StringBuilder(Text.Length)
        Dim i As Integer = 0
        While i < Text.Length
            If Text(i) = "§"c Then
                i += 2
            Else
                Builder.Append(Text(i))
                i += 1
            End If
        End While
        Return Builder.ToString()
    End Function

    Private Function ReadVersionName(Data As Dictionary(Of String, Object)) As String
        Dim VersionObj = GetNbtValue(Data, "Version")
        If TypeOf VersionObj Is String Then Return CStr(VersionObj)
        Dim VersionData = TryCast(VersionObj, Dictionary(Of String, Object))
        If VersionData Is Nothing Then Return ""
        Dim Name = GetNbtString(VersionData, "Name")
        If Name = "" Then Name = GetNbtString(VersionData, "Id")
        Return Name
    End Function

    Private Function ReadSeed(Data As Dictionary(Of String, Object)) As String
        Dim WorldGen = TryCast(GetNbtValue(Data, "WorldGenSettings"), Dictionary(Of String, Object))
        If WorldGen IsNot Nothing Then
            Dim SeedValue = GetNbtLong(WorldGen, "seed", Long.MinValue)
            If SeedValue <> Long.MinValue Then Return SeedValue.ToString()
        End If
        Dim RandomSeed = GetNbtLong(Data, "RandomSeed", Long.MinValue)
        If RandomSeed <> Long.MinValue Then Return RandomSeed.ToString()
        Return ""
    End Function

    Private Sub ReadStats(Folder As String, Info As SaveInfo)
        Try
            Dim StatsFolder As String = IO.Path.Combine(Folder, "stats")
            If Not DirectoryUtils.Exists(StatsFolder) Then Return
            Dim Files As New List(Of String)
            For Each File As String In DirectoryUtils.EnumerateFiles(StatsFolder, False, "*.json")
                Files.Add(File)
            Next
            If Files.Count = 0 Then Return
            Dim Target As String = Files.OrderByDescending(Function(F) IO.File.GetLastWriteTimeUtc(F)).First()
            Dim Json As JObject = TryCast(FileUtils.ReadAsJson(Target), JObject)
            If Json Is Nothing Then Return
            Dim Stats As JObject = TryCast(Json("stats"), JObject)
            If Stats Is Nothing Then Return
            Info.PlaySeconds = ReadStatTicks(Stats, {"minecraft:play_time", "stat.playOneMinute"})
            Info.Deaths = ReadStatValue(Stats, {"minecraft:deaths", "stat.deaths"})
        Catch ex As Exception
            Logger.Warn(ex, $"读取存档统计失败：{Folder}")
        End Try
    End Sub

    Private Function ReadStatTicks(Stats As JObject, Keys As String()) As Long
        For Each Key In Keys
            If Stats(Key) Is Nothing Then Continue For
            Try
                Dim Ticks As Long = Stats(Key).ToObject(Of Long)()
                Return Ticks \ 20
            Catch
            End Try
        Next
        Return -1
    End Function

    Private Function ReadStatValue(Stats As JObject, Keys As String()) As Long
        For Each Key In Keys
            If Stats(Key) Is Nothing Then Continue For
            Try
                Return Stats(Key).ToObject(Of Long)()
            Catch
            End Try
        Next
        Return -1
    End Function

    Private Function GetFolderSize(Folder As String) As Long
        Dim Total As Long = 0
        For Each File In DirectoryUtils.EnumerateFiles(Folder, True)
            Try
                Total += New IO.FileInfo(File).Length
            Catch
            End Try
        Next
        Return Total
    End Function

#End Region

#Region "NBT 读取"

    Public Function ReadNbtFile(FileName As String) As Dictionary(Of String, Object)
        Using Raw As IO.Stream = IO.File.OpenRead(FileName)
            Dim Head(1) As Byte
            Dim ReadCount As Integer = Raw.Read(Head, 0, 2)
            Raw.Position = 0
            Dim Strm As IO.Stream
            If ReadCount = 2 AndAlso Head(0) = &H1F AndAlso Head(1) = &H8B Then
                Strm = New GZipStream(Raw, CompressionMode.Decompress)
            ElseIf ReadCount = 2 AndAlso (Head(0) And &HF) = 8 Then
                Strm = New DeflateStream(Raw, CompressionMode.Decompress)
            Else
                Strm = Raw
            End If
            Using Strm
                Using Reader As New IO.BinaryReader(Strm, Encoding.UTF8, True)
                    Dim RootType As Byte = Reader.ReadByte()
                    If RootType <> 10 Then Return Nothing
                    ReadNbtString(Reader)
                    Return ReadNbtCompound(Reader, 0)
                End Using
            End Using
        End Using
    End Function

    Private Function ReadNbtCompound(Reader As IO.BinaryReader, Depth As Integer) As Dictionary(Of String, Object)
        Dim Result As New Dictionary(Of String, Object)(StringComparer.Ordinal)
        If Depth > 128 Then Return Result
        Do
            Dim TagType As Byte = Reader.ReadByte()
            If TagType = 0 Then Exit Do
            Dim TagName As String = ReadNbtString(Reader)
            If TagName = "Heightmaps" OrElse TagName = "entities" OrElse TagName = "block_entities" OrElse
               TagName = "structures" OrElse TagName = "block_ticks" OrElse TagName = "fluid_ticks" OrElse
               TagName = "PostProcessing" Then
                SkipNbtPayload(Reader, TagType)
            Else
                Result(TagName) = ReadNbtPayload(Reader, TagType, Depth)
            End If
        Loop
        Return Result
    End Function

    Private Function ReadNbtPayload(Reader As IO.BinaryReader, TagType As Byte, Depth As Integer) As Object
        Select Case TagType
            Case 1
                Return Reader.ReadByte()
            Case 2
                Return ReadI16BE(Reader)
            Case 3
                Return ReadI32BE(Reader)
            Case 4
                Return ReadI64BE(Reader)
            Case 5
                Dim Buffer As Byte() = Reader.ReadBytes(4)
                Array.Reverse(Buffer)
                Return BitConverter.ToSingle(Buffer, 0)
            Case 6
                Dim Buffer As Byte() = Reader.ReadBytes(8)
                Array.Reverse(Buffer)
                Return BitConverter.ToDouble(Buffer, 0)
            Case 7
                Dim Length As Integer = ReadI32BE(Reader)
                If Length < 0 Then Throw New IO.InvalidDataException("NBT 字节数组长度异常")
                Return Reader.ReadBytes(Length)
            Case 8
                Return ReadNbtString(Reader)
            Case 9
                Dim ItemType As Byte = Reader.ReadByte()
                Dim Count As Integer = ReadI32BE(Reader)
                If Count < 0 OrElse Count > 1000000 Then Throw New IO.InvalidDataException("NBT 列表长度异常")
                Dim List As New List(Of Object)(Math.Min(Count, 4096))
                For i As Integer = 1 To Count
                    If ItemType = 0 Then
                        List.Add(Nothing)
                    Else
                        List.Add(ReadNbtPayload(Reader, ItemType, Depth + 1))
                    End If
                Next
                Return List
            Case 10
                Return ReadNbtCompound(Reader, Depth + 1)
            Case 11
                Dim Length As Integer = ReadI32BE(Reader)
                If Length < 0 Then Throw New IO.InvalidDataException("NBT 整型数组长度异常")
                Dim Result(Length - 1) As Integer
                For i As Integer = 0 To Length - 1
                    Result(i) = ReadI32BE(Reader)
                Next
                Return Result
            Case 12
                Dim Length As Integer = ReadI32BE(Reader)
                If Length < 0 Then Throw New IO.InvalidDataException("NBT 长整型数组长度异常")
                Dim Result(Length - 1) As Long
                For i As Integer = 0 To Length - 1
                    Result(i) = ReadI64BE(Reader)
                Next
                Return Result
            Case Else
                Throw New IO.InvalidDataException($"未知的 NBT 标签类型 {TagType}")
        End Select
    End Function

    Private Function ReadI16BE(Reader As IO.BinaryReader) As Short
        Dim Buffer As Byte() = Reader.ReadBytes(2)
        If Buffer.Length < 2 Then Throw New IO.EndOfStreamException()
        Return BitConverter.ToInt16({Buffer(1), Buffer(0)}, 0)
    End Function

    Private Function ReadI32BE(Reader As IO.BinaryReader) As Integer
        Dim Buffer As Byte() = Reader.ReadBytes(4)
        If Buffer.Length < 4 Then Throw New IO.EndOfStreamException()
        Return BitConverter.ToInt32({Buffer(3), Buffer(2), Buffer(1), Buffer(0)}, 0)
    End Function

    Private Function ReadI64BE(Reader As IO.BinaryReader) As Long
        Dim Buffer As Byte() = Reader.ReadBytes(8)
        If Buffer.Length < 8 Then Throw New IO.EndOfStreamException()
        Return BitConverter.ToInt64({Buffer(7), Buffer(6), Buffer(5), Buffer(4), Buffer(3), Buffer(2), Buffer(1), Buffer(0)}, 0)
    End Function

    Private Function ReadU16BE(Reader As IO.BinaryReader) As Integer
        Dim Buffer As Byte() = Reader.ReadBytes(2)
        If Buffer.Length < 2 Then Throw New IO.EndOfStreamException()
        Return (CInt(Buffer(0)) << 8) Or CInt(Buffer(1))
    End Function

    Private Sub SkipBytes(Reader As IO.BinaryReader, Count As Integer)
        Dim Remaining As Integer = Count
        Dim Buffer(8191) As Byte
        While Remaining > 0
            Dim ReadCount As Integer = Reader.Read(Buffer, 0, Math.Min(Remaining, Buffer.Length))
            If ReadCount <= 0 Then Throw New IO.EndOfStreamException()
            Remaining -= ReadCount
        End While
    End Sub

    Private Sub SkipNbtPayload(Reader As IO.BinaryReader, TagType As Byte)
        Select Case TagType
            Case 1
                Reader.ReadByte()
            Case 2
                Reader.ReadBytes(2)
            Case 3
                Reader.ReadBytes(4)
            Case 4
                Reader.ReadBytes(8)
            Case 5
                Reader.ReadBytes(4)
            Case 6
                Reader.ReadBytes(8)
            Case 7
                SkipBytes(Reader, ReadI32BE(Reader))
            Case 8
                SkipBytes(Reader, ReadU16BE(Reader))
            Case 9
                Dim ItemType As Byte = Reader.ReadByte()
                Dim Count As Integer = ReadI32BE(Reader)
                For i As Integer = 1 To Count
                    SkipNbtPayload(Reader, ItemType)
                Next
            Case 10
                Do
                    Dim ChildType As Byte = Reader.ReadByte()
                    If ChildType = 0 Then Exit Do
                    SkipBytes(Reader, ReadU16BE(Reader))
                    SkipNbtPayload(Reader, ChildType)
                Loop
            Case 11
                SkipBytes(Reader, ReadI32BE(Reader) * 4)
            Case 12
                SkipBytes(Reader, ReadI32BE(Reader) * 8)
        End Select
    End Sub

    Private Function ReadNbtString(Reader As IO.BinaryReader) As String
        Dim Length As Integer = ReadU16BE(Reader)
        If Length = 0 Then Return ""
        Return Encoding.UTF8.GetString(Reader.ReadBytes(Length))
    End Function

    Private Function GetNbtValue(Data As Dictionary(Of String, Object), Key As String) As Object
        If Data Is Nothing Then Return Nothing
        Dim Value As Object = Nothing
        If Data.TryGetValue(Key, Value) Then Return Value
        Return Nothing
    End Function

    Private Function GetNbtString(Data As Dictionary(Of String, Object), Key As String) As String
        Dim Value = GetNbtValue(Data, Key)
        If Value Is Nothing Then Return ""
        Return Value.ToString()
    End Function

    Private Function GetNbtLong(Data As Dictionary(Of String, Object), Key As String, DefaultValue As Long) As Long
        Dim Value = GetNbtValue(Data, Key)
        If Value Is Nothing Then Return DefaultValue
        Try
            Return Convert.ToInt64(Value)
        Catch
            Return DefaultValue
        End Try
    End Function

#End Region

#Region "背包物品"

    Private ReadOnly LangLock As New Object
    Private ReadOnly LangCache As New Dictionary(Of String, Dictionary(Of String, String))(StringComparer.OrdinalIgnoreCase)

    Public Function GetLocalizedItemName(Instance As McInstance, ItemId As String) As String
        If Instance Is Nothing OrElse String.IsNullOrEmpty(ItemId) Then Return ""
        Dim Lang = GetLangData(Instance)
        If Lang Is Nothing Then Return ""
        Dim ShortId As String = ItemId
        If ShortId.StartsWith("minecraft:") Then ShortId = ShortId.Substring(10)
        For Each Key As String In {"item.minecraft." & ShortId, "block.minecraft." & ShortId}
            Dim Value As String = Nothing
            If Lang.TryGetValue(Key, Value) AndAlso Not String.IsNullOrWhiteSpace(Value) Then
                Return StripMinecraftFormatting(Value)
            End If
        Next
        Return ""
    End Function

    Private Function GetLangData(Instance As McInstance) As Dictionary(Of String, String)
        SyncLock LangLock
            Try
                Dim IndexName As String = McAssetsGetIndexName(Instance)
                If String.IsNullOrEmpty(IndexName) Then Return Nothing
                Dim CachedLang As Dictionary(Of String, String) = Nothing
                If LangCache.TryGetValue(IndexName, CachedLang) Then Return CachedLang
                Dim IndexPath As String = McFolderSelected & "assets\indexes\" & IndexName & ".json"
                If Not FileUtils.Exists(IndexPath) Then
                    Logger.Warn($"未找到资源索引文件，无法翻译物品名：{IndexPath}")
                    Return Nothing
                End If
                Dim IndexJson As JObject = TryCast(FileUtils.ReadAsJson(IndexPath), JObject)
                If IndexJson Is Nothing OrElse IndexJson("objects") Is Nothing Then Return Nothing
                For Each LangName As String In {"zh_cn", "en_us"}
                    Dim Entry = IndexJson("objects")("minecraft/lang/" & LangName & ".json")
                    If Entry Is Nothing OrElse Entry("hash") Is Nothing Then Continue For
                    Dim Hash As String = Entry("hash").ToString()
                    Dim ObjectPath As String = McFolderSelected & "assets\objects\" & Hash.Substring(0, 2) & "\" & Hash
                    If Not FileUtils.Exists(ObjectPath) Then Continue For
                    Dim LangJson As JObject = TryCast(FileUtils.ReadAsJson(ObjectPath), JObject)
                    If LangJson Is Nothing Then Continue For
                    Dim Result As New Dictionary(Of String, String)(StringComparer.Ordinal)
                    For Each Prop As JProperty In LangJson.Properties()
                        Result(Prop.Name) = If(Prop.Value.Type = Newtonsoft.Json.Linq.JTokenType.String, Prop.Value.ToString(), "")
                    Next
                    LangCache(IndexName) = Result
                    Return Result
                Next
            Catch ex As Exception
                Logger.Warn(ex, "读取游戏语言文件失败")
            End Try
            Return Nothing
        End SyncLock
    End Function

    Public Function ReadInventoryText(Info As SaveInfo, Optional Instance As McInstance = Nothing) As String
        Try
            Dim Items As List(Of Object) = Nothing
            Dim PlayerFile As String = ""

            Dim PlayerDataFolder As String = IO.Path.Combine(Info.Path, "playerdata")
            If DirectoryUtils.Exists(PlayerDataFolder) Then
                PlayerFile = DirectoryUtils.EnumerateFiles(PlayerDataFolder, searchPattern:="*.dat").
                    OrderByDescending(Function(F) IO.File.GetLastWriteTimeUtc(F)).FirstOrDefault()
            End If
            If Not String.IsNullOrEmpty(PlayerFile) Then
                Items = ReadInventoryFromNbt(PlayerFile)
            End If
            If Items Is Nothing Then
                Dim LevelFile As String = IO.Path.Combine(Info.Path, "level.dat")
                If FileUtils.Exists(LevelFile) Then
                    Dim Root = ReadNbtFile(LevelFile)
                    Dim Data = TryCast(GetNbtValue(Root, "Data"), Dictionary(Of String, Object))
                    Dim Player = TryCast(GetNbtValue(Data, "Player"), Dictionary(Of String, Object))
                    If Player IsNot Nothing Then Items = TryCast(GetNbtValue(Player, "Inventory"), List(Of Object))
                End If
            End If

            If Items Is Nothing Then Return "没有找到这个存档的玩家背包数据。"
            If Items.Count = 0 Then Return "玩家背包是空的。"

            Dim Lines As New List(Of String)
            Dim TotalCount As Long = 0
            For Each ItemObj In Items.
                OfType(Of Dictionary(Of String, Object))().
                OrderBy(Function(Item) GetNbtLong(Item, "Slot", GetNbtLong(Item, "slot", 999)))
                Dim ItemId As String = GetNbtString(ItemObj, "id")
                If ItemId = "" Then Continue For
                Dim Count As Long = Math.Max(1, GetNbtLong(ItemObj, "Count", GetNbtLong(ItemObj, "count", 1)))
                Dim Slot As Long = GetNbtLong(ItemObj, "Slot", GetNbtLong(ItemObj, "slot", -1))
                TotalCount += Count
                Dim DisplayName As String = ReadItemDisplayName(ItemObj)
                Dim ShortId As String = ItemId
                If ShortId.StartsWith("minecraft:") Then ShortId = ShortId.Substring(10)
                Dim LocalizedName As String = If(DisplayName = "", GetLocalizedItemName(Instance, ItemId), "")
                Dim Label As String = StripMinecraftFormatting(If(DisplayName <> "", DisplayName, If(LocalizedName <> "", LocalizedName, ShortId)))
                Lines.Add($"{If(Slot >= 0, $"槽位 {Slot}", "物品")}：{Label} × {Count}")
            Next
            If Lines.Count = 0 Then Return "玩家背包里没有可显示的物品。"
            Lines.Insert(0, $"共 {Lines.Count} 组物品，合计 {TotalCount} 个：")
            Return String.Join(vbCrLf, Lines)
        Catch ex As Exception
            Logger.Warn(ex, $"读取存档背包失败：{Info.Path}")
            Return "读取背包失败：" & ex.Message
        End Try
    End Function

    Private Function ReadInventoryFromNbt(FileName As String) As List(Of Object)
        Try
            Dim Root = ReadNbtFile(FileName)
            Dim Data = TryCast(GetNbtValue(Root, "Data"), Dictionary(Of String, Object))
            If Data Is Nothing Then Return Nothing
            Return TryCast(GetNbtValue(Data, "Inventory"), List(Of Object))
        Catch ex As Exception
            Logger.Warn(ex, $"读取玩家数据失败：{FileName}")
            Return Nothing
        End Try
    End Function

    Private Function ReadItemDisplayName(Item As Dictionary(Of String, Object)) As String
        Try
            Dim Tag = TryCast(GetNbtValue(Item, "tag"), Dictionary(Of String, Object))
            If Tag Is Nothing Then Return ""
            Dim Display = TryCast(GetNbtValue(Tag, "display"), Dictionary(Of String, Object))
            If Display Is Nothing Then Return ""
            Dim RawName As String = GetNbtString(Display, "Name")
            If RawName = "" Then Return ""
            If RawName.TrimStart().StartsWith("{") Then
                Try
                    Dim Json = Newtonsoft.Json.Linq.JObject.Parse(RawName)
                    If Json("text") IsNot Nothing Then Return Json("text").ToString()
                    If Json("translate") IsNot Nothing Then Return Json("translate").ToString()
                Catch
                End Try
            End If
            Return RawName
        Catch
            Return ""
        End Try
    End Function

#End Region

#Region "缩略图与俯视图"

    Public Function GetSaveIconPath(Info As SaveInfo) As String
        Dim IconPath As String = IO.Path.Combine(Info.Path, "icon.png")
        If FileUtils.Exists(IconPath) Then Return IconPath
        Return Nothing
    End Function

    Public Function LoadSaveIcon(Info As SaveInfo) As BitmapSource
        Try
            Dim IconPath As String = GetSaveIconPath(Info)
            If IconPath Is Nothing Then Return Nothing
            Return LoadBitmap(IconPath)
        Catch ex As Exception
            Logger.Warn(ex, $"加载存档图标失败：{Info.Path}")
            Return Nothing
        End Try
    End Function

    Public Function LoadCachedThumb(Info As SaveInfo) As BitmapSource
        Try
            Dim MapPath As String = GetMapCachePath(Info)
            If FileUtils.Exists(MapPath) Then
                Dim Map = LoadBitmap(MapPath)
                If Map IsNot Nothing AndAlso Not IsNearlyBlankBitmap(Map) Then Return Map
            End If
            Dim IconPath As String = GetSaveIconPath(Info)
            If IconPath Is Nothing Then Return Nothing
            Return LoadBitmap(IconPath)
        Catch ex As Exception
            Logger.Warn(ex, $"加载存档缩略图失败：{Info.Path}")
            Return Nothing
        End Try
    End Function

    Public Function GenerateMapPreview(Info As SaveInfo, ByRef Coverage As Integer) As BitmapSource
        Coverage = 0
        Try
            Dim MapPath As String = GetMapCachePath(Info)
            If FileUtils.Exists(MapPath) Then
                Dim CachedMap = LoadBitmap(MapPath)
                If CachedMap IsNot Nothing AndAlso Not IsNearlyBlankBitmap(CachedMap) Then
                    Coverage = 100
                    Return CachedMap
                End If
                Try
                    IO.File.Delete(MapPath)
                Catch
                End Try
            End If
            Dim Bmp As BitmapSource = RenderTopView(Info, 96, Coverage)
            If Bmp Is Nothing Then Return Nothing
            If Coverage >= 1 Then
                DirectoryUtils.Create(PathUtils.RemoveLastPart(MapPath))
                Using Strm As New IO.FileStream(MapPath, IO.FileMode.Create, IO.FileAccess.Write)
                    Dim Encoder As New PngBitmapEncoder()
                    Encoder.Frames.Add(BitmapFrame.Create(Bmp))
                    Encoder.Save(Strm)
                End Using
            End If
            Return Bmp
        Catch ex As Exception
            Logger.Warn(ex, $"生成存档俯视图失败：{Info.Path}")
            Return Nothing
        End Try
    End Function

    Private Function GetMapCachePath(Info As SaveInfo) As String
        Return SaveCacheFolder & HashText((Info.Path & "|" & Info.LastPlayedTicks & "|v4").ToLowerInvariant()) & "_map.png"
    End Function

    ''' <summary>
    ''' 判断图片是否几乎全是背景色（用来识别早期版本留下的空白地图缓存）。
    ''' </summary>
    Private Function IsNearlyBlankBitmap(Bmp As BitmapSource) As Boolean
        Try
            If Bmp Is Nothing Then Return True
            Dim Src As BitmapSource = Bmp
            If Src.Format <> PixelFormats.Bgra32 Then Src = New FormatConvertedBitmap(Bmp, PixelFormats.Bgra32, Nothing, 0)
            Dim Stride As Integer = Src.PixelWidth * 4
            Dim Data(Stride * Src.PixelHeight - 1) As Byte
            Src.CopyPixels(Data, Stride, 0)
            Dim Total As Integer = Src.PixelWidth * Src.PixelHeight
            Dim Differ As Integer = 0
            For i As Integer = 0 To Total - 1
                Dim Blue As Integer = Data(i * 4)
                Dim Green As Integer = Data(i * 4 + 1)
                Dim Red As Integer = Data(i * 4 + 2)
                If Math.Abs(Red - 191) > 8 OrElse Math.Abs(Green - 191) > 8 OrElse Math.Abs(Blue - 191) > 8 Then Differ += 1
            Next
            Return Differ * 100 < Total
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Function LoadBitmap(FileName As String, Optional DecodeWidth As Integer = 96) As BitmapSource
        Dim Img As New BitmapImage()
        Img.BeginInit()
        Img.CacheOption = BitmapCacheOption.OnLoad
        Img.CreateOptions = BitmapCreateOptions.IgnoreColorProfile
        If DecodeWidth > 0 Then Img.DecodePixelWidth = DecodeWidth
        Img.UriSource = New Uri(FileName, UriKind.Absolute)
        Img.EndInit()
        Img.Freeze()
        Return Img
    End Function

    Private Function RenderTopView(Info As SaveInfo, PixelSize As Integer, ByRef Coverage As Integer) As BitmapSource
        Coverage = 0
        Dim RegionFolder As String = IO.Path.Combine(Info.Path, "region")
        If Not DirectoryUtils.Exists(RegionFolder) Then Return Nothing
        Dim Pixels(PixelSize * PixelSize - 1) As Integer
        For i As Integer = 0 To Pixels.Length - 1
            Pixels(i) = &HFFBFBFBF
        Next

        Dim CenterChunkX As Integer = Info.SpawnX >> 4
        Dim CenterChunkZ As Integer = Info.SpawnZ >> 4
        Dim Half As Integer = PixelSize \ 2
        Dim Painted As Integer = 0
        Using RegionReader As New RegionFileReader(RegionFolder)
            For Px As Integer = 0 To PixelSize - 1
                For Pz As Integer = 0 To PixelSize - 1
                    Dim ChunkX As Integer = CenterChunkX - Half + Px
                    Dim ChunkZ As Integer = CenterChunkZ - Half + Pz
                    Dim Color As Integer? = RegionReader.GetTopColor(ChunkX, ChunkZ)
                    If Color.HasValue Then
                        Pixels(Pz * PixelSize + Px) = Color.Value
                        Painted += 1
                    End If
                Next
            Next
        End Using
        Coverage = CInt(Painted * 100.0 / Pixels.Length)

        Dim Bmp As BitmapSource = BitmapSource.Create(PixelSize, PixelSize, 96, 96, PixelFormats.Bgra32, Nothing, Pixels, PixelSize * 4)
        Bmp.Freeze()
        Return Bmp
    End Function

    Public Class TerrainMap
        Public Width As Integer
        Public Depth As Integer
        Public Heights As Integer()
        Public Colors As Integer()
        Public MinY As Integer
        Public MaxY As Integer
        Public Coverage As Integer
    End Class

    ''' <summary>
    ''' 读取出生点附近区域的地形，输出每个方块列的最高点高度与颜色，用于 3D 预览。
    ''' </summary>
    Public Function BuildTerrain(Info As SaveInfo, RadiusChunks As Integer, ByRef Coverage As Integer) As TerrainMap
        Coverage = 0
        Dim RegionFolder As String = IO.Path.Combine(Info.Path, "region")
        If Not DirectoryUtils.Exists(RegionFolder) Then Return Nothing
        RadiusChunks = Math.Max(1, Math.Min(16, RadiusChunks))
        Dim Side As Integer = RadiusChunks * 32
        Dim Map As New TerrainMap With {
            .Width = Side, .Depth = Side,
            .Heights = New Integer(Side * Side - 1) {},
            .Colors = New Integer(Side * Side - 1) {},
            .MinY = Integer.MaxValue, .MaxY = Integer.MinValue}
        For i As Integer = 0 To Map.Heights.Length - 1
            Map.Heights(i) = Integer.MinValue
        Next

        Dim CenterChunkX As Integer = Info.SpawnX >> 4
        Dim CenterChunkZ As Integer = Info.SpawnZ >> 4
        Dim StartChunkX As Integer = CenterChunkX - RadiusChunks
        Dim StartChunkZ As Integer = CenterChunkZ - RadiusChunks
        Dim Painted As Integer = 0
        Using RegionReader As New RegionFileReader(RegionFolder)
            For Cz As Integer = 0 To RadiusChunks * 2 - 1
                For Cx As Integer = 0 To RadiusChunks * 2 - 1
                    Dim Blocks = RegionReader.GetTopBlocks(StartChunkX + Cx, StartChunkZ + Cz)
                    If Blocks Is Nothing Then Continue For
                    For Lz As Integer = 0 To 15
                        For Lx As Integer = 0 To 15
                            Dim Block = Blocks(Lz * 16 + Lx)
                            If Not Block.HasBlock Then Continue For
                            Dim Index As Integer = (Cz * 16 + Lz) * Side + (Cx * 16 + Lx)
                            Map.Heights(Index) = Block.Y
                            Map.Colors(Index) = Block.Color
                            If Block.Y < Map.MinY Then Map.MinY = Block.Y
                            If Block.Y > Map.MaxY Then Map.MaxY = Block.Y
                            Painted += 1
                        Next
                    Next
                Next
            Next
        End Using
        If Painted = 0 Then Return Nothing
        Coverage = CInt(Painted * 100.0 / Map.Heights.Length)
        Map.Coverage = Coverage
        Return Map
    End Function

    Public Structure TopBlock
        Public HasBlock As Boolean
        Public Color As Integer
        Public Y As Integer
    End Structure

    Private Function GetTopBlocksFromChunk(Chunk As Dictionary(Of String, Object)) As TopBlock()
        Dim Sections As List(Of Object) = Nothing
        Dim Level = TryCast(GetNbtValue(Chunk, "Level"), Dictionary(Of String, Object))
        If Level IsNot Nothing Then
            Sections = TryCast(GetNbtValue(Level, "sections"), List(Of Object))
            If Sections Is Nothing Then Sections = TryCast(GetNbtValue(Level, "Sections"), List(Of Object))
        Else
            Sections = TryCast(GetNbtValue(Chunk, "sections"), List(Of Object))
        End If
        If Sections Is Nothing Then Return Nothing

        Dim Views As New List(Of Tuple(Of Integer, SectionView))
        For Each SectionObj In Sections.OfType(Of Dictionary(Of String, Object))()
            Dim SectionY As Integer = GetSectionY(SectionObj)
            Dim View = BuildSectionView(SectionObj)
            If View IsNot Nothing Then Views.Add(Tuple.Create(SectionY, View))
        Next
        If Views.Count = 0 Then Return Nothing
        Views.Sort(Function(A, B) B.Item1.CompareTo(A.Item1))

        Dim Result(255) As TopBlock
        For X As Integer = 0 To 15
            For Z As Integer = 0 To 15
                For Each Entry In Views
                    Dim Color As Integer = 0
                    Dim LocalY As Integer = GetViewColumn(Entry.Item2, X, Z, Color)
                    If LocalY >= 0 Then
                        Result(Z * 16 + X) = New TopBlock With {
                            .HasBlock = True, .Color = Color, .Y = Entry.Item1 * 16 + LocalY}
                        Exit For
                    End If
                Next
            Next
        Next
        Return Result
    End Function

    Private Function GetSectionY(Section As Dictionary(Of String, Object)) As Integer
        Dim SectionY As Integer = CInt(GetNbtLong(Section, "Y", 0))
        ' NBT 的 Byte 是无符号读取的，负数区段（-4 ~ -1）会变成 252 ~ 255
        If SectionY >= 128 AndAlso SectionY <= 255 Then SectionY -= 256
        Return SectionY
    End Function

    Private Function GetViewColumn(View As SectionView, X As Integer, Z As Integer, ByRef Color As Integer) As Integer
        If View.Blocks IsNot Nothing Then
            For Y As Integer = 15 To 0 Step -1
                Dim Id As Integer = View.Blocks(Y * 256 + Z * 16 + X)
                If Id <> 0 Then
                    Color = LegacyBlockColor(Id)
                    Return Y
                End If
            Next
            Return -1
        End If
        If View.Data Is Nothing Then
            If View.Palette Is Nothing OrElse View.Palette.Count <> 1 Then Return -1
            Color = BlockColor(GetPaletteName(View.Palette, 0))
            Return 15
        End If
        For Y As Integer = 15 To 0 Step -1
            Dim Index As Integer = Y * 256 + Z * 16 + X
            Dim PaletteIndex As Integer = ReadPackedIndex(View.Data, View.Bits, Index)
            Dim Name As String = GetPaletteName(View.Palette, PaletteIndex)
            If Name <> "" AndAlso Not IsAirBlock(Name) Then
                Color = BlockColor(Name)
                Return Y
            End If
        Next
        Return -1
    End Function

    Public Function GetTopColorFromChunk(Chunk As Dictionary(Of String, Object)) As Integer?
        Dim Sections As List(Of Object) = Nothing
        Dim Level = TryCast(GetNbtValue(Chunk, "Level"), Dictionary(Of String, Object))
        If Level IsNot Nothing Then
            Sections = TryCast(GetNbtValue(Level, "sections"), List(Of Object))
            If Sections Is Nothing Then Sections = TryCast(GetNbtValue(Level, "Sections"), List(Of Object))
        Else
            Sections = TryCast(GetNbtValue(Chunk, "sections"), List(Of Object))
        End If
        If Sections Is Nothing Then Return Nothing

        Dim Views As New List(Of SectionView)
        For Each SectionObj In Sections.OfType(Of Dictionary(Of String, Object))().
            OrderByDescending(Function(S) GetSectionY(S))
            Dim View = BuildSectionView(SectionObj)
            If View IsNot Nothing Then Views.Add(View)
        Next
        If Views.Count = 0 Then Return Nothing

        Dim Counts As New Dictionary(Of Integer, Integer)
        For X As Integer = 0 To 15
            For Z As Integer = 0 To 15
                For Each View In Views
                    Dim Color = GetViewColor(View, X, Z)
                    If Color.HasValue Then
                        If Counts.ContainsKey(Color.Value) Then
                            Counts(Color.Value) += 1
                        Else
                            Counts(Color.Value) = 1
                        End If
                        Exit For
                    End If
                Next
            Next
        Next
        If Counts.Count = 0 Then Return Nothing
        Return Counts.OrderByDescending(Function(P) P.Value).First().Key
    End Function

    Private Class SectionView
        Public Palette As List(Of Object)
        Public Data As Long()
        Public Blocks As Byte()
        Public Bits As Integer = 4
    End Class

    Private Function BuildSectionView(Section As Dictionary(Of String, Object)) As SectionView
        Dim View As New SectionView
        View.Blocks = TryCast(GetNbtValue(Section, "Blocks"), Byte())
        If View.Blocks IsNot Nothing Then Return View

        Dim StatesObj = TryCast(GetNbtValue(Section, "block_states"), Dictionary(Of String, Object))
        If StatesObj IsNot Nothing Then
            View.Palette = TryCast(GetNbtValue(StatesObj, "palette"), List(Of Object))
            View.Data = TryCast(GetNbtValue(StatesObj, "data"), Long())
        Else
            View.Palette = TryCast(GetNbtValue(Section, "Palette"), List(Of Object))
            View.Data = TryCast(GetNbtValue(Section, "BlockStates"), Long())
        End If
        If View.Palette Is Nothing OrElse View.Palette.Count = 0 Then Return Nothing
        If View.Data Is Nothing AndAlso View.Palette.Count = 1 Then
            Dim OnlyName As String = GetPaletteName(View.Palette, 0)
            If OnlyName = "" OrElse IsAirBlock(OnlyName) Then Return Nothing
        End If
        View.Bits = Math.Max(4, CInt(Math.Ceiling(Math.Log(View.Palette.Count, 2))))
        Return View
    End Function

    Private Function GetViewColor(View As SectionView, X As Integer, Z As Integer) As Integer?
        If View.Blocks IsNot Nothing Then
            For Y As Integer = 15 To 0 Step -1
                Dim Id As Integer = View.Blocks(Y * 256 + Z * 16 + X)
                If Id <> 0 Then Return LegacyBlockColor(Id)
            Next
            Return Nothing
        End If

        For Y As Integer = 15 To 0 Step -1
            Dim Index As Integer = Y * 256 + Z * 16 + X
            Dim PaletteIndex As Integer = ReadPackedIndex(View.Data, View.Bits, Index)
            Dim Name As String = GetPaletteName(View.Palette, PaletteIndex)
            If Name <> "" AndAlso Not IsAirBlock(Name) Then Return BlockColor(Name)
        Next
        Return Nothing
    End Function

    Private Function ReadPackedIndex(Data As Long(), Bits As Integer, Index As Integer) As Integer
        If Data Is Nothing OrElse Data.Length = 0 Then Return 0
        Dim PerLong As Integer = 64 \ Bits
        Dim LongIndex As Integer = Index \ PerLong
        If LongIndex < 0 OrElse LongIndex >= Data.Length Then Return 0
        Dim Shift As Integer = (Index Mod PerLong) * Bits
        Dim Value As Long = Data(LongIndex)
        Dim Raw As ULong = CULng(Value And Long.MaxValue) Or If(Value < 0, &H8000000000000000UL, 0UL)
        Dim Mask As ULong = (1UL << Bits) - 1UL
        Return CInt((Raw >> Shift) And Mask)
    End Function

    Private Function GetPaletteName(Palette As List(Of Object), Index As Integer) As String
        If Index < 0 OrElse Index >= Palette.Count Then Return ""
        Dim Entry = TryCast(Palette(Index), Dictionary(Of String, Object))
        If Entry Is Nothing Then Return ""
        Return GetNbtString(Entry, "Name")
    End Function

    Private Function IsAirBlock(Name As String) As Boolean
        Return Name = "minecraft:air" OrElse Name = "minecraft:cave_air" OrElse Name = "minecraft:void_air" OrElse Name = "air"
    End Function

    Private Function BlockColor(Name As String) As Integer
        Dim ShortName As String = Name
        If ShortName.StartsWith("minecraft:") Then ShortName = ShortName.Substring(10)

        Select Case True
            Case ShortName.Contains("water") : Return &HFF3F76D4
            Case ShortName.Contains("lava") : Return &HFFCF5A12
            Case ShortName = "grass_block" OrElse ShortName = "grass" OrElse ShortName.Contains("moss") : Return &HFF5D9C3F
            Case ShortName.Contains("podzol") OrElse ShortName.Contains("mycelium") : Return &HFF6B5A55
            Case ShortName.Contains("snow") : Return &HFFF0F8FF
            Case ShortName.Contains("ice") : Return &HFF9FC7E8
            Case ShortName.Contains("sand") : Return &HFFDBCFA3
            Case ShortName.Contains("gravel") : Return &HFF8A8683
            Case ShortName.Contains("deepslate") OrElse ShortName.Contains("blackstone") OrElse ShortName.Contains("basalt") : Return &HFF4F4F52
            Case ShortName.Contains("bedrock") : Return &HFF333333
            Case ShortName.Contains("leaves") OrElse ShortName.Contains("vine") : Return &HFF3E7A2E
            Case ShortName.Contains("log") OrElse ShortName.Contains("wood") OrElse ShortName.Contains("planks") : Return &HFF8A6A3B
            Case ShortName.Contains("terracotta") : Return &HFF9C5A3C
            Case ShortName.Contains("netherrack") OrElse ShortName.Contains("nether") : Return &HFF6E2B2B
            Case ShortName.Contains("end_stone") : Return &HFFDDDE9F
            Case ShortName.Contains("clay") : Return &HFFA0A8B8
            Case ShortName.Contains("stone") OrElse ShortName.Contains("cobble") OrElse ShortName.Contains("andesite") OrElse ShortName.Contains("diorite") OrElse ShortName.Contains("granite") : Return &HFF7F7F7F
            Case ShortName.Contains("dirt") OrElse ShortName.Contains("farmland") OrElse ShortName.Contains("path") : Return &HFF79553A
            Case ShortName.Contains("cactus") : Return &HFF4F8F3F
            Case ShortName.Contains("magma") : Return &HFFC24A0F
            Case ShortName.Contains("obsidian") : Return &HFF14101E
            Case ShortName.Contains("prismarine") : Return &HFF63A89A
            Case ShortName.Contains("coral") : Return &HFFDF7F7F
            Case Else : Return &HFF6E8B4E
        End Select
    End Function

    Private Function LegacyBlockColor(Id As Integer) As Integer
        Select Case Id
            Case 1, 4, 98 : Return &HFF7F7F7F
            Case 2 : Return &HFF5D9C3F
            Case 3, 60 : Return &HFF79553A
            Case 7 : Return &HFF333333
            Case 8, 9 : Return &HFF3F76D4
            Case 10, 11 : Return &HFFCF5A12
            Case 12, 24 : Return &HFFDBCFA3
            Case 13 : Return &HFF8A8683
            Case 17 : Return &HFF8A6A3B
            Case 18 : Return &HFF3E7A2E
            Case 78, 80 : Return &HFFF0F8FF
            Case 79 : Return &HFF9FC7E8
            Case 82 : Return &HFFA0A8B8
            Case 87 : Return &HFF6E2B2B
            Case 89 : Return &HFF8F8F5F
            Case 95 : Return &HFFDDDE9F
            Case 110 : Return &HFF6B5A55
            Case 111 : Return &HFF5D9C3F
            Case 121 : Return &HFFDDDE9F
            Case 49 : Return &HFF14101E
            Case Else : Return &HFF6E8B4E
        End Select
    End Function

    Private Class RegionFileReader
        Implements IDisposable

        Private ReadOnly Folder As String
        Private ReadOnly Streams As New Dictionary(Of String, IO.FileStream)
        Private ReadOnly Offsets As New Dictionary(Of String, Integer())

        Public Sub New(Folder As String)
            Me.Folder = Folder
        End Sub

        Public Function GetTopColor(ChunkX As Integer, ChunkZ As Integer) As Integer?
            Dim ChunkData = ReadChunk(ChunkX, ChunkZ)
            If ChunkData Is Nothing Then Return Nothing
            Return GetTopColorFromChunk(ChunkData)
        End Function

        Public Function GetTopBlocks(ChunkX As Integer, ChunkZ As Integer) As TopBlock()
            Dim ChunkData = ReadChunk(ChunkX, ChunkZ)
            If ChunkData Is Nothing Then Return Nothing
            Return GetTopBlocksFromChunk(ChunkData)
        End Function

        Private Function ReadChunk(ChunkX As Integer, ChunkZ As Integer) As Dictionary(Of String, Object)
            Try
                Dim RegionX As Integer = ChunkX >> 5
                Dim RegionZ As Integer = ChunkZ >> 5
                Dim LocalX As Integer = ChunkX And 31
                Dim LocalZ As Integer = ChunkZ And 31
                Dim Key As String = $"{RegionX}.{RegionZ}"
                Dim Strm As IO.FileStream = Nothing
                If Not Streams.TryGetValue(Key, Strm) Then
                    Dim FileName As String = IO.Path.Combine(Folder, $"r.{RegionX}.{RegionZ}.mca")
                    If Not FileUtils.Exists(FileName) Then
                        Streams(Key) = Nothing
                        Return Nothing
                    End If
                    Strm = New IO.FileStream(FileName, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.Read)
                    Streams(Key) = Strm
                    Dim Header(4095) As Byte
                    If Strm.Read(Header, 0, 4096) <> 4096 Then
                        Offsets(Key) = New Integer() {}
                        Return Nothing
                    End If
                    Dim Table(1023) As Integer
                    For i As Integer = 0 To 1023
                        Table(i) = (CInt(Header(i * 4)) << 16) Or (CInt(Header(i * 4 + 1)) << 8) Or CInt(Header(i * 4 + 2))
                    Next
                    Offsets(Key) = Table
                End If
                If Strm Is Nothing Then Return Nothing
                Dim OffsetTable = Offsets(Key)
                Dim Offset As Integer = OffsetTable(LocalX + LocalZ * 32)
                If Offset <= 0 Then Return Nothing
                Strm.Position = CLng(Offset) * 4096
                Dim LengthBytes(3) As Byte
                If Strm.Read(LengthBytes, 0, 4) <> 4 Then Return Nothing
                Dim Length As Integer = (CInt(LengthBytes(0)) << 24) Or (CInt(LengthBytes(1)) << 16) Or
                                        (CInt(LengthBytes(2)) << 8) Or CInt(LengthBytes(3))
                If Length <= 1 OrElse Length > 10485760 Then Return Nothing
                Dim CompressionType As Byte = CByte(Strm.ReadByte())
                Dim Compressed(Length - 2) As Byte
                If Strm.Read(Compressed, 0, Compressed.Length) <> Compressed.Length Then Return Nothing
                Dim ChunkData As Dictionary(Of String, Object)
                Using Memory As New IO.MemoryStream(Compressed)
                    Dim Decompressed As IO.Stream
                    If CompressionType = 1 Then
                        Decompressed = New GZipStream(Memory, CompressionMode.Decompress)
                    ElseIf CompressionType = 2 Then
                        If Memory.Length > 2 Then Memory.Position = 2
                        Decompressed = New DeflateStream(Memory, CompressionMode.Decompress)
                    Else
                        Decompressed = Memory
                    End If
                    Using Decompressed
                        Using Reader As New IO.BinaryReader(Decompressed, Encoding.UTF8, True)
                            Dim RootType As Byte = Reader.ReadByte()
                            If RootType <> 10 Then Return Nothing
                            ReadNbtString(Reader)
                            ChunkData = ReadNbtCompound(Reader, 0)
                        End Using
                    End Using
                End Using
                Return ChunkData
            Catch ex As Exception
                Return Nothing
            End Try
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            For Each Strm In Streams.Values
                If Strm IsNot Nothing Then Strm.Dispose()
            Next
            Streams.Clear()
        End Sub
    End Class

#End Region

End Module

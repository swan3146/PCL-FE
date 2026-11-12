''' <summary>
''' 显示第三方组件的完整许可证文本。
''' </summary>
Public Class ThirdPartyLicenseWindow
    Inherits Window

    Private Const BlueMapLicense As String =
        "BlueMap — MIT License" & vbCrLf &
        "Copyright (c) Blue <https://www.bluecolored.de>" & vbCrLf &
        "Copyright (c) contributors" & vbCrLf & vbCrLf &
        "Permission is hereby granted, free of charge, to any person obtaining a copy" & vbCrLf &
        "of this software and associated documentation files (the ""Software""), to deal" & vbCrLf &
        "in the Software without restriction, including without limitation the rights" & vbCrLf &
        "to use, copy, modify, merge, publish, distribute, sublicense, and/or sell" & vbCrLf &
        "copies of the Software, and to permit persons to whom the Software is" & vbCrLf &
        "furnished to do so, subject to the following conditions:" & vbCrLf & vbCrLf &
        "The above copyright notice and this permission notice shall be included in all" & vbCrLf &
        "copies or substantial portions of the Software." & vbCrLf & vbCrLf &
        "THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR" & vbCrLf &
        "IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY," & vbCrLf &
        "FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE" & vbCrLf &
        "AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER" & vbCrLf &
        "LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM," & vbCrLf &
        "OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE" & vbCrLf &
        "SOFTWARE."

    Private Const WebView2License As String =
        "Microsoft Edge WebView2 SDK" & vbCrLf &
        "Copyright (C) Microsoft Corporation. All rights reserved." & vbCrLf & vbCrLf &
        "Redistribution and use in source and binary forms, with or without" & vbCrLf &
        "modification, are permitted provided that the following conditions are" & vbCrLf &
        "met:" & vbCrLf & vbCrLf &
        "  * Redistributions of source code must retain the above copyright" & vbCrLf &
        "notice, this list of conditions and the following disclaimer." & vbCrLf &
        "  * Redistributions in binary form must reproduce the above" & vbCrLf &
        "copyright notice, this list of conditions and the following disclaimer" & vbCrLf &
        "in the documentation and/or other materials provided with the" & vbCrLf &
        "distribution." & vbCrLf &
        "  * The name of Microsoft Corporation, or the names of its contributors" & vbCrLf &
        "may not be used to endorse or promote products derived from this" & vbCrLf &
        "software without specific prior written permission." & vbCrLf & vbCrLf &
        "THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS" & vbCrLf &
        """AS IS"" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT" & vbCrLf &
        "LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR" & vbCrLf &
        "A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT" & vbCrLf &
        "OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL," & vbCrLf &
        "SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT" & vbCrLf &
        "LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE," & vbCrLf &
        "DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY" & vbCrLf &
        "THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT" & vbCrLf &
        "(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE" & vbCrLf &
        "OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE."

    Public Sub New()
        Title = "第三方组件许可证"
        Width = 760
        Height = 620
        WindowStartupLocation = WindowStartupLocation.CenterOwner
        Dim Text As New TextBox With {
            .Text = BlueMapLicense & vbCrLf & vbCrLf & New String("-"c, 60) & vbCrLf & vbCrLf & WebView2License,
            .IsReadOnly = True,
            .TextWrapping = TextWrapping.Wrap,
            .VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            .HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            .FontFamily = New FontFamily("Consolas, Microsoft YaHei UI"),
            .FontSize = 12,
            .Padding = New Thickness(16),
            .BorderThickness = New Thickness(0),
            .Background = New SolidColorBrush(Color.FromRgb(&HF7, &HF7, &HF7))
        }
        Content = Text
    End Sub
End Class

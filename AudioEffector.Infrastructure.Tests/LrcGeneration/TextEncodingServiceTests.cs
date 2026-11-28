using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.LrcGeneration;

namespace AudioEffector.Infrastructure.Tests.LrcGeneration;

public sealed class TextEncodingServiceTests : IDisposable
{
    private readonly string _tempDir;

    static TextEncodingServiceTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public TextEncodingServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffector_TextEncTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    /// <summary>
    /// BOM なし UTF-8 テキストが変換不要の UTF-8 として判定され、承諾フラグ false でもそのまま読み込めることを検証します。
    /// </summary>
    [Fact]
    public async Task InspectAndRead_Utf8Bomなしファイル_変換不要でそのまま読み込める()
    {
        // Arrange
        var sut = new TextEncodingService();
        string path = Path.Combine(_tempDir, "utf8.txt");
        string expected = "夜空の星\n明日へ進む";
        await File.WriteAllBytesAsync(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(expected));

        // Act
        TextEncodingInspectionResult inspection = await sut.InspectAsync(path);
        string content = await sut.ReadAsUtf8TextAsync(path, allowEncodingConversion: false);

        // Assert
        Assert.True(inspection.IsUtf8);
        Assert.False(inspection.HasUtf8Bom);
        Assert.False(inspection.RequiresConversion);
        Assert.Equal("UTF-8", inspection.DetectedEncodingName);
        Assert.Equal(2, inspection.NonEmptyLineCount);
        Assert.Equal(expected, content);
    }

    /// <summary>
    /// Shift-JIS テキストが非 UTF-8 として検出され、未承諾時は拒否され、承諾時は元ファイルを破壊せずに UTF-8 読み込みできることを検証します。
    /// </summary>
    [Fact]
    public async Task ReadAsUtf8TextAsync_ShiftJisファイル_未承諾は例外となり承諾時は元ファイル非破壊でデコードされる()
    {
        // Arrange
        var sut = new TextEncodingService();
        string path = Path.Combine(_tempDir, "sjis.txt");
        string expected = "春の風が吹く\r\n桜の花びらが舞う";
        byte[] sjisBytes = Encoding.GetEncoding(932).GetBytes(expected);
        await File.WriteAllBytesAsync(path, sjisBytes);

        // Act
        TextEncodingInspectionResult inspection = await sut.InspectAsync(path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ReadAsUtf8TextAsync(path, allowEncodingConversion: false));
        string decoded = await sut.ReadAsUtf8TextAsync(path, allowEncodingConversion: true);
        byte[] bytesAfterRead = await File.ReadAllBytesAsync(path);

        // Assert
        Assert.False(inspection.IsUtf8);
        Assert.True(inspection.RequiresConversion);
        Assert.Equal("Shift-JIS (CP932)", inspection.DetectedEncodingName);
        Assert.Equal(expected, decoded);
        Assert.Equal(sjisBytes, bytesAfterRead);
    }

    /// <summary>
    /// EUC-JP テキストが EUC-JP として検出され、承諾後に文字化けなくデコードされることを検証します。
    /// </summary>
    [Fact]
    public async Task InspectAndRead_EucJpファイル_EucJpとして検出され承諾後に正しくデコードされる()
    {
        // Arrange
        var sut = new TextEncodingService();
        string path = Path.Combine(_tempDir, "eucjp.txt");
        string expected = "あいうえお\nかきくけこ\n静かな夜のメロディ";
        byte[] eucBytes = Encoding.GetEncoding(51932).GetBytes(expected);
        await File.WriteAllBytesAsync(path, eucBytes);

        // Act
        TextEncodingInspectionResult inspection = await sut.InspectAsync(path);
        string decoded = await sut.ReadAsUtf8TextAsync(path, allowEncodingConversion: true);

        // Assert
        Assert.False(inspection.IsUtf8);
        Assert.True(inspection.RequiresConversion);
        Assert.Equal("EUC-JP", inspection.DetectedEncodingName);
        Assert.Equal(expected, decoded);
    }

    /// <summary>
    /// UTF-16 LE (BOM 付き) テキストが非 UTF-8 として検出され、承諾後に正しくデコードされることを検証します。
    /// </summary>
    [Fact]
    public async Task InspectAndRead_Utf16LeBom付きファイル_非Utf8として検出され承諾後にデコードされる()
    {
        // Arrange
        var sut = new TextEncodingService();
        string path = Path.Combine(_tempDir, "utf16.txt");
        string expected = "星の海を越えて";
        await File.WriteAllTextAsync(path, expected, Encoding.Unicode);

        // Act
        TextEncodingInspectionResult inspection = await sut.InspectAsync(path);
        string decoded = await sut.ReadAsUtf8TextAsync(path, allowEncodingConversion: true);

        // Assert
        Assert.False(inspection.IsUtf8);
        Assert.Equal("UTF-16 LE", inspection.DetectedEncodingName);
        Assert.Equal(expected, decoded);
    }

    /// <summary>
    /// LRC ファイル保存時に UTF-8 (BOM なし) で書き出され、既存ファイルありで上書き未許可時は例外、許可時は安全に上書きされることを検証します。
    /// </summary>
    [Fact]
    public async Task SaveLrcFileAsync_新規保存および上書き制御_UTF8Bomなしで保存し未許可上書きを拒否する()
    {
        // Arrange
        var sut = new TextEncodingService();
        string lrcPath = Path.Combine(_tempDir, "song.lrc");
        string firstContent = "[00:01.00]最初の歌詞";
        string secondContent = "[00:02.00]更新後の歌詞";

        // Act
        await sut.SaveLrcFileAsync(lrcPath, firstContent, overwrite: false);
        byte[] firstBytes = await File.ReadAllBytesAsync(lrcPath);
        await Assert.ThrowsAsync<IOException>(() => sut.SaveLrcFileAsync(lrcPath, secondContent, overwrite: false));
        await sut.SaveLrcFileAsync(lrcPath, secondContent, overwrite: true);
        string finalContent = await File.ReadAllTextAsync(lrcPath, new UTF8Encoding(false));
        string[] leftoverTemps = Directory.GetFiles(_tempDir, "*.tmp");

        // Assert
        Assert.False(firstBytes.Length >= 3 && firstBytes[0] == 0xEF && firstBytes[1] == 0xBB && firstBytes[2] == 0xBF);
        Assert.Equal(secondContent, finalContent);
        Assert.Empty(leftoverTemps);
    }
}

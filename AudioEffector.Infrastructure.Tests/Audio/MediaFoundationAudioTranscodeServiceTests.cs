using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Infrastructure.Audio;
using Xunit;

namespace AudioEffector.Infrastructure.Tests.Audio;

/// <summary>
/// MediaFoundationAudioTranscodeService および TranscodedAudioResult の変換判定・ファイル名解決・一時ファイル後始末を検証するテストクラス
/// </summary>
public sealed class MediaFoundationAudioTranscodeServiceTests
{
    /// <summary>
    /// ConvertMp4ToM4aおよびConvertFlacToMp3オプションと拡張子の組み合わせに応じてNeedsTranscodeとResolveTargetFileNameが期待通り動作することを検証します。
    /// </summary>
    [Theory]
    [InlineData(@"C:\Music\01_Song.mp4", true, false, true, "01_Song.m4a")]
    [InlineData(@"C:\Music\01_Song.MP4", true, false, true, "01_Song.m4a")]
    [InlineData(@"C:\Music\01_Song.mp4", false, true, false, "01_Song.mp4")]
    [InlineData(@"C:\Music\02_Song.flac", true, false, false, "02_Song.flac")]
    [InlineData(@"C:\Music\02_Song.flac", false, true, true, "02_Song.mp3")]
    [InlineData(@"C:\Music\02_Song.FLAC", true, true, true, "02_Song.mp3")]
    [InlineData(@"C:\Music\03_Song.mp3", true, true, false, "03_Song.mp3")]
    public void NeedsTranscodeおよびResolveTargetFileName_拡張子と設定の組み合わせ_期待通りの判定とファイル名を返すこと(
        string sourcePath,
        bool convertMp4ToM4a,
        bool convertFlacToMp3,
        bool expectedNeedsTranscode,
        string expectedTargetFileName)
    {
        // Arrange
        var sut = new MediaFoundationAudioTranscodeService();
        var options = new AudioTranscodeOptions
        {
            ConvertMp4ToM4a = convertMp4ToM4a,
            ConvertFlacToMp3 = convertFlacToMp3
        };

        // Act
        bool needsTranscode = sut.NeedsTranscode(sourcePath, options);
        string targetFileName = sut.ResolveTargetFileName(sourcePath, options);

        // Assert
        Assert.Equal(expectedNeedsTranscode, needsTranscode);
        Assert.Equal(expectedTargetFileName, targetFileName);
    }

    /// <summary>
    /// 変換対象外ファイルに対してPrepareForTransferAsyncを実行した場合、元ファイルパスをそのまま返すPassthrough結果となりDisposeしても元ファイルが削除されないことを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task PrepareForTransferAsync_変換対象外ファイルの場合_元ファイルをそのまま返しDispose後も削除されないこと()
    {
        // Arrange
        string tempFile = Path.Combine(Path.GetTempPath(), $"AE_Pass_{Guid.NewGuid():N}.mp3");
        File.WriteAllText(tempFile, "dummy");
        var sut = new MediaFoundationAudioTranscodeService();
        var options = new AudioTranscodeOptions { ConvertMp4ToM4a = true };

        try
        {
            // Act
            using (var result = await sut.PrepareForTransferAsync(tempFile, options))
            {
                // Assert
                Assert.False(result.WasTranscoded);
                Assert.Equal(tempFile, result.OutputFilePath);
                Assert.Equal(Path.GetFileName(tempFile), result.TargetFileName);
                Assert.Null(result.TemporaryDirectoryPath);
            }

            Assert.True(File.Exists(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>
    /// TranscodedAudioResultのDispose時に、変換生成された一時ファイルおよび一時ディレクトリが確実にクリーンアップされることを検証します。
    /// </summary>
    [Fact]
    public void TranscodedAudioResult_Dispose時_一時ファイルおよび一時ディレクトリが確実に削除されること()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AE_Transcode_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string tempFile = Path.Combine(tempDir, "01_Song.m4a");
        File.WriteAllText(tempFile, "transcoded");
        var sut = new TranscodedAudioResult(
            sourceFilePath: @"C:\Music\01_Song.mp4",
            outputFilePath: tempFile,
            targetFileName: "01_Song.m4a",
            wasTranscoded: true,
            temporaryDirectoryPath: tempDir);

        // Act
        sut.Dispose();
        sut.Dispose(); // 二重Disposeの冪等性も確認

        // Assert
        Assert.False(File.Exists(tempFile));
        Assert.False(Directory.Exists(tempDir));
    }

    /// <summary>
    /// 変換対象ファイルが存在しない場合およびキャンセル済みトークン指定時に適切な例外が送出されることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task PrepareForTransferAsync_存在しないファイルやキャンセル済みトークン指定時_適切な例外を送出すること()
    {
        // Arrange
        var sut = new MediaFoundationAudioTranscodeService();
        var options = new AudioTranscodeOptions { ConvertMp4ToM4a = true };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => sut.PrepareForTransferAsync(@"C:\NonExistent\missing.mp4", options));

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => sut.PrepareForTransferAsync(@"C:\NonExistent\missing.flac", new AudioTranscodeOptions { ConvertFlacToMp3 = true }));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.PrepareForTransferAsync(@"C:\NonExistent\missing.mp4", options, cts.Token));
    }
}

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Infrastructure.Audio;
using NAudio.Wave;
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

    /// <summary>
    /// RequiresMp3Resampleがサンプリングレート・ビット深度・チャンネル数・エンコーディングの各種境界条件を正しく判定することを検証します。
    /// </summary>
    [Theory]
    [InlineData(44100, 16, 2, WaveFormatEncoding.Pcm, false)]
    [InlineData(48000, 16, 2, WaveFormatEncoding.Pcm, false)]
    [InlineData(44100, 16, 1, WaveFormatEncoding.Pcm, false)]
    [InlineData(48001, 16, 2, WaveFormatEncoding.Pcm, true)]
    [InlineData(96000, 24, 2, WaveFormatEncoding.Pcm, true)]
    [InlineData(44100, 8, 2, WaveFormatEncoding.Pcm, true)]
    [InlineData(44100, 24, 2, WaveFormatEncoding.Pcm, true)]
    [InlineData(44100, 16, 6, WaveFormatEncoding.Pcm, true)]
    [InlineData(44100, 32, 2, WaveFormatEncoding.IeeeFloat, true)]
    [InlineData(44100, 16, 2, WaveFormatEncoding.Extensible, true)]
    public void RequiresMp3Resample_各種音声フォーマット境界条件_期待通りのリサンプル要否を返すこと(
        int sampleRate,
        int bitsPerSample,
        int channels,
        WaveFormatEncoding encoding,
        bool expectedRequiresResample)
    {
        // Arrange
        WaveFormat format = CreateTestWaveFormat(sampleRate, bitsPerSample, channels, encoding);

        // Act
        bool actual = MediaFoundationAudioTranscodeService.RequiresMp3Resample(format);

        // Assert
        Assert.Equal(expectedRequiresResample, actual);
    }

    /// <summary>
    /// RequiresMp3Resampleにnullを渡した場合、ArgumentNullExceptionを送出することを検証します。
    /// </summary>
    [Fact]
    public void RequiresMp3Resample_フォーマットがnullの場合_ArgumentNullExceptionを送出すること()
    {
        // Arrange
        WaveFormat? format = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => MediaFoundationAudioTranscodeService.RequiresMp3Resample(format!));
    }

    /// <summary>
    /// BuildMp3ResampleTargetFormatが入力フォーマットを16bit PCM・最大48kHz・1〜2chへ正規化することを検証します。
    /// </summary>
    [Theory]
    [InlineData(96000, 24, 2, WaveFormatEncoding.Pcm, 48000, 16, 2)]
    [InlineData(48000, 24, 2, WaveFormatEncoding.Pcm, 48000, 16, 2)]
    [InlineData(44100, 8, 2, WaveFormatEncoding.Pcm, 44100, 16, 2)]
    [InlineData(44100, 16, 6, WaveFormatEncoding.Pcm, 44100, 16, 2)]
    [InlineData(44100, 32, 1, WaveFormatEncoding.IeeeFloat, 44100, 16, 1)]
    [InlineData(192000, 24, 6, WaveFormatEncoding.Pcm, 48000, 16, 2)]
    public void BuildMp3ResampleTargetFormat_多様な入力フォーマット_16bitPCM最大48kHz最大2chへ正規化されること(
        int sourceSampleRate,
        int sourceBitsPerSample,
        int sourceChannels,
        WaveFormatEncoding sourceEncoding,
        int expectedSampleRate,
        int expectedBitsPerSample,
        int expectedChannels)
    {
        // Arrange
        WaveFormat sourceFormat = CreateTestWaveFormat(sourceSampleRate, sourceBitsPerSample, sourceChannels, sourceEncoding);

        // Act
        WaveFormat actual = MediaFoundationAudioTranscodeService.BuildMp3ResampleTargetFormat(sourceFormat);

        // Assert
        Assert.Equal(WaveFormatEncoding.Pcm, actual.Encoding);
        Assert.Equal(expectedSampleRate, actual.SampleRate);
        Assert.Equal(expectedBitsPerSample, actual.BitsPerSample);
        Assert.Equal(expectedChannels, actual.Channels);
    }

    /// <summary>
    /// BuildMp3ResampleTargetFormatにnullを渡した場合、ArgumentNullExceptionを送出することを検証します。
    /// </summary>
    [Fact]
    public void BuildMp3ResampleTargetFormat_フォーマットがnullの場合_ArgumentNullExceptionを送出すること()
    {
        // Arrange
        WaveFormat? sourceFormat = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => MediaFoundationAudioTranscodeService.BuildMp3ResampleTargetFormat(sourceFormat!));
    }

    /// <summary>
    /// BuildMp3ResampleTargetFormatにサンプルレートが0以下のフォーマットを渡した場合、ArgumentOutOfRangeExceptionを送出することを検証します。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-44100)]
    public void BuildMp3ResampleTargetFormat_サンプルレートが0以下の場合_ArgumentOutOfRangeExceptionを送出すること(int invalidSampleRate)
    {
        // Arrange
        WaveFormat sourceFormat = new WaveFormat(invalidSampleRate, 16, 2);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => MediaFoundationAudioTranscodeService.BuildMp3ResampleTargetFormat(sourceFormat));
    }

    private static WaveFormat CreateTestWaveFormat(
        int sampleRate,
        int bitsPerSample,
        int channels,
        WaveFormatEncoding encoding)
    {
        if (encoding == WaveFormatEncoding.IeeeFloat)
        {
            return WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }

        if (encoding == WaveFormatEncoding.Pcm)
        {
            return new WaveFormat(sampleRate, bitsPerSample, channels);
        }

        int blockAlign = channels * (bitsPerSample / 8);
        int averageBytesPerSecond = sampleRate * blockAlign;
        return WaveFormat.CreateCustomFormat(
            encoding,
            sampleRate,
            channels,
            averageBytesPerSecond,
            blockAlign,
            bitsPerSample);
    }
}

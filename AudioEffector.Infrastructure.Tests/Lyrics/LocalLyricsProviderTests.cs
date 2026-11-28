using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure;
using AudioEffector.Infrastructure.Lyrics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AudioEffector.Infrastructure.Tests.Lyrics;

/// <summary>
/// LocalLyricsProvider の外部 .lrc 優先探索・文字コードフォールバック・埋め込み歌詞フォールバック・例外安全性・DI 解決を検証するテストクラスです。
/// </summary>
public sealed class LocalLyricsProviderTests : IDisposable
{
    private readonly string _tempDirectory;

    static LocalLyricsProviderTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public LocalLyricsProviderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_Lyrics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // クリーンアップ失敗は無視
        }
    }

    private string CreateDummyWavFile(string fileName, string? embeddedLyrics = null)
    {
        string filePath = Path.Combine(_tempDirectory, fileName);
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write("RIFF"u8.ToArray());
            bw.Write(36);
            bw.Write("WAVE"u8.ToArray());
            bw.Write("fmt "u8.ToArray());
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)2);
            bw.Write(44100);
            bw.Write(44100 * 4);
            bw.Write((short)4);
            bw.Write((short)16);
            bw.Write("data"u8.ToArray());
            bw.Write(0);
        }

        if (embeddedLyrics is not null)
        {
            using var tagFile = TagLib.File.Create(filePath);
            tagFile.Tag.Lyrics = embeddedLyrics;
            tagFile.Save();
        }

        return filePath;
    }

    private static Track CreateTrack(string filePath)
    {
        return new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(filePath),
            title: Path.GetFileNameWithoutExtension(filePath),
            artist: "Test Artist",
            album: "Test Album",
            duration: TimeSpan.FromSeconds(180));
    }

    /// <summary>
    /// 同階層・同名の UTF-8（BOM 有無）外部 .lrc ファイルが存在する場合、埋め込み歌詞より優先して読み込まれることを検証します。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadLyricsAsync_外部UTF8Lrcファイルが存在する_埋め込み歌詞より優先して読み込まれること(bool withBom)
    {
        // Arrange
        string wavPath = CreateDummyWavFile($"utf8_bom_{withBom}.wav", embeddedLyrics: "[00:01.00]埋め込み側の歌詞");
        string lrcPath = Path.ChangeExtension(wavPath, ".lrc");
        string lrcContent = "[ti:外部タイトル]\n[00:05.00]外部LRCの歌詞";
        await File.WriteAllTextAsync(lrcPath, lrcContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom));

        var sut = new LocalLyricsProvider(new LrcParser());
        Track track = CreateTrack(wavPath);

        // Act
        LyricsDocument? actual = await sut.LoadLyricsAsync(track);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(LyricsSourceType.LrcFile, actual.SourceType);
        Assert.Equal(lrcPath, actual.SourcePath);
        Assert.True(actual.IsSynced);
        Assert.Equal("外部タイトル", actual.Title);
        Assert.Single(actual.Lines);
        Assert.Equal("外部LRCの歌詞", actual.Lines[0].Text);
    }

    /// <summary>
    /// Shift-JIS（CP932）で保存された外部 .lrc ファイルが UTF-8 デコード不可時に CP932 へフォールバックして文字化けなく読み込まれることを検証します。
    /// </summary>
    [Fact]
    public async Task LoadLyricsAsync_ShiftJISのLrcファイル_CP932へフォールバックして正常に読み込まれること()
    {
        // Arrange
        string wavPath = CreateDummyWavFile("sjis_song.wav");
        string lrcPath = Path.ChangeExtension(wavPath, ".lrc");
        Encoding shiftJis = Encoding.GetEncoding(932);
        byte[] sjisBytes = shiftJis.GetBytes("[00:10.00]日本語の歌詞テスト\r\n[00:20.00]文字化けしないこと");
        await File.WriteAllBytesAsync(lrcPath, sjisBytes);

        var sut = new LocalLyricsProvider(new LrcParser());
        Track track = CreateTrack(wavPath);

        // Act
        LyricsDocument? actual = await sut.LoadLyricsAsync(track);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(LyricsSourceType.LrcFile, actual.SourceType);
        Assert.True(actual.IsSynced);
        Assert.Equal(2, actual.Lines.Count);
        Assert.Equal("日本語の歌詞テスト", actual.Lines[0].Text);
        Assert.Equal("文字化けしないこと", actual.Lines[1].Text);
    }

    /// <summary>
    /// 外部 .lrc が存在しない場合、音声ファイル埋め込み歌詞タグへフォールバックし、タイムタグの有無に応じて同期／非同期を正しく判定することを検証します。
    /// </summary>
    [Theory]
    [InlineData("[00:08.50]同期埋め込み歌詞", true, "同期埋め込み歌詞")]
    [InlineData("プレーン埋め込み歌詞1行目\nプレーン埋め込み歌詞2行目", false, "プレーン埋め込み歌詞1行目")]
    public async Task LoadLyricsAsync_外部Lrc不在かつ埋め込み歌詞あり_埋め込み歌詞へフォールバックすること(
        string embeddedText,
        bool expectedSynced,
        string expectedFirstLine)
    {
        // Arrange
        string wavPath = CreateDummyWavFile($"embedded_{expectedSynced}.wav", embeddedLyrics: embeddedText);
        var sut = new LocalLyricsProvider(new LrcParser());
        Track track = CreateTrack(wavPath);

        // Act
        LyricsDocument? actual = await sut.LoadLyricsAsync(track);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(LyricsSourceType.EmbeddedTag, actual.SourceType);
        Assert.Equal(wavPath, actual.SourcePath);
        Assert.Equal(expectedSynced, actual.IsSynced);
        Assert.Equal(expectedFirstLine, actual.Lines[0].Text);
    }

    /// <summary>
    /// 外部 .lrc ファイルが空またはメタデータのみで表示可能な歌詞行を含まない場合、埋め込み歌詞タグへフォールバックすることを検証します。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("[ti:タイトルのみ]\n[ar:アーティストのみ]\n[offset:100]")]
    public async Task LoadLyricsAsync_外部Lrcが空またはメタデータのみ_埋め込み歌詞へフォールバックすること(string invalidExternalContent)
    {
        // Arrange
        string wavPath = CreateDummyWavFile($"fallback_{Guid.NewGuid():N}.wav", embeddedLyrics: "[00:03.00]フォールバック成功");
        string lrcPath = Path.ChangeExtension(wavPath, ".lrc");
        await File.WriteAllTextAsync(lrcPath, invalidExternalContent, new UTF8Encoding(false));

        var sut = new LocalLyricsProvider(new LrcParser());
        Track track = CreateTrack(wavPath);

        // Act
        LyricsDocument? actual = await sut.LoadLyricsAsync(track);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(LyricsSourceType.EmbeddedTag, actual.SourceType);
        Assert.Equal("フォールバック成功", actual.Lines[0].Text);
    }

    /// <summary>
    /// 外部 .lrc ファイルが排他ロック等で読み取れない場合、例外を投げずに埋め込み歌詞タグへフォールバックすることを検証します。
    /// </summary>
    [Fact]
    public async Task LoadLyricsAsync_外部Lrcがロックされ読取不可_例外を投げず埋め込み歌詞へフォールバックすること()
    {
        // Arrange
        string wavPath = CreateDummyWavFile("locked_external.wav", embeddedLyrics: "ロック時フォールバック歌詞");
        string lrcPath = Path.ChangeExtension(wavPath, ".lrc");
        await File.WriteAllTextAsync(lrcPath, "[00:01.00]読めない外部LRC", new UTF8Encoding(false));

        var sut = new LocalLyricsProvider(new LrcParser());
        Track track = CreateTrack(wavPath);

        using var lockStream = new FileStream(lrcPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // Act
        LyricsDocument? actual = await sut.LoadLyricsAsync(track);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(LyricsSourceType.EmbeddedTag, actual.SourceType);
        Assert.Equal("ロック時フォールバック歌詞", actual.Lines[0].Text);
    }

    /// <summary>
    /// 外部 .lrc ファイルが他プロセスによって書き込み共有モードで開かれている状態でも、共有違反にならず正常に読み込まれることを検証します。
    /// </summary>
    [Fact]
    public async Task LoadLyricsAsync_外部Lrcが書き込み共有ロック中_共有違反とならず正常に読み込まれること()
    {
        // Arrange
        string wavPath = CreateDummyWavFile("shared_write_external.wav", embeddedLyrics: "埋め込み側");
        string lrcPath = Path.ChangeExtension(wavPath, ".lrc");
        await File.WriteAllTextAsync(lrcPath, "[00:05.00]書き込み共有中の外部LRC", new UTF8Encoding(false));

        var sut = new LocalLyricsProvider(new LrcParser());
        Track track = CreateTrack(wavPath);

        using var sharedWriteStream = new FileStream(lrcPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

        // Act
        LyricsDocument? actual = await sut.LoadLyricsAsync(track);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(LyricsSourceType.LrcFile, actual.SourceType);
        Assert.Equal("書き込み共有中の外部LRC", actual.Lines[0].Text);
    }

    /// <summary>
    /// 外部 .lrc も埋め込み歌詞も存在しない場合や破損ファイルの場合、例外を投げず null を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LoadLyricsAsync_歌詞未存在または破損ファイル_例外を投げずnullを返すこと()
    {
        // Arrange
        string noLyricsWav = CreateDummyWavFile("no_lyrics.wav");
        string corruptedPath = Path.Combine(_tempDirectory, "corrupted.mp3");
        await File.WriteAllBytesAsync(corruptedPath, [0x00, 0xFF, 0xAA, 0x55]);
        string notExistPath = Path.Combine(_tempDirectory, "not_exist.flac");

        var sut = new LocalLyricsProvider(new LrcParser());

        // Act
        LyricsDocument? resultNoLyrics = await sut.LoadLyricsAsync(CreateTrack(noLyricsWav));
        LyricsDocument? resultCorrupted = await sut.LoadLyricsAsync(CreateTrack(corruptedPath));
        LyricsDocument? resultNotExist = await sut.LoadLyricsAsync(CreateTrack(notExistPath));

        // Assert
        Assert.Null(resultNoLyrics);
        Assert.Null(resultCorrupted);
        Assert.Null(resultNotExist);
    }

    /// <summary>
    /// キャンセル済みの CancellationToken を渡した場合、OperationCanceledException がスローされることを検証します。
    /// </summary>
    [Fact]
    public async Task LoadLyricsAsync_キャンセル済みトークン指定_OperationCanceledExceptionをスローすること()
    {
        // Arrange
        string wavPath = CreateDummyWavFile("cancel_test.wav");
        var sut = new LocalLyricsProvider(new LrcParser());
        Track track = CreateTrack(wavPath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.LoadLyricsAsync(track, cts.Token));
    }

    /// <summary>
    /// AddInfrastructureServices および AddApplicationServices の DI コンテナから歌詞関連サービスが Singleton として解決できることを検証します。
    /// </summary>
    [Fact]
    public void AddInfrastructureServicesおよびAddApplicationServices_歌詞関連サービスDI解決_Singletonとして解決できること()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddInfrastructureServices(_tempDirectory);
        services.AddApplicationServices();
        using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        ILrcParser parser1 = provider.GetRequiredService<ILrcParser>();
        ILrcParser parser2 = provider.GetRequiredService<ILrcParser>();
        ILyricsProvider lyricsProvider = provider.GetRequiredService<ILyricsProvider>();
        LyricsApplicationService appService = provider.GetRequiredService<LyricsApplicationService>();

        // Assert
        Assert.IsType<LrcParser>(parser1);
        Assert.Same(parser1, parser2);
        Assert.IsType<LocalLyricsProvider>(lyricsProvider);
        Assert.NotNull(appService);
    }
}

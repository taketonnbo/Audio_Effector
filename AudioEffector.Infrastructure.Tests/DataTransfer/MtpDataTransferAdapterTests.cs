using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.DataTransfer;
using Xunit;

namespace AudioEffector.Infrastructure.Tests.DataTransfer;

/// <summary>
/// MtpDataTransferAdapterのファイル転送・削除・進捗通知・デバイススキャン処理を検証するテストクラス
/// </summary>
public sealed class MtpDataTransferAdapterTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _sourceDirectory;
    private readonly string _destinationDirectory;

    public MtpDataTransferAdapterTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_Mtp", Guid.NewGuid().ToString());
        _sourceDirectory = Path.Combine(_tempDirectory, "Source");
        _destinationDirectory = Path.Combine(_tempDirectory, "Dest");
        Directory.CreateDirectory(_sourceDirectory);
        Directory.CreateDirectory(_destinationDirectory);
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
            // クリーンアップエラーは無視
        }
    }

    private string CreateSourceFile(string fileName, int sizeBytes = 128 * 1024)
    {
        string filePath = Path.Combine(_sourceDirectory, fileName);
        var buffer = new byte[sizeBytes];
        new Random(42).NextBytes(buffer);
        File.WriteAllBytes(filePath, buffer);
        return filePath;
    }

    /// <summary>
    /// 転送元ファイルが存在しない場合、TransferTrackAsyncがfalseを返すかを検証します。
    /// </summary>
    [Fact]
    public async Task TransferTrackAsync_転送元ファイルが存在しない場合_falseを返すこと()
    {
        // Arrange
        var sut = new MtpDataTransferAdapter();
        var nonExistentPath = AudioPath.Create(Path.Combine(_sourceDirectory, "ghost.mp3"));

        // Act
        bool result = await sut.TransferTrackAsync(nonExistentPath, _destinationDirectory);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// 転送先ディレクトリが存在しない場合、TransferTrackAsyncがfalseを返すかを検証します。
    /// </summary>
    [Fact]
    public async Task TransferTrackAsync_転送先ディレクトリが存在しない場合_falseを返すこと()
    {
        // Arrange
        var sut = new MtpDataTransferAdapter();
        string sourceFile = CreateSourceFile("track.mp3", 1024);
        string nonExistentDest = Path.Combine(_tempDirectory, "InvalidFolder");

        // Act
        bool result = await sut.TransferTrackAsync(AudioPath.Create(sourceFile), nonExistentDest);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// 正常な転送元・転送先を指定した際、ファイルが転送先にコピーされtrueを返すかを検証します。
    /// </summary>
    [Fact]
    public async Task TransferTrackAsync_正常系_ファイルが転送先にコピーされtrueを返すこと()
    {
        // Arrange
        var sut = new MtpDataTransferAdapter();
        string sourceFile = CreateSourceFile("favorite_song.mp3", 256 * 1024);
        string expectedDestFile = Path.Combine(_destinationDirectory, "favorite_song.mp3");

        // Act
        bool result = await sut.TransferTrackAsync(AudioPath.Create(sourceFile), _destinationDirectory);

        // Assert
        Assert.True(result);
        Assert.True(File.Exists(expectedDestFile));
        Assert.Equal(new FileInfo(sourceFile).Length, new FileInfo(expectedDestFile).Length);
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SyncProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }

    /// <summary>
    /// 転送処理中、IProgressに進捗率が通知され、完了時に1.0が報告されるかを検証します。
    /// </summary>
    [Fact]
    public async Task TransferTrackAsync_進捗通知_IProgressに進捗率が通知され最後に1が報告されること()
    {
        // Arrange
        var sut = new MtpDataTransferAdapter();
        string sourceFile = CreateSourceFile("progress_test.mp3", 256 * 1024);
        var reportedProgress = new List<double>();
        var progress = new SyncProgress<double>(val => reportedProgress.Add(val));

        // Act
        bool result = await sut.TransferTrackAsync(AudioPath.Create(sourceFile), _destinationDirectory, progress);

        // Assert
        Assert.True(result);
        Assert.NotEmpty(reportedProgress);
        Assert.Equal(1.0, reportedProgress[^1]);
    }

    /// <summary>
    /// 転送中にキャンセルが発生した場合、転送が中断され転送先の不完全ファイルが削除されるかを検証します。
    /// </summary>
    [Fact]
    public async Task TransferTrackAsync_キャンセル発生時_転送が中断され不完全ファイルが削除されること()
    {
        // Arrange
        var sut = new MtpDataTransferAdapter();
        // 5MBのファイルを作成
        string sourceFile = CreateSourceFile("large_track.mp3", 5 * 1024 * 1024);
        string destFile = Path.Combine(_destinationDirectory, "large_track.mp3");

        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<double>(_ =>
        {
            // 転送開始後にキャンセル
            cts.Cancel();
        });

        // Act
        bool result = false;
        try
        {
            result = await sut.TransferTrackAsync(AudioPath.Create(sourceFile), _destinationDirectory, progress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            result = false;
        }

        // Assert
        Assert.False(result);
        Assert.False(File.Exists(destFile));
    }

    /// <summary>
    /// 指定されたデバイスファイルが存在する場合、ファイルを削除してtrueを返すかを検証します。
    /// </summary>
    [Fact]
    public async Task DeleteDeviceTrackAsync_指定ファイルが存在する場合_ファイルを削除しtrueを返すこと()
    {
        // Arrange
        var sut = new MtpDataTransferAdapter();
        string targetFile = Path.Combine(_destinationDirectory, "to_delete.mp3");
        File.WriteAllText(targetFile, "dummy");

        // Act
        bool result = await sut.DeleteDeviceTrackAsync(targetFile);

        // Assert
        Assert.True(result);
        Assert.False(File.Exists(targetFile));
    }

    /// <summary>
    /// 指定されたデバイスファイルが存在しない場合、DeleteDeviceTrackAsyncがfalseを返すかを検証します。
    /// </summary>
    [Fact]
    public async Task DeleteDeviceTrackAsync_指定ファイルが存在しない場合_falseを返すこと()
    {
        // Arrange
        var sut = new MtpDataTransferAdapter();
        string nonExistentFile = Path.Combine(_destinationDirectory, "missing.mp3");

        // Act
        bool result = await sut.DeleteDeviceTrackAsync(nonExistentFile);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// リムーバブルドライブの接続判定処理が例外をスローせず安全にブール値を返すかを検証します。
    /// </summary>
    [Fact]
    public async Task IsDeviceConnectedAsync_リムーバブルドライブ判定_例外が発生せずブール値を返すこと()
    {
        // Arrange
        var sut = new MtpDataTransferAdapter();

        // Act
        var exception = await Record.ExceptionAsync(() => sut.IsDeviceConnectedAsync());

        // Assert
        Assert.Null(exception);
    }

    /// <summary>
    /// デバイス走査メソッド（GetDeviceTracksAsync / GetDeviceAlbumsAsync）が例外なく実行されリストを返すかを検証します。
    /// </summary>
    [Fact]
    public async Task GetDeviceTracksAsyncおよびGetDeviceAlbumsAsync_デバイス走査_例外なくリストを返すこと()
    {
        // Arrange
        var sut = new MtpDataTransferAdapter();

        // Act
        var tracks = await sut.GetDeviceTracksAsync();
        var albums = await sut.GetDeviceAlbumsAsync();

        // Assert
        Assert.NotNull(tracks);
        Assert.NotNull(albums);
    }

    /// <summary>
    /// TransferCompanionFileAsync で補助ファイル（.lrc）が指定ファイル名で転送され、ファイル不在やパストラバーサル指定時は false を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task TransferCompanionFileAsync_正常系と不正ファイル名および不在ファイル_安全に転送または拒否されること()
    {
        // Arrange
        var sut = new MtpDataTransferAdapter();
        string sourceLrc = Path.Combine(_sourceDirectory, "01_Track.lrc");
        File.WriteAllText(sourceLrc, "[00:01.00]テスト歌詞");

        // Act
        bool copiedRenamed = await sut.TransferCompanionFileAsync(sourceLrc, _destinationDirectory, "01_Converted.lrc");
        bool copiedDefault = await sut.TransferCompanionFileAsync(sourceLrc, _destinationDirectory);
        bool rejectedTraversal = await sut.TransferCompanionFileAsync(sourceLrc, _destinationDirectory, @"..\escape.lrc");
        bool rejectedDot = await sut.TransferCompanionFileAsync(sourceLrc, _destinationDirectory, ".");
        bool rejectedDotDot = await sut.TransferCompanionFileAsync(sourceLrc, _destinationDirectory, "..");
        bool missingSource = await sut.TransferCompanionFileAsync(Path.Combine(_sourceDirectory, "missing.lrc"), _destinationDirectory);

        // Assert
        Assert.True(copiedRenamed);
        Assert.True(File.Exists(Path.Combine(_destinationDirectory, "01_Converted.lrc")));
        Assert.Equal("[00:01.00]テスト歌詞", File.ReadAllText(Path.Combine(_destinationDirectory, "01_Converted.lrc")));

        Assert.True(copiedDefault);
        Assert.True(File.Exists(Path.Combine(_destinationDirectory, "01_Track.lrc")));

        Assert.False(rejectedTraversal);
        Assert.False(rejectedDot);
        Assert.False(rejectedDotDot);
        Assert.False(File.Exists(Path.Combine(_tempDirectory, "escape.lrc")));
        Assert.False(missingSource);
    }

    /// <summary>
    /// DownloadTrackFromDeviceAsync の正常系でサブディレクトリが自動作成されてファイルが逆転送され、進捗が 1.0 まで通知されることを検証します
    /// </summary>
    [Fact]
    public async Task DownloadTrackFromDeviceAsync_正常系_ファイルが逆転送され進捗が1まで通知されること()
    {
        // Arrange
        var sut = new MtpDataTransferAdapter();
        string sourceFile = CreateSourceFile("device_song.flac", 192 * 1024);
        string destFile = Path.Combine(_destinationDirectory, "Artist", "Album", "device_song.flac");
        var reports = new List<double>();
        var progress = new SyncProgress<double>(v => reports.Add(v));

        // Act
        bool result = await sut.DownloadTrackFromDeviceAsync(sourceFile, destFile, progress);

        // Assert
        Assert.True(result);
        Assert.True(File.Exists(destFile));
        Assert.Equal(new FileInfo(sourceFile).Length, new FileInfo(destFile).Length);
        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports[^1]);
    }

    /// <summary>
    /// DownloadTrackFromDeviceAsync の途中でキャンセルされた場合、false を返し不完全な出力ファイルが残らないことを検証します
    /// </summary>
    [Fact]
    public async Task DownloadTrackFromDeviceAsync_途中キャンセル時_falseを返し不完全ファイルが削除されること()
    {
        // Arrange
        var sut = new MtpDataTransferAdapter();
        string sourceFile = CreateSourceFile("large_song.wav", 256 * 1024);
        string destFile = Path.Combine(_destinationDirectory, "Canceled", "large_song.wav");
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<double>(v =>
        {
            if (v > 0 && v < 1.0)
            {
                cts.Cancel();
            }
        });

        // Act
        bool result = await sut.DownloadTrackFromDeviceAsync(sourceFile, destFile, progress, cts.Token);

        // Assert
        Assert.False(result);
        Assert.False(File.Exists(destFile));
    }

    /// <summary>
    /// DownloadTrackFromDeviceAsync で転送元ファイルが存在しない場合、false を返し出力ファイルを作成しないことを検証します
    /// </summary>
    [Fact]
    public async Task DownloadTrackFromDeviceAsync_転送元不在時_falseを返すこと()
    {
        // Arrange
        var sut = new MtpDataTransferAdapter();
        string missingSource = Path.Combine(_sourceDirectory, "not_found.mp3");
        string destFile = Path.Combine(_destinationDirectory, "not_found.mp3");

        // Act
        bool result = await sut.DownloadTrackFromDeviceAsync(missingSource, destFile);

        // Assert
        Assert.False(result);
        Assert.False(File.Exists(destFile));
    }

    /// <summary>
    /// DownloadTrackFromDeviceAsync で保存先に既存ファイルが存在する場合、false を返し既存ファイルが上書きも削除もされないことを検証します
    /// </summary>
    [Fact]
    public async Task DownloadTrackFromDeviceAsync_保存先に既存ファイル存在時_falseを返し既存ファイルが保護されること()
    {
        // Arrange
        var sut = new MtpDataTransferAdapter();
        string sourceFile = CreateSourceFile("existing_song.mp3", 1024);
        string destDir = Path.Combine(_destinationDirectory, "Protected");
        Directory.CreateDirectory(destDir);
        string destFile = Path.Combine(destDir, "existing_song.mp3");
        File.WriteAllText(destFile, "original-pc-data");

        // Act
        bool result = await sut.DownloadTrackFromDeviceAsync(sourceFile, destFile);

        // Assert
        Assert.False(result);
        Assert.True(File.Exists(destFile));
        Assert.Equal("original-pc-data", File.ReadAllText(destFile));
    }
}

using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.McpServer.Dtos;
using AudioEffector.McpServer.Gateways;
using AudioEffector.McpServer.Protocol;
using AudioEffector.McpServer.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Moq;

namespace AudioEffector.McpServer.Tests.Tools;

public sealed class PlaybackToolsTests
{
    /// <summary>
    /// trackId を指定して play_track を呼び出した場合、ゲートウェイへ TrackId が渡され統一 JSON 応答が返ることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_有効なtrackId指定時_再生開始結果JSONを返す()
    {
        // Arrange
        var trackGuid = Guid.NewGuid();
        var expectedResponse = new PlayTrackResponse(
            PlaybackState: "playing",
            Track: new PlayedTrackDto(
                Id: trackGuid,
                Title: "Blue Note Morning",
                Artist: "Miles Trio",
                Album: "Midnight Sessions",
                DurationSeconds: 225.5,
                DurationString: "03:45",
                FilePath: @"C:\Music\Jazz\BlueNote.flac"));

        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.PlayTrackAsync(
                It.Is<TrackId?>(id => id.HasValue && id.Value.Value == trackGuid),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlayTrackResponse>.Success(expectedResponse));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayTrackAsync(trackId: trackGuid.ToString("D"));

        // Assert
        Assert.False(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal("playing", data.GetProperty("playbackState").GetString());
        JsonElement track = data.GetProperty("track");
        Assert.Equal(trackGuid.ToString("D"), track.GetProperty("id").GetString());
        Assert.Equal("Blue Note Morning", track.GetProperty("title").GetString());
        Assert.Equal("Miles Trio", track.GetProperty("artist").GetString());
        Assert.Equal("Midnight Sessions", track.GetProperty("album").GetString());
        Assert.Equal(225.5, track.GetProperty("durationSeconds").GetDouble());
        Assert.Equal("03:45", track.GetProperty("durationString").GetString());
        Assert.Equal(@"C:\Music\Jazz\BlueNote.flac", track.GetProperty("filePath").GetString());
    }

    /// <summary>
    /// filePath を指定して play_track を呼び出した場合、前後空白がトリムされてゲートウェイへ渡され正常応答が返ることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_filePath指定時_トリム済みパスで再生開始結果JSONを返す()
    {
        // Arrange
        const string filePath = @"C:\Music\Rock\Track01.mp3";
        var expectedResponse = new PlayTrackResponse(
            PlaybackState: "playing",
            Track: new PlayedTrackDto(
                Id: Guid.NewGuid(),
                Title: "Track01",
                Artist: "Rock Band",
                Album: "First Album",
                DurationSeconds: 180.0,
                DurationString: "03:00",
                FilePath: filePath));

        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.PlayTrackAsync(null, filePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlayTrackResponse>.Success(expectedResponse));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayTrackAsync(filePath: $"  {filePath}  ");

        // Assert
        Assert.False(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(filePath, doc.RootElement.GetProperty("data").GetProperty("track").GetProperty("filePath").GetString());
        gateway.Verify(g => g.PlayTrackAsync(null, filePath, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// trackId と filePath の両方が未指定または空白の場合に INVALID_PARAMETER エラーを返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "   ")]
    public async Task PlayTrackAsync_trackIdとfilePath両方未指定時_INVALID_PARAMETERを返す(string? trackId, string? filePath)
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayTrackAsync(trackId: trackId, filePath: filePath);

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(McpErrorCodes.INVALID_PARAMETER, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        gateway.Verify(g => g.PlayTrackAsync(It.IsAny<TrackId?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// trackId に不正な GUID 形式または空 GUID を指定した場合に INVALID_PARAMETER エラーを返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData("invalid-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task PlayTrackAsync_不正なtrackId指定時_INVALID_PARAMETERを返す(string invalidTrackId)
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayTrackAsync(trackId: invalidTrackId);

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(McpErrorCodes.INVALID_PARAMETER, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        gateway.Verify(g => g.PlayTrackAsync(It.IsAny<TrackId?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// filePath に相対パスを指定した場合に送信前で INVALID_PARAMETER エラーを返しゲートウェイを呼び出さないことを検証します。
    /// </summary>
    [Theory]
    [InlineData("song.wav")]
    [InlineData(@".\music\song.flac")]
    [InlineData(@"..\song.mp3")]
    public async Task PlayTrackAsync_filePathに相対パス指定時_INVALID_PARAMETERを返す(string relativeFilePath)
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayTrackAsync(filePath: relativeFilePath);

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(McpErrorCodes.INVALID_PARAMETER, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        gateway.Verify(g => g.PlayTrackAsync(It.IsAny<TrackId?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// WPF 本体が未起動の場合に WPF_APP_NOT_RUNNING エラーおよび launch_app 案内メッセージが返ることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_WPF未起動時_WPF_APP_NOT_RUNNINGとlaunch_app案内を返す()
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.PlayTrackAsync(It.IsAny<TrackId?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlayTrackResponse>.Failure(
                McpErrorCodes.WPF_APP_NOT_RUNNING,
                McpErrorMapper.GetPublicMessage(McpErrorCodes.WPF_APP_NOT_RUNNING)));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayTrackAsync(trackId: Guid.NewGuid().ToString("D"));

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        JsonElement error = doc.RootElement.GetProperty("error");
        Assert.Equal(McpErrorCodes.WPF_APP_NOT_RUNNING, error.GetProperty("code").GetString());
        Assert.Contains("launch_app", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// ゲートウェイが NOT_FOUND / FILE_NOT_FOUND / IPC_CONNECTION_FAILED を返した場合に対応するエラーコードが返ることを検証します。
    /// </summary>
    [Theory]
    [InlineData(McpErrorCodes.NOT_FOUND)]
    [InlineData(McpErrorCodes.FILE_NOT_FOUND)]
    [InlineData(McpErrorCodes.IPC_CONNECTION_FAILED)]
    public async Task PlayTrackAsync_ゲートウェイ失敗結果時_対応するエラーコードを返す(string errorCode)
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.PlayTrackAsync(It.IsAny<TrackId?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlayTrackResponse>.Failure(
                errorCode,
                McpErrorMapper.GetPublicMessage(errorCode)));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayTrackAsync(trackId: Guid.NewGuid().ToString("D"));

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.Equal(errorCode, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// ゲートウェイ実行中にキャンセル例外または予期しない例外が発生した場合に統一エラー応答へ変換され内部詳細が露出しないことを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_予期しない例外発生時_INTERNAL_ERRORを返し機密詳細を露出しない()
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.PlayTrackAsync(It.IsAny<TrackId?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(@"secret internal path C:\Users\secret\file.flac"));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayTrackAsync(trackId: Guid.NewGuid().ToString("D"));

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        JsonElement error = doc.RootElement.GetProperty("error");
        Assert.Equal(McpErrorCodes.INTERNAL_ERROR, error.GetProperty("code").GetString());
        Assert.DoesNotContain("secret", error.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// play_track ツールの Description 属性に launch_app による事前起動案内が明記されていることを検証します。
    /// </summary>
    [Fact]
    public void PlayTrackAsync_ツール説明属性_launch_app案内が含まれる()
    {
        // Arrange
        MethodInfo? method = typeof(PlaybackTools).GetMethod(nameof(PlaybackTools.PlayTrackAsync));
        Assert.NotNull(method);

        // Act
        DescriptionAttribute? description = method.GetCustomAttribute<DescriptionAttribute>();

        // Assert
        Assert.NotNull(description);
        Assert.Contains("launch_app", description.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// play_album で albumId・shuffle・repeat・startTrackIndex を指定した場合、ゲートウェイへ渡され統一 JSON 応答が返ることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayAlbumAsync_有効なパラメータ指定時_コレクション再生結果JSONを返す()
    {
        // Arrange
        var albumGuid = Guid.NewGuid();
        var trackGuid = Guid.NewGuid();
        var expected = new PlayCollectionResponse(
            SourceType: "album",
            SourceId: albumGuid,
            SourceName: "Midnight Sessions",
            PlaybackState: "playing",
            TotalTracks: 8,
            IsShuffleEnabled: true,
            IsRepeatEnabled: true,
            Track: new PlayedTrackDto(
                Id: trackGuid,
                Title: "Blue Note Morning",
                Artist: "Miles Trio",
                Album: "Midnight Sessions",
                DurationSeconds: 225.5,
                DurationString: "03:45",
                FilePath: @"C:\Music\Jazz\BlueNote.flac"));

        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.PlayAlbumAsync(
                It.Is<AlbumId?>(id => id.HasValue && id.Value.Value == albumGuid),
                null,
                null,
                null,
                2,
                true,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlayCollectionResponse>.Success(expected));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayAlbumAsync(
            albumId: albumGuid.ToString("D"),
            startTrackIndex: 2,
            shuffle: true,
            repeat: true);

        // Assert
        Assert.False(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal("album", data.GetProperty("sourceType").GetString());
        Assert.Equal(albumGuid.ToString("D"), data.GetProperty("sourceId").GetString());
        Assert.Equal("Midnight Sessions", data.GetProperty("sourceName").GetString());
        Assert.Equal("playing", data.GetProperty("playbackState").GetString());
        Assert.Equal(8, data.GetProperty("totalTracks").GetInt32());
        Assert.True(data.GetProperty("isShuffleEnabled").GetBoolean());
        Assert.True(data.GetProperty("isRepeatEnabled").GetBoolean());
        Assert.Equal(trackGuid.ToString("D"), data.GetProperty("track").GetProperty("id").GetString());
    }

    /// <summary>
    /// play_album で必須項目未指定・不正 GUID・負数インデックスを指定した場合に INVALID_PARAMETER を返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData("invalid-guid", null, null, null)]
    [InlineData("00000000-0000-0000-0000-000000000000", null, null, null)]
    [InlineData(null, "Album", "bad-track-guid", null)]
    [InlineData(null, "Album", null, -1)]
    public async Task PlayAlbumAsync_不正パラメータ指定時_INVALID_PARAMETERを返す(
        string? albumId,
        string? albumName,
        string? startTrackId,
        int? startTrackIndex)
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayAlbumAsync(
            albumId: albumId,
            albumName: albumName,
            startTrackId: startTrackId,
            startTrackIndex: startTrackIndex);

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(McpErrorCodes.INVALID_PARAMETER, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        gateway.Verify(
            g => g.PlayAlbumAsync(
                It.IsAny<AlbumId?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<TrackId?>(),
                It.IsAny<int?>(),
                It.IsAny<bool?>(),
                It.IsAny<bool?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// play_playlist で playlistName・startTrackId・shuffle を指定した場合、ゲートウェイへ渡され統一 JSON 応答が返ることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayPlaylistAsync_有効なパラメータ指定時_コレクション再生結果JSONを返す()
    {
        // Arrange
        var playlistGuid = Guid.NewGuid();
        var startTrackGuid = Guid.NewGuid();
        var expected = new PlayCollectionResponse(
            SourceType: "playlist",
            SourceId: playlistGuid,
            SourceName: "Drive Mix",
            PlaybackState: "playing",
            TotalTracks: 5,
            IsShuffleEnabled: true,
            IsRepeatEnabled: false,
            Track: new PlayedTrackDto(
                Id: startTrackGuid,
                Title: "Highway Star",
                Artist: "Synth Wave",
                Album: "Neon",
                DurationSeconds: 210.0,
                DurationString: "03:30",
                FilePath: @"C:\Music\Drive\01.flac"));

        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.PlayPlaylistAsync(
                null,
                "Drive Mix",
                It.Is<TrackId?>(id => id.HasValue && id.Value.Value == startTrackGuid),
                null,
                true,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlayCollectionResponse>.Success(expected));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayPlaylistAsync(
            playlistName: "  Drive Mix  ",
            startTrackId: startTrackGuid.ToString("D"),
            shuffle: true,
            repeat: false);

        // Assert
        Assert.False(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal("playlist", data.GetProperty("sourceType").GetString());
        Assert.Equal(playlistGuid.ToString("D"), data.GetProperty("sourceId").GetString());
        Assert.Equal("Drive Mix", data.GetProperty("sourceName").GetString());
        Assert.Equal(5, data.GetProperty("totalTracks").GetInt32());
        Assert.True(data.GetProperty("isShuffleEnabled").GetBoolean());
        Assert.False(data.GetProperty("isRepeatEnabled").GetBoolean());
    }

    /// <summary>
    /// play_playlist で必須項目未指定・不正 GUID・負数インデックスを指定した場合に INVALID_PARAMETER を返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData("invalid-guid", null, null, null)]
    [InlineData("00000000-0000-0000-0000-000000000000", null, null, null)]
    [InlineData(null, "Drive Mix", "bad-track-guid", null)]
    [InlineData(null, "Drive Mix", null, -1)]
    public async Task PlayPlaylistAsync_不正パラメータ指定時_INVALID_PARAMETERを返す(
        string? playlistId,
        string? playlistName,
        string? startTrackId,
        int? startTrackIndex)
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.PlayPlaylistAsync(
            playlistId: playlistId,
            playlistName: playlistName,
            startTrackId: startTrackId,
            startTrackIndex: startTrackIndex);

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(McpErrorCodes.INVALID_PARAMETER, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        gateway.Verify(
            g => g.PlayPlaylistAsync(
                It.IsAny<PlaylistId?>(),
                It.IsAny<string?>(),
                It.IsAny<TrackId?>(),
                It.IsAny<int?>(),
                It.IsAny<bool?>(),
                It.IsAny<bool?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// play_album / play_playlist でゲートウェイが WPF_APP_NOT_RUNNING / IPC_CONNECTION_FAILED を返した場合に対応するエラーコードを返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData(McpErrorCodes.WPF_APP_NOT_RUNNING)]
    [InlineData(McpErrorCodes.IPC_CONNECTION_FAILED)]
    public async Task PlayAlbumとPlayPlaylist_ゲートウェイ失敗時_対応するエラーコードを返す(string errorCode)
    {
        // Arrange
        var failure = PlaybackControlResult<PlayCollectionResponse>.Failure(
            errorCode,
            McpErrorMapper.GetPublicMessage(errorCode));

        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.PlayAlbumAsync(
                It.IsAny<AlbumId?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<TrackId?>(),
                It.IsAny<int?>(),
                It.IsAny<bool?>(),
                It.IsAny<bool?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failure);
        gateway
            .Setup(g => g.PlayPlaylistAsync(
                It.IsAny<PlaylistId?>(),
                It.IsAny<string?>(),
                It.IsAny<TrackId?>(),
                It.IsAny<int?>(),
                It.IsAny<bool?>(),
                It.IsAny<bool?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failure);

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult albumResult = await sut.PlayAlbumAsync(albumName: "Album");
        CallToolResult playlistResult = await sut.PlayPlaylistAsync(playlistName: "Playlist");

        // Assert
        Assert.True(albumResult.IsError);
        Assert.True(playlistResult.IsError);
        using JsonDocument albumDoc = ParsePayload(albumResult);
        using JsonDocument playlistDoc = ParsePayload(playlistResult);
        Assert.Equal(errorCode, albumDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(errorCode, playlistDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// play_album および play_playlist ツールの Description 属性に launch_app 案内が含まれることを検証します。
    /// </summary>
    [Theory]
    [InlineData(nameof(PlaybackTools.PlayAlbumAsync))]
    [InlineData(nameof(PlaybackTools.PlayPlaylistAsync))]
    public void PlayAlbumとPlayPlaylist_ツール説明属性_launch_app案内が含まれる(string methodName)
    {
        // Arrange
        MethodInfo? method = typeof(PlaybackTools).GetMethod(methodName);
        Assert.NotNull(method);

        // Act
        DescriptionAttribute? description = method.GetCustomAttribute<DescriptionAttribute>();

        // Assert
        Assert.NotNull(description);
        Assert.Contains("launch_app", description.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// get_playback_state を呼び出し再生中・一時停止中応答を受け取った場合、全フィールドを含む統一 JSON が返ることを検証します。
    /// </summary>
    [Theory]
    [InlineData("playing")]
    [InlineData("paused")]
    public async Task GetPlaybackStateAsync_再生中または一時停止中時_再生状態JSONを返す(string state)
    {
        // Arrange
        var trackGuid = Guid.NewGuid();
        var expected = new PlaybackStateResponse(
            PlaybackState: state,
            Track: new PlayedTrackDto(
                Id: trackGuid,
                Title: "Blue Note Morning",
                Artist: "Miles Trio",
                Album: "Midnight Sessions",
                DurationSeconds: 225.5,
                DurationString: "03:45",
                FilePath: @"C:\Music\Jazz\BlueNote.flac"),
            PositionSeconds: 83.25,
            PositionString: "01:23",
            DurationSeconds: 225.5,
            DurationString: "03:45",
            Volume: 0.75,
            IsShuffleEnabled: true,
            IsRepeatEnabled: false,
            UserQueueCount: 2,
            AlbumQueueCount: 5);

        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.GetPlaybackStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlaybackStateResponse>.Success(expected));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.GetPlaybackStateAsync();

        // Assert
        Assert.False(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal(state, data.GetProperty("playbackState").GetString());
        Assert.Equal(trackGuid.ToString("D"), data.GetProperty("track").GetProperty("id").GetString());
        Assert.Equal(83.25, data.GetProperty("positionSeconds").GetDouble());
        Assert.Equal("01:23", data.GetProperty("positionString").GetString());
        Assert.Equal(225.5, data.GetProperty("durationSeconds").GetDouble());
        Assert.Equal("03:45", data.GetProperty("durationString").GetString());
        Assert.Equal(0.75, data.GetProperty("volume").GetDouble());
        Assert.True(data.GetProperty("isShuffleEnabled").GetBoolean());
        Assert.False(data.GetProperty("isRepeatEnabled").GetBoolean());
        Assert.Equal(2, data.GetProperty("userQueueCount").GetInt32());
        Assert.Equal(5, data.GetProperty("albumQueueCount").GetInt32());
    }

    /// <summary>
    /// get_playback_state で停止中・トラック未選択の場合、playbackState が stopped、track が JSON の null として正常返却されることを検証します。
    /// </summary>
    [Fact]
    public async Task GetPlaybackStateAsync_停止中トラック未選択時_stoppedとtrackNullのJSONを返す()
    {
        // Arrange
        var expected = new PlaybackStateResponse(
            PlaybackState: "stopped",
            Track: null,
            PositionSeconds: 0.0,
            PositionString: "00:00",
            DurationSeconds: 0.0,
            DurationString: "00:00",
            Volume: 0.8,
            IsShuffleEnabled: false,
            IsRepeatEnabled: false,
            UserQueueCount: 0,
            AlbumQueueCount: 0);

        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.GetPlaybackStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlaybackStateResponse>.Success(expected));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.GetPlaybackStateAsync();

        // Assert
        Assert.False(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal("stopped", data.GetProperty("playbackState").GetString());
        Assert.True(data.TryGetProperty("track", out JsonElement trackElement));
        Assert.Equal(JsonValueKind.Null, trackElement.ValueKind);
        Assert.Equal(0.0, data.GetProperty("positionSeconds").GetDouble());
        Assert.Equal("00:00", data.GetProperty("positionString").GetString());
        Assert.Equal(0.8, data.GetProperty("volume").GetDouble());
    }

    /// <summary>
    /// get_playback_state でゲートウェイが WPF_APP_NOT_RUNNING または IPC_CONNECTION_FAILED を返した場合に対応するエラー応答となることを検証します。
    /// </summary>
    [Theory]
    [InlineData(McpErrorCodes.WPF_APP_NOT_RUNNING)]
    [InlineData(McpErrorCodes.IPC_CONNECTION_FAILED)]
    public async Task GetPlaybackStateAsync_ゲートウェイ失敗時_対応するエラーコードを返す(string errorCode)
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.GetPlaybackStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlaybackStateResponse>.Failure(
                errorCode,
                McpErrorMapper.GetPublicMessage(errorCode)));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.GetPlaybackStateAsync();

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        JsonElement error = doc.RootElement.GetProperty("error");
        Assert.Equal(errorCode, error.GetProperty("code").GetString());
        if (errorCode == McpErrorCodes.WPF_APP_NOT_RUNNING)
        {
            Assert.Contains("launch_app", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// get_playback_state 実行中に予期しない例外が発生した場合、INTERNAL_ERROR に変換され機密詳細が露出しないことを検証します。
    /// </summary>
    [Fact]
    public async Task GetPlaybackStateAsync_予期しない例外発生時_INTERNAL_ERRORを返し機密詳細を露出しない()
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.GetPlaybackStateAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(@"secret diagnostic C:\private\token"));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.GetPlaybackStateAsync();

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        JsonElement error = doc.RootElement.GetProperty("error");
        Assert.Equal(McpErrorCodes.INTERNAL_ERROR, error.GetProperty("code").GetString());
        Assert.DoesNotContain("secret", error.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// get_playback_state ツールの Description 属性に launch_app による事前起動案内が含まれることを検証します。
    /// </summary>
    [Fact]
    public void GetPlaybackStateAsync_ツール説明属性_launch_app案内が含まれる()
    {
        // Arrange
        MethodInfo? method = typeof(PlaybackTools).GetMethod(nameof(PlaybackTools.GetPlaybackStateAsync));
        Assert.NotNull(method);

        // Act
        DescriptionAttribute? description = method.GetCustomAttribute<DescriptionAttribute>();

        // Assert
        Assert.NotNull(description);
        Assert.Contains("launch_app", description.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// 基本再生制御の 5 ツールが正常終了した際、統一 JSON 応答（action / playbackState / track）を返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData("pause_playback", "paused", true)]
    [InlineData("resume_playback", "playing", true)]
    [InlineData("stop_playback", "stopped", false)]
    [InlineData("next_track", "playing", true)]
    [InlineData("next_track", "stopped", false)]
    [InlineData("previous_track", "playing", true)]
    public async Task TransportTools_正常終了時_actionとplaybackStateとtrackを含む統一JSONを返す(
        string toolName,
        string playbackState,
        bool hasTrack)
    {
        // Arrange
        var trackDto = hasTrack
            ? new PlayedTrackDto(
                Id: Guid.NewGuid(),
                Title: "Blue Note Morning",
                Artist: "Miles Trio",
                Album: "Midnight Sessions",
                DurationSeconds: 225.5,
                DurationString: "03:45",
                FilePath: @"C:\Music\Jazz\BlueNote.flac")
            : null;
        var expected = new PlaybackActionResponse(toolName, playbackState, trackDto);

        var gateway = new Mock<IPlaybackControlGateway>();
        gateway.Setup(g => g.PausePlaybackAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlaybackActionResponse>.Success(expected));
        gateway.Setup(g => g.ResumePlaybackAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlaybackActionResponse>.Success(expected));
        gateway.Setup(g => g.StopPlaybackAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlaybackActionResponse>.Success(expected));
        gateway.Setup(g => g.NextTrackAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlaybackActionResponse>.Success(expected));
        gateway.Setup(g => g.PreviousTrackAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlaybackActionResponse>.Success(expected));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = toolName switch
        {
            "pause_playback" => await sut.PausePlaybackAsync(),
            "resume_playback" => await sut.ResumePlaybackAsync(),
            "stop_playback" => await sut.StopPlaybackAsync(),
            "next_track" => await sut.NextTrackAsync(),
            "previous_track" => await sut.PreviousTrackAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(toolName)),
        };

        // Assert
        Assert.False(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal(toolName, data.GetProperty("action").GetString());
        Assert.Equal(playbackState, data.GetProperty("playbackState").GetString());
        Assert.True(data.TryGetProperty("track", out JsonElement trackElement));
        if (hasTrack)
        {
            Assert.Equal(trackDto!.Id.ToString("D"), trackElement.GetProperty("id").GetString());
            Assert.Equal("Blue Note Morning", trackElement.GetProperty("title").GetString());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, trackElement.ValueKind);
        }
    }

    /// <summary>
    /// 基本再生制御の 5 ツールでゲートウェイがエラーを返した場合、対応するエラーコードと公開メッセージを返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData("pause_playback", McpErrorCodes.WPF_APP_NOT_RUNNING)]
    [InlineData("resume_playback", McpErrorCodes.NOT_FOUND)]
    [InlineData("stop_playback", McpErrorCodes.IPC_CONNECTION_FAILED)]
    [InlineData("next_track", McpErrorCodes.WPF_APP_NOT_RUNNING)]
    [InlineData("previous_track", McpErrorCodes.NOT_FOUND)]
    public async Task TransportTools_ゲートウェイ失敗時_対応するエラーコードを返す(string toolName, string errorCode)
    {
        // Arrange
        var failure = PlaybackControlResult<PlaybackActionResponse>.Failure(
            errorCode,
            McpErrorMapper.GetPublicMessage(errorCode));

        var gateway = new Mock<IPlaybackControlGateway>();
        gateway.Setup(g => g.PausePlaybackAsync(It.IsAny<CancellationToken>())).ReturnsAsync(failure);
        gateway.Setup(g => g.ResumePlaybackAsync(It.IsAny<CancellationToken>())).ReturnsAsync(failure);
        gateway.Setup(g => g.StopPlaybackAsync(It.IsAny<CancellationToken>())).ReturnsAsync(failure);
        gateway.Setup(g => g.NextTrackAsync(It.IsAny<CancellationToken>())).ReturnsAsync(failure);
        gateway.Setup(g => g.PreviousTrackAsync(It.IsAny<CancellationToken>())).ReturnsAsync(failure);

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = toolName switch
        {
            "pause_playback" => await sut.PausePlaybackAsync(),
            "resume_playback" => await sut.ResumePlaybackAsync(),
            "stop_playback" => await sut.StopPlaybackAsync(),
            "next_track" => await sut.NextTrackAsync(),
            "previous_track" => await sut.PreviousTrackAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(toolName)),
        };

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(errorCode, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// 基本再生制御ツール実行中に予期しない例外が発生した場合、INTERNAL_ERROR に変換され機密詳細が露出しないことを検証します。
    /// </summary>
    [Theory]
    [InlineData("pause_playback")]
    [InlineData("resume_playback")]
    [InlineData("stop_playback")]
    [InlineData("next_track")]
    [InlineData("previous_track")]
    public async Task TransportTools_予期しない例外発生時_INTERNAL_ERRORを返し機密詳細を露出しない(string toolName)
    {
        // Arrange
        var ex = new InvalidOperationException(@"secret diagnostic C:\private\token");
        var gateway = new Mock<IPlaybackControlGateway>();
        gateway.Setup(g => g.PausePlaybackAsync(It.IsAny<CancellationToken>())).ThrowsAsync(ex);
        gateway.Setup(g => g.ResumePlaybackAsync(It.IsAny<CancellationToken>())).ThrowsAsync(ex);
        gateway.Setup(g => g.StopPlaybackAsync(It.IsAny<CancellationToken>())).ThrowsAsync(ex);
        gateway.Setup(g => g.NextTrackAsync(It.IsAny<CancellationToken>())).ThrowsAsync(ex);
        gateway.Setup(g => g.PreviousTrackAsync(It.IsAny<CancellationToken>())).ThrowsAsync(ex);

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = toolName switch
        {
            "pause_playback" => await sut.PausePlaybackAsync(),
            "resume_playback" => await sut.ResumePlaybackAsync(),
            "stop_playback" => await sut.StopPlaybackAsync(),
            "next_track" => await sut.NextTrackAsync(),
            "previous_track" => await sut.PreviousTrackAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(toolName)),
        };

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        JsonElement error = doc.RootElement.GetProperty("error");
        Assert.Equal(McpErrorCodes.INTERNAL_ERROR, error.GetProperty("code").GetString());
        Assert.DoesNotContain("secret", error.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 基本再生制御 5 ツールの Description 属性に launch_app による事前起動案内が含まれることを検証します。
    /// </summary>
    [Theory]
    [InlineData(nameof(PlaybackTools.PausePlaybackAsync))]
    [InlineData(nameof(PlaybackTools.ResumePlaybackAsync))]
    [InlineData(nameof(PlaybackTools.StopPlaybackAsync))]
    [InlineData(nameof(PlaybackTools.NextTrackAsync))]
    [InlineData(nameof(PlaybackTools.PreviousTrackAsync))]
    public void TransportTools_ツール説明属性_launch_app案内が含まれる(string methodName)
    {
        // Arrange
        MethodInfo? method = typeof(PlaybackTools).GetMethod(methodName);
        Assert.NotNull(method);

        // Act
        DescriptionAttribute? description = method.GetCustomAttribute<DescriptionAttribute>();

        // Assert
        Assert.NotNull(description);
        Assert.Contains("launch_app", description.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// get_playback_queue を呼び出し再生中トラックおよび予約キュー・アルバム残り曲キューを受け取った場合、統一 JSON が返ることを検証します。
    /// </summary>
    [Fact]
    public async Task GetPlaybackQueueAsync_再生中かつ両キュー存在時_再生キューJSONを返す()
    {
        // Arrange
        var currentGuid = Guid.NewGuid();
        var userGuid = Guid.NewGuid();
        var albumGuid = Guid.NewGuid();
        var expected = new PlaybackQueueResponse(
            CurrentTrack: new PlayedTrackDto(
                Id: currentGuid,
                Title: "Current Track",
                Artist: "Miles Trio",
                Album: "Midnight Sessions",
                DurationSeconds: 225.5,
                DurationString: "03:45",
                FilePath: @"C:\Music\01.flac"),
            Limit: 20,
            UserQueueCount: 1,
            AlbumQueueCount: 2,
            UserQueue:
            [
                new PlayedTrackDto(
                    Id: userGuid,
                    Title: "Queued Request",
                    Artist: "Guest Artist",
                    Album: "Single",
                    DurationSeconds: 180.0,
                    DurationString: "03:00",
                    FilePath: @"C:\Music\u1.flac"),
            ],
            AlbumQueue:
            [
                new PlayedTrackDto(
                    Id: albumGuid,
                    Title: "Next Album Track",
                    Artist: "Miles Trio",
                    Album: "Midnight Sessions",
                    DurationSeconds: 200.0,
                    DurationString: "03:20",
                    FilePath: @"C:\Music\02.flac"),
            ]);

        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.GetPlaybackQueueAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlaybackQueueResponse>.Success(expected));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.GetPlaybackQueueAsync(limit: 20);

        // Assert
        Assert.False(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.Equal(currentGuid.ToString("D"), data.GetProperty("currentTrack").GetProperty("id").GetString());
        Assert.Equal(20, data.GetProperty("limit").GetInt32());
        Assert.Equal(1, data.GetProperty("userQueueCount").GetInt32());
        Assert.Equal(2, data.GetProperty("albumQueueCount").GetInt32());
        JsonElement userItem = Assert.Single(data.GetProperty("userQueue").EnumerateArray());
        Assert.Equal(userGuid.ToString("D"), userItem.GetProperty("id").GetString());
        JsonElement albumItem = Assert.Single(data.GetProperty("albumQueue").EnumerateArray());
        Assert.Equal(albumGuid.ToString("D"), albumItem.GetProperty("id").GetString());
    }

    /// <summary>
    /// get_playback_queue でキューが空かつ停止中の場合、currentTrack が JSON の null、両キューが空配列として正常返却されることを検証します。
    /// </summary>
    [Fact]
    public async Task GetPlaybackQueueAsync_キュー空かつ停止中時_currentTrackNullと空配列のJSONを返す()
    {
        // Arrange
        var expected = new PlaybackQueueResponse(
            CurrentTrack: null,
            Limit: 50,
            UserQueueCount: 0,
            AlbumQueueCount: 0,
            UserQueue: Array.Empty<PlayedTrackDto>(),
            AlbumQueue: Array.Empty<PlayedTrackDto>());

        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.GetPlaybackQueueAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlaybackQueueResponse>.Success(expected));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.GetPlaybackQueueAsync();

        // Assert
        Assert.False(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        JsonElement data = doc.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("currentTrack", out JsonElement currentTrackElement));
        Assert.Equal(JsonValueKind.Null, currentTrackElement.ValueKind);
        Assert.Equal(0, data.GetProperty("userQueueCount").GetInt32());
        Assert.Equal(0, data.GetProperty("albumQueueCount").GetInt32());
        Assert.Empty(data.GetProperty("userQueue").EnumerateArray());
        Assert.Empty(data.GetProperty("albumQueue").EnumerateArray());
    }

    /// <summary>
    /// get_playback_queue で 1〜200 の範囲外の limit を指定した場合、ゲートウェイを呼び出さず INVALID_PARAMETER を返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(201)]
    public async Task GetPlaybackQueueAsync_範囲外limit指定時_INVALID_PARAMETERを返す(int invalidLimit)
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.GetPlaybackQueueAsync(limit: invalidLimit);

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(McpErrorCodes.INVALID_PARAMETER, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        gateway.Verify(g => g.GetPlaybackQueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// get_playback_queue でゲートウェイが WPF_APP_NOT_RUNNING または IPC_CONNECTION_FAILED を返した場合に対応するエラーコードを返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData(McpErrorCodes.WPF_APP_NOT_RUNNING)]
    [InlineData(McpErrorCodes.IPC_CONNECTION_FAILED)]
    public async Task GetPlaybackQueueAsync_ゲートウェイ失敗時_対応するエラーコードを返す(string errorCode)
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.GetPlaybackQueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlaybackControlResult<PlaybackQueueResponse>.Failure(
                errorCode,
                McpErrorMapper.GetPublicMessage(errorCode)));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.GetPlaybackQueueAsync();

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        JsonElement error = doc.RootElement.GetProperty("error");
        Assert.Equal(errorCode, error.GetProperty("code").GetString());
        if (errorCode == McpErrorCodes.WPF_APP_NOT_RUNNING)
        {
            Assert.Contains("launch_app", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// get_playback_queue 実行中に予期しない例外が発生した場合、INTERNAL_ERROR に変換され機密詳細が露出しないことを検証します。
    /// </summary>
    [Fact]
    public async Task GetPlaybackQueueAsync_予期しない例外発生時_INTERNAL_ERRORを返し機密詳細を露出しない()
    {
        // Arrange
        var gateway = new Mock<IPlaybackControlGateway>();
        gateway
            .Setup(g => g.GetPlaybackQueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(@"secret diagnostic C:\private\queue"));

        var sut = new PlaybackTools(gateway.Object, NullLogger<PlaybackTools>.Instance);

        // Act
        CallToolResult result = await sut.GetPlaybackQueueAsync();

        // Assert
        Assert.True(result.IsError);
        using JsonDocument doc = ParsePayload(result);
        JsonElement error = doc.RootElement.GetProperty("error");
        Assert.Equal(McpErrorCodes.INTERNAL_ERROR, error.GetProperty("code").GetString());
        Assert.DoesNotContain("secret", error.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// get_playback_queue ツールの Description 属性に userQueue・albumQueue の区別および launch_app 案内が含まれることを検証します。
    /// </summary>
    [Fact]
    public void GetPlaybackQueueAsync_ツール説明属性_userQueueとalbumQueueおよびlaunch_app案内が含まれる()
    {
        // Arrange
        MethodInfo? method = typeof(PlaybackTools).GetMethod(nameof(PlaybackTools.GetPlaybackQueueAsync));
        Assert.NotNull(method);

        // Act
        DescriptionAttribute? description = method.GetCustomAttribute<DescriptionAttribute>();

        // Assert
        Assert.NotNull(description);
        Assert.Contains("userQueue", description.Description, StringComparison.Ordinal);
        Assert.Contains("albumQueue", description.Description, StringComparison.Ordinal);
        Assert.Contains("launch_app", description.Description, StringComparison.Ordinal);
    }

    private static JsonDocument ParsePayload(CallToolResult result)
    {
        var textBlock = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        return JsonDocument.Parse(textBlock.Text);
    }
}

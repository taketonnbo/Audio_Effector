using System.IO;
using System.Text.Json;
using AudioEffector.Application.Common;
using AudioEffector.McpServer.Configuration;
using AudioEffector.McpServer.Protocol;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Moq;
using AppResult = AudioEffector.Application.Common.Result;

namespace AudioEffector.Tests.McpServer.Protocol;

public sealed class McpResponseFormatterTests
{
    private sealed record SamplePayload(
        string TrackTitle,
        string? OptionalNote,
        McpTransportMode TransportMode);

    /// <summary>
    /// Success および SerializeSuccess が camelCase・null 省略・日本語非エスケープ・enum 文字列化で統一エンベロープを生成することを検証します。
    /// </summary>
    [Fact]
    public void Success_正常データ指定時_統一JSONエンベロープと規約どおりの形式で返却する()
    {
        // Arrange
        var payload = new SamplePayload("夜に駆ける", null, McpTransportMode.Stdio);

        // Act
        CallToolResult sut = McpResponseFormatter.Success(payload);
        string text = GetSingleText(sut);

        // Assert
        Assert.False(sut.IsError ?? false);
        Assert.Contains("夜に駆ける", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", text, StringComparison.Ordinal);

        using JsonDocument doc = JsonDocument.Parse(text);
        JsonElement root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        JsonElement data = root.GetProperty("data");
        Assert.Equal("夜に駆ける", data.GetProperty("trackTitle").GetString());
        Assert.False(data.TryGetProperty("optionalNote", out _));
        Assert.Equal("stdio", data.GetProperty("transportMode").GetString());
    }

    /// <summary>
    /// Error が details 未指定時に details キーを省略し、指定時にそのまま出力することを検証します。
    /// </summary>
    [Fact]
    public void Error_details有無指定時_構造化エラーJSONを正しく生成する()
    {
        // Arrange
        var details = new { parameter = "limit", min = 1, max = 200 };

        // Act
        CallToolResult withoutDetails = McpResponseFormatter.Error(McpErrorCodes.NOT_FOUND, "対象が見つかりません。");
        CallToolResult withDetails = McpResponseFormatter.Error(McpErrorCodes.INVALID_PARAMETER, "limit が範囲外です。", details);

        // Assert
        Assert.True(withoutDetails.IsError);
        using (JsonDocument doc = JsonDocument.Parse(GetSingleText(withoutDetails)))
        {
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            JsonElement err = doc.RootElement.GetProperty("error");
            Assert.Equal(McpErrorCodes.NOT_FOUND, err.GetProperty("code").GetString());
            Assert.Equal("対象が見つかりません。", err.GetProperty("message").GetString());
            Assert.False(err.TryGetProperty("details", out _));
        }

        Assert.True(withDetails.IsError);
        using (JsonDocument doc = JsonDocument.Parse(GetSingleText(withDetails)))
        {
            JsonElement err = doc.RootElement.GetProperty("error");
            Assert.Equal(McpErrorCodes.INVALID_PARAMETER, err.GetProperty("code").GetString());
            JsonElement det = err.GetProperty("details");
            Assert.Equal("limit", det.GetProperty("parameter").GetString());
            Assert.Equal(1, det.GetProperty("min").GetInt32());
            Assert.Equal(200, det.GetProperty("max").GetInt32());
        }
    }

    /// <summary>
    /// FromResult（非ジェネリック）が成功時に正常応答、失敗時に指定エラーコードのエラー応答へ変換することを検証します。
    /// </summary>
    [Fact]
    public void FromResult_成功と失敗のResult指定時_期待される正常およびエラー応答へ変換する()
    {
        // Arrange
        AppResult successResult = AppResult.Success();
        AppResult failureResult = AppResult.Failure("プレイリストが見つかりません。");

        // Act
        CallToolResult okResponse = McpResponseFormatter.FromResult(successResult);
        CallToolResult errResponse = McpResponseFormatter.FromResult(failureResult, errorCode: McpErrorCodes.NOT_FOUND);

        // Assert
        Assert.False(okResponse.IsError ?? false);
        using (JsonDocument doc = JsonDocument.Parse(GetSingleText(okResponse)))
        {
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("ok", doc.RootElement.GetProperty("data").GetProperty("status").GetString());
        }

        Assert.True(errResponse.IsError);
        using (JsonDocument doc = JsonDocument.Parse(GetSingleText(errResponse)))
        {
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            JsonElement err = doc.RootElement.GetProperty("error");
            Assert.Equal(McpErrorCodes.NOT_FOUND, err.GetProperty("code").GetString());
            Assert.Equal("プレイリストが見つかりません。", err.GetProperty("message").GetString());
        }
    }

    /// <summary>
    /// FromResult（ジェネリック）が成功時に Value を data として返し、失敗時にエラー応答へ変換することを検証します。
    /// </summary>
    [Fact]
    public void FromResultGeneric_成功と失敗のResultT指定時_期待される応答へ変換する()
    {
        // Arrange
        Result<SamplePayload> successResult = AppResult.Success(new SamplePayload("テスト曲", "メモ", McpTransportMode.Http));
        Result<SamplePayload> failureResult = AppResult.Failure<SamplePayload>("読み込みに失敗しました。");

        // Act
        CallToolResult okResponse = McpResponseFormatter.FromResult(successResult);
        CallToolResult errResponse = McpResponseFormatter.FromResult(failureResult);

        // Assert
        Assert.False(okResponse.IsError ?? false);
        using (JsonDocument doc = JsonDocument.Parse(GetSingleText(okResponse)))
        {
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.Equal("テスト曲", data.GetProperty("trackTitle").GetString());
            Assert.Equal("メモ", data.GetProperty("optionalNote").GetString());
            Assert.Equal("http", data.GetProperty("transportMode").GetString());
        }

        Assert.True(errResponse.IsError);
        using (JsonDocument doc = JsonDocument.Parse(GetSingleText(errResponse)))
        {
            JsonElement err = doc.RootElement.GetProperty("error");
            Assert.Equal(McpErrorCodes.INTERNAL_ERROR, err.GetProperty("code").GetString());
            Assert.Equal("読み込みに失敗しました。", err.GetProperty("message").GetString());
        }
    }

    /// <summary>
    /// FromException が ArgumentException を INVALID_PARAMETER へ変換しパラメータ名を details に含めることを検証します。
    /// </summary>
    [Fact]
    public void FromException_ArgumentException発生時_INVALID_PARAMETERとパラメータ詳細を返す()
    {
        // Arrange
        var ex = new ArgumentException("limit は 1〜200 の範囲で指定してください。", "limit");

        // Act
        CallToolResult sut = McpResponseFormatter.FromException(ex);

        // Assert
        Assert.True(sut.IsError);
        using JsonDocument doc = JsonDocument.Parse(GetSingleText(sut));
        JsonElement err = doc.RootElement.GetProperty("error");
        Assert.Equal(McpErrorCodes.INVALID_PARAMETER, err.GetProperty("code").GetString());
        Assert.Equal("limit は 1〜200 の範囲で指定してください。", err.GetProperty("message").GetString());
        Assert.Equal("limit", err.GetProperty("details").GetProperty("parameter").GetString());
    }

    /// <summary>
    /// FromException が各種標準例外を対応する標準エラーコードへ変換し、機密パスやスタックトレースを露出しないことを検証します。
    /// </summary>
    [Theory]
    [MemberData(nameof(ExceptionMappingCases))]
    public void FromException_各種例外発生時_対応する標準エラーコードへ変換し内部詳細を露出しない(
        Exception exception,
        string expectedCode,
        string secretToken)
    {
        // Arrange
        var loggerMock = new Mock<ILogger>();
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        // Act
        CallToolResult sut = McpResponseFormatter.FromException(exception, loggerMock.Object);
        string text = GetSingleText(sut);

        // Assert
        Assert.True(sut.IsError);
        Assert.DoesNotContain(secretToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain("at ", text, StringComparison.Ordinal);

        using JsonDocument doc = JsonDocument.Parse(text);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(expectedCode, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// FromException が未分類の内部例外発生時に ILogger へ Error レベルで記録することを検証します。
    /// </summary>
    [Fact]
    public void FromException_未分類の内部例外発生時_ILoggerへErrorログを記録しスタックトレースを露出しない()
    {
        // Arrange
        var loggerMock = new Mock<ILogger>();
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        Exception thrown;
        try
        {
            throw new InvalidOperationException("SECRET_DB_CONNECTION_STRING");
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }

        // Act
        CallToolResult sut = McpResponseFormatter.FromException(thrown, loggerMock.Object);
        string text = GetSingleText(sut);

        // Assert
        Assert.True(sut.IsError);
        Assert.DoesNotContain("SECRET_DB_CONNECTION_STRING", text, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(McpResponseFormatterTests), text, StringComparison.Ordinal);

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                thrown,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    public static IEnumerable<object[]> ExceptionMappingCases()
    {
        yield return new object[]
        {
            new KeyNotFoundException("SECRET_KEY_12345"),
            McpErrorCodes.NOT_FOUND,
            "SECRET_KEY_12345",
        };
        yield return new object[]
        {
            new FileNotFoundException("ファイルが見つかりません", @"C:\SecretUsers\private\track.flac"),
            McpErrorCodes.FILE_NOT_FOUND,
            "SecretUsers",
        };
        yield return new object[]
        {
            new DirectoryNotFoundException(@"C:\SecretUsers\private\music"),
            McpErrorCodes.FILE_NOT_FOUND,
            "SecretUsers",
        };
        yield return new object[]
        {
            new FileLockTimeoutException(@"C:\SecretUsers\AppData\tracks.json", TimeSpan.FromSeconds(5)),
            McpErrorCodes.CONFLICT_LOCK_TIMEOUT,
            "SecretUsers",
        };
        yield return new object[]
        {
            new OperationCanceledException("INTERNAL_CANCEL_REASON"),
            McpErrorCodes.OPERATION_CANCELLED,
            "INTERNAL_CANCEL_REASON",
        };
        yield return new object[]
        {
            new InvalidOperationException("INTERNAL_SECRET_DETAIL"),
            McpErrorCodes.INTERNAL_ERROR,
            "INTERNAL_SECRET_DETAIL",
        };
    }

    private static string GetSingleText(CallToolResult result)
    {
        TextContentBlock block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        return block.Text;
    }
}

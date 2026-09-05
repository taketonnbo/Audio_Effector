using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Repositories;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Application.ApplicationServices;

/// <summary>
/// SettingsApplicationServiceの設定読み込み、型シリアライズ保存、デフォルト値フォールバックを検証するテストクラス
/// </summary>
public sealed class SettingsApplicationServiceTests
{
    private sealed record WindowBounds(double Width, double Height);

    /// <summary>
    /// コンストラクタ引数にnullを指定した場合、ArgumentNullExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_必須引数null指定_ArgumentNullExceptionをスローすること()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SettingsApplicationService(null!));
    }

    /// <summary>
    /// 未設定キーに対してGetSettingAsyncを呼び出した際、指定したデフォルト値が返るかを検証します。
    /// </summary>
    [Fact]
    public async Task GetSettingAsync_未設定キー_デフォルト値を返すこと()
    {
        // Arrange
        var mockRepo = new Mock<ISettingsRepository>();
        mockRepo.Setup(r => r.GetValueAsync("UnknownKey", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        using var sut = new SettingsApplicationService(mockRepo.Object);

        // Act
        var actual = await sut.GetSettingAsync("UnknownKey", 100);

        // Assert
        Assert.Equal(100, actual);
    }

    /// <summary>
    /// 文字列型の設定値が存在する場合、型変換を行わず文字列をそのまま返すかを検証します。
    /// </summary>
    [Fact]
    public async Task GetSettingAsync_文字列値設定済み_文字列をそのまま返すこと()
    {
        // Arrange
        var mockRepo = new Mock<ISettingsRepository>();
        mockRepo.Setup(r => r.GetValueAsync("AppTheme", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Dark");

        using var sut = new SettingsApplicationService(mockRepo.Object);

        // Act
        var actual = await sut.GetSettingAsync<string>("AppTheme");

        // Assert
        Assert.Equal("Dark", actual);
    }

    /// <summary>
    /// 複合オブジェクト型の設定値が存在する場合、JSONデシリアライズして正しく復元されるかを検証します。
    /// </summary>
    [Fact]
    public async Task GetSettingAsync_オブジェクト型設定済み_JSONデシリアライズして復元すること()
    {
        // Arrange
        var expectedBounds = new WindowBounds(1280.0, 720.0);
        string json = JsonSerializer.Serialize(expectedBounds);

        var mockRepo = new Mock<ISettingsRepository>();
        mockRepo.Setup(r => r.GetValueAsync("WindowBounds", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        using var sut = new SettingsApplicationService(mockRepo.Object);

        // Act
        var actual = await sut.GetSettingAsync<WindowBounds>("WindowBounds");

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(expectedBounds.Width, actual.Width);
        Assert.Equal(expectedBounds.Height, actual.Height);
    }

    /// <summary>
    /// 文字列型の値を保存する際、そのままリポジトリのSetValueAsyncへ渡されるかを検証します。
    /// </summary>
    [Fact]
    public async Task SaveSettingAsync_文字列値指定_そのままリポジトリのSetValueAsyncへ渡すこと()
    {
        // Arrange
        var mockRepo = new Mock<ISettingsRepository>();
        using var sut = new SettingsApplicationService(mockRepo.Object);

        // Act
        await sut.SaveSettingAsync("Language", "ja-JP");

        // Assert
        mockRepo.Verify(r => r.SetValueAsync("Language", "ja-JP", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// オブジェクト型の値を保存する際、JSONシリアライズされてリポジトリのSetValueAsyncへ渡されるかを検証します。
    /// </summary>
    [Fact]
    public async Task SaveSettingAsync_オブジェクト型指定_JSONシリアライズしてSetValueAsyncへ渡すこと()
    {
        // Arrange
        var mockRepo = new Mock<ISettingsRepository>();
        using var sut = new SettingsApplicationService(mockRepo.Object);
        var bounds = new WindowBounds(1920.0, 1080.0);

        // Act
        await sut.SaveSettingAsync("WindowBounds", bounds);

        // Assert
        mockRepo.Verify(r => r.SetValueAsync(
            "WindowBounds",
            It.Is<string?>(s => s != null && s.Contains("1920")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// GetAllSettingsAsyncを呼び出した際、リポジトリの全設定ディクショナリが返るかを検証します。
    /// </summary>
    [Fact]
    public async Task GetAllSettingsAsync_呼び出し時_リポジトリの全設定ディクショナリを返すこと()
    {
        // Arrange
        var dict = new Dictionary<string, string>
        {
            ["Theme"] = "Dark",
            ["Volume"] = "80"
        };

        var mockRepo = new Mock<ISettingsRepository>();
        mockRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dict);

        using var sut = new SettingsApplicationService(mockRepo.Object);

        // Act
        var actual = await sut.GetAllSettingsAsync();

        // Assert
        Assert.Equal(2, actual.Count);
        Assert.Equal("Dark", actual["Theme"]);
        Assert.Equal("80", actual["Volume"]);
    }
}

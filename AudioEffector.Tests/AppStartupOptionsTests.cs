using AudioEffector.McpServer.Gateways;

namespace AudioEffector.Tests;

public sealed class AppStartupOptionsTests
{
    /// <summary>
    /// 引数が null の場合、StartInMiniPlayer・ForceNormalWindow ともに false（既定動作）となることを検証します。
    /// </summary>
    [Fact]
    public void Parse_引数がnullの場合_StartInMiniPlayerとForceNormalWindowがFalseになる()
    {
        // Arrange
        string[]? args = null;

        // Act
        AppStartupOptions sut = AppStartupOptions.Parse(args);

        // Assert
        Assert.False(sut.StartInMiniPlayer);
        Assert.False(sut.ForceNormalWindow);
    }

    /// <summary>
    /// 引数が空配列の場合、StartInMiniPlayer・ForceNormalWindow ともに false（既定動作）となることを検証します。
    /// </summary>
    [Fact]
    public void Parse_引数が空配列の場合_StartInMiniPlayerとForceNormalWindowがFalseになる()
    {
        // Arrange
        string[] args = [];

        // Act
        AppStartupOptions sut = AppStartupOptions.Parse(args);

        // Assert
        Assert.False(sut.StartInMiniPlayer);
        Assert.False(sut.ForceNormalWindow);
    }

    /// <summary>
    /// --mini-player 引数が指定された場合（大文字小文字・前後空白・他引数併存を含む）、StartInMiniPlayer が true・ForceNormalWindow が false となることを検証します。
    /// </summary>
    [Theory]
    [InlineData("--mini-player")]
    [InlineData("--MINI-PLAYER")]
    [InlineData("  --mini-player  ")]
    [InlineData("--other-flag", "--mini-player", "value")]
    [InlineData("--normal", "--mini-player")]
    public void Parse_MiniPlayer引数指定時_StartInMiniPlayerがTrueになる(params string[] args)
    {
        // Arrange & Act
        AppStartupOptions sut = AppStartupOptions.Parse(args);

        // Assert
        Assert.True(sut.StartInMiniPlayer);
        Assert.False(sut.ForceNormalWindow);
    }

    /// <summary>
    /// --normal 引数が指定された場合（大文字小文字・前後空白・後勝ち含む）、ForceNormalWindow が true・StartInMiniPlayer が false となることを検証します。
    /// </summary>
    [Theory]
    [InlineData("--normal")]
    [InlineData("--NORMAL")]
    [InlineData("  --normal  ")]
    [InlineData("--other-flag", "--normal", "value")]
    [InlineData("--mini-player", "--normal")]
    public void Parse_Normal引数指定時_ForceNormalWindowがTrueになる(params string[] args)
    {
        // Arrange & Act
        AppStartupOptions sut = AppStartupOptions.Parse(args);

        // Assert
        Assert.False(sut.StartInMiniPlayer);
        Assert.True(sut.ForceNormalWindow);
    }

    /// <summary>
    /// 無関係な引数のみが指定された場合、StartInMiniPlayer・ForceNormalWindow ともに false となることを検証します。
    /// </summary>
    [Theory]
    [InlineData("mini-player")]
    [InlineData("--mini-player=true")]
    [InlineData("normal")]
    [InlineData("--normal=true")]
    [InlineData("   ")]
    public void Parse_無関係な引数のみの場合_StartInMiniPlayerとForceNormalWindowがFalseになる(params string[] args)
    {
        // Arrange & Act
        AppStartupOptions sut = AppStartupOptions.Parse(args);

        // Assert
        Assert.False(sut.StartInMiniPlayer);
        Assert.False(sut.ForceNormalWindow);
    }

    /// <summary>
    /// WPF 側と MCP サーバ側で使用する単一インスタンス Mutex 名および起動引数定数が完全に一致することを検証します。
    /// </summary>
    [Fact]
    public void 定数整合性_WPF側とMCPサーバ側_Mutex名と起動引数が一致する()
    {
        // Arrange & Act
        string wpfMutexName = App.SINGLE_INSTANCE_MUTEX_NAME;
        string mcpMutexName = WpfAppLauncher.SINGLE_INSTANCE_MUTEX_NAME;
        string wpfMiniArg = AppStartupOptions.MINI_PLAYER_ARGUMENT;
        string mcpMiniArg = WpfAppLauncher.MINI_PLAYER_ARGUMENT;
        string wpfNormalArg = AppStartupOptions.NORMAL_ARGUMENT;
        string mcpNormalArg = WpfAppLauncher.NORMAL_ARGUMENT;

        // Assert
        Assert.Equal(wpfMutexName, mcpMutexName);
        Assert.Equal(wpfMiniArg, mcpMiniArg);
        Assert.Equal(wpfNormalArg, mcpNormalArg);
    }
}

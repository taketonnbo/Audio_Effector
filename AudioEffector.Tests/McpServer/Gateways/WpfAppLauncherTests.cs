using System.Diagnostics;
using System.IO;
using AudioEffector.McpServer.Configuration;
using AudioEffector.McpServer.Gateways;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioEffector.Tests.McpServer.Gateways;

public sealed class WpfAppLauncherTests
{
    /// <summary>
    /// WpfAppPath 未指定時はベースディレクトリ配下の AudioEffector.exe を既定パスとして解決することを検証します。
    /// </summary>
    [Fact]
    public void ResolveExecutablePath_WpfAppPath未指定時_ベースディレクトリ配下のAudioEffectorExeを返す()
    {
        // Arrange
        var options = new McpServerOptions();
        using var sut = CreateSut(options, baseDirectory: @"C:\Apps\AudioEffector");

        // Act
        string resolvedPath = sut.ResolveExecutablePath();

        // Assert
        Assert.Equal(@"C:\Apps\AudioEffector\AudioEffector.exe", resolvedPath);
    }

    /// <summary>
    /// WpfAppPath 明示指定時は指定されたパスを解決することを検証します。
    /// </summary>
    [Fact]
    public void ResolveExecutablePath_WpfAppPath指定時_指定パスを返す()
    {
        // Arrange
        var options = new McpServerOptions
        {
            WpfAppPath = @"  C:\Custom\AudioEffector.exe  ",
        };
        using var sut = CreateSut(options, baseDirectory: @"C:\Apps\AudioEffector");

        // Act
        string resolvedPath = sut.ResolveExecutablePath();

        // Assert
        Assert.Equal(@"C:\Custom\AudioEffector.exe", resolvedPath);
    }

    /// <summary>
    /// MiniPlayer モード指定時に --mini-player 引数付きでプロセスを起動し launched=true を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_MiniPlayerモード指定時_MiniPlayer引数付きで起動しlaunchedTrueを返す()
    {
        // Arrange
        bool isRunning = false;
        ProcessStartInfo? capturedStartInfo = null;
        using var sut = CreateSut(
            new McpServerOptions(),
            fileExists: _ => true,
            isAlreadyRunning: () => isRunning,
            processStarter: startInfo =>
            {
                capturedStartInfo = startInfo;
                isRunning = true;
                return new FakeLaunchedProcess();
            });

        // Act
        AppLaunchResult result = await sut.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: true);

        // Assert
        Assert.NotNull(capturedStartInfo);
        Assert.Equal(@"C:\Apps\AudioEffector\AudioEffector.exe", capturedStartInfo.FileName);
        Assert.Contains(WpfAppLauncher.MINI_PLAYER_ARGUMENT, capturedStartInfo.ArgumentList);
        Assert.True(result.Launched);
        Assert.False(result.AlreadyRunning);
        Assert.Equal(WpfAppLauncher.MODE_MINI_PLAYER, result.Mode);
        Assert.False(result.IpcReady);
    }

    /// <summary>
    /// Normal モード指定時に --mini-player 引数なしでプロセスを起動し launched=true を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_Normalモード指定時_引数なしで通常起動しlaunchedTrueを返す()
    {
        // Arrange
        bool isRunning = false;
        ProcessStartInfo? capturedStartInfo = null;
        using var sut = CreateSut(
            new McpServerOptions(),
            fileExists: _ => true,
            isAlreadyRunning: () => isRunning,
            processStarter: startInfo =>
            {
                capturedStartInfo = startInfo;
                isRunning = true;
                return new FakeLaunchedProcess();
            });

        // Act
        AppLaunchResult result = await sut.LaunchAsync(AppLaunchMode.Normal, waitForReady: true);

        // Assert
        Assert.NotNull(capturedStartInfo);
        Assert.Empty(capturedStartInfo.ArgumentList);
        Assert.True(result.Launched);
        Assert.False(result.AlreadyRunning);
        Assert.Equal(WpfAppLauncher.MODE_NORMAL, result.Mode);
        Assert.False(result.IpcReady);
    }

    /// <summary>
    /// すでに起動済みの場合に新規プロセスを起動せず alreadyRunning=true・mode="existing" を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_すでに起動済みの場合_新規起動せずalreadyRunningTrueを返す()
    {
        // Arrange
        int startCount = 0;
        using var sut = CreateSut(
            new McpServerOptions(),
            fileExists: _ => true,
            isAlreadyRunning: () => true,
            processStarter: _ =>
            {
                startCount++;
                return new FakeLaunchedProcess();
            },
            ipcReadinessProbe: _ => Task.FromResult(true));

        // Act
        AppLaunchResult result = await sut.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: true);

        // Assert
        Assert.Equal(0, startCount);
        Assert.False(result.Launched);
        Assert.True(result.AlreadyRunning);
        Assert.Equal(WpfAppLauncher.MODE_EXISTING, result.Mode);
        Assert.True(result.IpcReady);
    }

    /// <summary>
    /// waitForReady=true かつ IPC プローブが構成されている場合、プローブ成功までリトライ待機し ipcReady=true を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_IPCプローブが遅延成功する場合_待機後にipcReadyTrueを返す()
    {
        // Arrange
        int probeCount = 0;
        using var sut = CreateSut(
            new McpServerOptions(),
            fileExists: _ => true,
            isAlreadyRunning: () => false,
            processStarter: _ => new FakeLaunchedProcess(),
            ipcReadinessProbe: _ => Task.FromResult(++probeCount >= 3),
            readyTimeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(10));

        // Act
        AppLaunchResult result = await sut.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: true);

        // Assert
        Assert.True(probeCount >= 3);
        Assert.True(result.Launched);
        Assert.False(result.AlreadyRunning);
        Assert.True(result.IpcReady);
    }

    /// <summary>
    /// waitForReady=false の場合、準備完了を待たずに即座に launched=true を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_waitForReadyがFalseの場合_待機せず即座にlaunchedTrueを返す()
    {
        // Arrange
        using var sut = CreateSut(
            new McpServerOptions(),
            fileExists: _ => true,
            isAlreadyRunning: () => false,
            processStarter: _ => new FakeLaunchedProcess(),
            readyTimeout: TimeSpan.FromMilliseconds(50),
            pollInterval: TimeSpan.FromMilliseconds(10));

        // Act
        AppLaunchResult result = await sut.LaunchAsync(AppLaunchMode.Normal, waitForReady: false);

        // Assert
        Assert.True(result.Launched);
        Assert.False(result.AlreadyRunning);
        Assert.Equal(WpfAppLauncher.MODE_NORMAL, result.Mode);
        Assert.False(result.IpcReady);
    }

    /// <summary>
    /// 実行ファイルが存在しない場合に FileNotFoundException をスローすることを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_実行ファイル未存在時_FileNotFoundExceptionをスローする()
    {
        // Arrange
        using var sut = CreateSut(
            new McpServerOptions(),
            fileExists: _ => false,
            isAlreadyRunning: () => false,
            processStarter: _ => new FakeLaunchedProcess());

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => sut.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: true));
    }

    /// <summary>
    /// 起動後に準備完了状態へ遷移しない場合、タイムアウト経過後に TimeoutException をスローすることを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_起動待機がタイムアウトした場合_TimeoutExceptionをスローする()
    {
        // Arrange
        using var sut = CreateSut(
            new McpServerOptions(),
            fileExists: _ => true,
            isAlreadyRunning: () => false,
            processStarter: _ => new FakeLaunchedProcess(),
            readyTimeout: TimeSpan.FromMilliseconds(60),
            pollInterval: TimeSpan.FromMilliseconds(10));

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() => sut.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: true));
    }

    /// <summary>
    /// 起動直後に子プロセスが終了した場合、InvalidOperationException をスローすることを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_起動したプロセスが準備完了前に終了した場合_InvalidOperationExceptionをスローする()
    {
        // Arrange
        using var sut = CreateSut(
            new McpServerOptions(),
            fileExists: _ => true,
            isAlreadyRunning: () => false,
            processStarter: _ => new FakeLaunchedProcess { HasExited = true, ExitCode = 1 },
            readyTimeout: TimeSpan.FromSeconds(1),
            pollInterval: TimeSpan.FromMilliseconds(10));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: true));
    }

    /// <summary>
    /// 連続した並行呼び出しが発生した場合でも排他制御によりプロセスが多重起動されないことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_並行呼び出し発生時_排他制御により多重起動されない()
    {
        // Arrange
        int startCount = 0;
        int runningFlag = 0;
        var startedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var sut = CreateSut(
            new McpServerOptions(),
            fileExists: _ => true,
            isAlreadyRunning: () => Volatile.Read(ref runningFlag) == 1,
            processStarter: _ =>
            {
                Interlocked.Increment(ref startCount);
                startedTcs.TrySetResult();
                return new FakeLaunchedProcess();
            },
            ipcReadinessProbe: async ct =>
            {
                await Task.Delay(40, ct);
                Volatile.Write(ref runningFlag, 1);
                return true;
            },
            readyTimeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(10));

        // Act
        Task<AppLaunchResult> firstTask = sut.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: true);
        await startedTcs.Task;
        Task<AppLaunchResult> secondTask = sut.LaunchAsync(AppLaunchMode.Normal, waitForReady: true);

        AppLaunchResult[] results = await Task.WhenAll(firstTask, secondTask);

        // Assert
        Assert.Equal(1, startCount);
        Assert.True(results[0].Launched);
        Assert.False(results[0].AlreadyRunning);
        Assert.False(results[1].Launched);
        Assert.True(results[1].AlreadyRunning);
        Assert.Equal(WpfAppLauncher.MODE_EXISTING, results[1].Mode);
    }

    private static WpfAppLauncher CreateSut(
        McpServerOptions options,
        string baseDirectory = @"C:\Apps\AudioEffector",
        Func<string, bool>? fileExists = null,
        Func<bool>? isAlreadyRunning = null,
        Func<ProcessStartInfo, WpfAppLauncher.ILaunchedProcess?>? processStarter = null,
        Func<CancellationToken, Task<bool>>? ipcReadinessProbe = null,
        TimeSpan? readyTimeout = null,
        TimeSpan? pollInterval = null)
    {
        var loggerMock = new Mock<ILogger<WpfAppLauncher>>();
        return new WpfAppLauncher(
            options,
            loggerMock.Object,
            baseDirectory,
            fileExists ?? (_ => true),
            isAlreadyRunning ?? (() => false),
            processStarter ?? (_ => new FakeLaunchedProcess()),
            ipcReadinessProbe,
            readyTimeout ?? TimeSpan.FromSeconds(1),
            pollInterval ?? TimeSpan.FromMilliseconds(10));
    }

    private sealed class FakeLaunchedProcess : WpfAppLauncher.ILaunchedProcess
    {
        public bool HasExited { get; init; }

        public int ExitCode { get; init; }

        public void Dispose()
        {
        }
    }
}

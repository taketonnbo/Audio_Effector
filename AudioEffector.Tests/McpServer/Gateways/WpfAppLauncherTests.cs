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
    /// MiniPlayer モード指定時に --mini-player 引数付きでプロセスを起動し IPC 準備完了後に launched=true, ipcReady=true を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_MiniPlayerモード指定時_MiniPlayer引数付きで起動しlaunchedTrueとipcReadyTrueを返す()
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
            },
            ipcReadinessProbe: _ => Task.FromResult(isRunning));

        // Act
        AppLaunchResult result = await sut.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: true);

        // Assert
        Assert.NotNull(capturedStartInfo);
        Assert.Equal(@"C:\Apps\AudioEffector\AudioEffector.exe", capturedStartInfo.FileName);
        Assert.Contains(WpfAppLauncher.MINI_PLAYER_ARGUMENT, capturedStartInfo.ArgumentList);
        Assert.True(result.Launched);
        Assert.False(result.AlreadyRunning);
        Assert.Equal(WpfAppLauncher.MODE_MINI_PLAYER, result.Mode);
        Assert.True(result.IpcReady);
    }

    /// <summary>
    /// Normal モード指定時に --normal 引数付きでプロセスを起動し launched=true, ipcReady=true を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_Normalモード指定時_Normal引数付きで通常起動しlaunchedTrueとipcReadyTrueを返す()
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
            },
            ipcReadinessProbe: _ => Task.FromResult(isRunning));

        // Act
        AppLaunchResult result = await sut.LaunchAsync(AppLaunchMode.Normal, waitForReady: true);

        // Assert
        Assert.NotNull(capturedStartInfo);
        Assert.Single(capturedStartInfo.ArgumentList, WpfAppLauncher.NORMAL_ARGUMENT);
        Assert.True(result.Launched);
        Assert.False(result.AlreadyRunning);
        Assert.Equal(WpfAppLauncher.MODE_NORMAL, result.Mode);
        Assert.True(result.IpcReady);
    }

    /// <summary>
    /// すでに起動済みの場合に新規プロセスを起動せず、waitForReady=true では IPC 接続可能になるまで待機して alreadyRunning=true, ipcReady=true を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_すでに起動済みかつwaitForReadyTrueの場合_IPC準備完了まで待機しalreadyRunningTrueを返す()
    {
        // Arrange
        int startCount = 0;
        int probeCount = 0;
        using var sut = CreateSut(
            new McpServerOptions(),
            fileExists: _ => true,
            isAlreadyRunning: () => true,
            processStarter: _ =>
            {
                startCount++;
                return new FakeLaunchedProcess();
            },
            ipcReadinessProbe: _ => Task.FromResult(++probeCount >= 3),
            readyTimeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(10));

        // Act
        AppLaunchResult result = await sut.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: true);

        // Assert
        Assert.Equal(0, startCount);
        Assert.True(probeCount >= 3);
        Assert.False(result.Launched);
        Assert.True(result.AlreadyRunning);
        Assert.Equal(WpfAppLauncher.MODE_EXISTING, result.Mode);
        Assert.True(result.IpcReady);
    }

    /// <summary>
    /// すでに起動済みかつ waitForReady=true で IPC が応答しない場合、タイムアウトして TimeoutException をスローすることを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_すでに起動済みかつIPC未応答でwaitForReadyTrueの場合_TimeoutExceptionをスローする()
    {
        // Arrange
        using var sut = CreateSut(
            new McpServerOptions(),
            fileExists: _ => true,
            isAlreadyRunning: () => true,
            processStarter: _ => new FakeLaunchedProcess(),
            ipcReadinessProbe: _ => Task.FromResult(false),
            readyTimeout: TimeSpan.FromMilliseconds(60),
            pollInterval: TimeSpan.FromMilliseconds(10));

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() => sut.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: true));
    }

    /// <summary>
    /// 新規起動後に Mutex が確立されても IPC が応答しない場合、タイムアウトして TimeoutException をスローすることを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_Mutex確立後もIPCが応答しない場合_TimeoutExceptionをスローする()
    {
        // Arrange
        bool mutexCreated = false;
        using var sut = CreateSut(
            new McpServerOptions(),
            fileExists: _ => true,
            isAlreadyRunning: () => mutexCreated,
            processStarter: _ =>
            {
                mutexCreated = true;
                return new FakeLaunchedProcess();
            },
            ipcReadinessProbe: _ => Task.FromResult(false),
            readyTimeout: TimeSpan.FromMilliseconds(60),
            pollInterval: TimeSpan.FromMilliseconds(10));

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() => sut.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: true));
    }

    /// <summary>
    /// waitForReady=true で IPC プローブが遅延成功する場合、プローブ成功までリトライ待機し ipcReady=true を返すことを検証します。
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
            ipcReadinessProbe: _ => Task.FromResult(false),
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
    /// Mutex の確立が遅れている状態で waitForReady=false を 2 回連続で呼び出しても、起動中プロセス追跡により起動回数が 1 回に抑止されることを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_Mutex未確立のままwaitForReadyFalseで2回呼び出した場合_起動回数が1回になり2回目はalreadyRunningTrueを返す()
    {
        // Arrange
        int startCount = 0;
        using var sut = CreateSut(
            new McpServerOptions(),
            fileExists: _ => true,
            isAlreadyRunning: () => false,
            processStarter: _ =>
            {
                startCount++;
                return new FakeLaunchedProcess();
            },
            ipcReadinessProbe: _ => Task.FromResult(false));

        // Act
        AppLaunchResult first = await sut.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: false);
        AppLaunchResult second = await sut.LaunchAsync(AppLaunchMode.Normal, waitForReady: false);

        // Assert
        Assert.Equal(1, startCount);
        Assert.True(first.Launched);
        Assert.False(first.AlreadyRunning);
        Assert.Equal(WpfAppLauncher.MODE_MINI_PLAYER, first.Mode);
        Assert.False(second.Launched);
        Assert.True(second.AlreadyRunning);
        Assert.Equal(WpfAppLauncher.MODE_EXISTING, second.Mode);
    }

    /// <summary>
    /// 1 回目が waitForReady=false で起動直後の Mutex 未確立状態に 2 回目が waitForReady=true で呼ばれた場合、同じ起動に合流して IPC 準備完了まで待機することを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_waitForReadyFalse直後にMutex未確立でwaitForReadyTrueを呼んだ場合_既存起動へ合流しIPC準備完了まで待機する()
    {
        // Arrange
        int startCount = 0;
        int probeCount = 0;
        using var sut = CreateSut(
            new McpServerOptions(),
            fileExists: _ => true,
            isAlreadyRunning: () => false,
            processStarter: _ =>
            {
                startCount++;
                return new FakeLaunchedProcess();
            },
            ipcReadinessProbe: _ => Task.FromResult(++probeCount >= 3),
            readyTimeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(10));

        // Act
        AppLaunchResult first = await sut.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: false);
        AppLaunchResult second = await sut.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: true);

        // Assert
        Assert.Equal(1, startCount);
        Assert.True(first.Launched);
        Assert.False(first.IpcReady);
        Assert.False(second.Launched);
        Assert.True(second.AlreadyRunning);
        Assert.Equal(WpfAppLauncher.MODE_EXISTING, second.Mode);
        Assert.True(second.IpcReady);
    }

    /// <summary>
    /// 追跡中の起動プロセスが終了した後は、次回呼び出しで再度新規起動できることを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_追跡中プロセス終了後に再呼び出しした場合_新規プロセスを起動する()
    {
        // Arrange
        int startCount = 0;
        using var firstProcess = new FakeLaunchedProcess();
        using var sut = CreateSut(
            new McpServerOptions(),
            fileExists: _ => true,
            isAlreadyRunning: () => false,
            processStarter: _ => ++startCount == 1 ? firstProcess : new FakeLaunchedProcess(),
            ipcReadinessProbe: _ => Task.FromResult(false));

        // Act
        AppLaunchResult first = await sut.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: false);
        firstProcess.HasExited = true;
        AppLaunchResult second = await sut.LaunchAsync(AppLaunchMode.Normal, waitForReady: false);

        // Assert
        Assert.Equal(2, startCount);
        Assert.True(first.Launched);
        Assert.True(second.Launched);
        Assert.False(second.AlreadyRunning);
        Assert.Equal(WpfAppLauncher.MODE_NORMAL, second.Mode);
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
            processStarter: _ => new FakeLaunchedProcess(),
            ipcReadinessProbe: _ => Task.FromResult(false));

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => sut.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: true));
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
            ipcReadinessProbe: _ => Task.FromResult(false),
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
        Assert.True(results[0].IpcReady);
        Assert.False(results[1].Launched);
        Assert.True(results[1].AlreadyRunning);
        Assert.Equal(WpfAppLauncher.MODE_EXISTING, results[1].Mode);
        Assert.True(results[1].IpcReady);
    }

    /// <summary>
    /// 独立した 2 つのランチャー（別 MCP サーバプロセス相当）から Mutex 未確立のまま waitForReady=false で同時呼び出しした場合、セッション調停により起動回数が 1 回となり 1 つだけ launched=true・もう 1 つは alreadyRunning=true を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_独立した2つのランチャーからMutex未確立でwaitForReadyFalse同時呼び出し時_起動回数が1回になり結果が一致する()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorLaunchCoord_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            int startCount = 0;
            var activePids = new HashSet<int>();
            object syncRoot = new();
            string scopeKey = $"test-{Guid.NewGuid():N}";

            IAppLaunchCoordinator CreateCoordinator() => new SessionFileAppLaunchCoordinator(
                tempDir,
                scopeKey,
                identity =>
                {
                    lock (syncRoot)
                    {
                        return activePids.Contains(identity.ProcessId);
                    }
                });

            WpfAppLauncher.ILaunchedProcess StartFakeProcess(ProcessStartInfo _)
            {
                int pid = 10000 + Interlocked.Increment(ref startCount);
                lock (syncRoot)
                {
                    activePids.Add(pid);
                }

                return new FakeLaunchedProcess(pid);
            }

            using var launcherA = CreateSut(
                new McpServerOptions(),
                isAlreadyRunning: () => false,
                processStarter: StartFakeProcess,
                ipcReadinessProbe: _ => Task.FromResult(false),
                launchCoordinator: CreateCoordinator());
            using var launcherB = CreateSut(
                new McpServerOptions(),
                isAlreadyRunning: () => false,
                processStarter: StartFakeProcess,
                ipcReadinessProbe: _ => Task.FromResult(false),
                launchCoordinator: CreateCoordinator());

            // Act
            AppLaunchResult[] results = await Task.WhenAll(
                launcherA.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: false),
                launcherB.LaunchAsync(AppLaunchMode.Normal, waitForReady: false));

            // Assert
            Assert.Equal(1, startCount);
            Assert.Single(results, r => r.Launched && !r.AlreadyRunning);
            AppLaunchResult joined = Assert.Single(results, r => !r.Launched && r.AlreadyRunning);
            Assert.Equal(WpfAppLauncher.MODE_EXISTING, joined.Mode);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// 独立した 2 つのランチャーから Mutex 未確立のまま waitForReady=true で同時呼び出しした場合、起動回数が 1 回に調停され、両者ともエラーにならず ipcReady=true で完了することを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_独立した2つのランチャーからMutex未確立でwaitForReadyTrue同時呼び出し時_先行起動へ合流し両者ともipcReadyTrueで完了する()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorLaunchCoordWait_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            int startCount = 0;
            int ipcReadyFlag = 0;
            var activePids = new HashSet<int>();
            object syncRoot = new();
            string scopeKey = $"test-{Guid.NewGuid():N}";

            IAppLaunchCoordinator CreateCoordinator() => new SessionFileAppLaunchCoordinator(
                tempDir,
                scopeKey,
                identity =>
                {
                    lock (syncRoot)
                    {
                        return activePids.Contains(identity.ProcessId);
                    }
                });

            WpfAppLauncher.ILaunchedProcess StartFakeProcess(ProcessStartInfo startInfo)
            {
                _ = startInfo;
                int pid = 20000 + Interlocked.Increment(ref startCount);
                lock (syncRoot)
                {
                    activePids.Add(pid);
                }

                _ = Task.Run(async () =>
                {
                    await Task.Delay(50);
                    Volatile.Write(ref ipcReadyFlag, 1);
                });

                return new FakeLaunchedProcess(pid);
            }

            using var launcherA = CreateSut(
                new McpServerOptions(),
                isAlreadyRunning: () => false,
                processStarter: StartFakeProcess,
                ipcReadinessProbe: _ => Task.FromResult(Volatile.Read(ref ipcReadyFlag) == 1),
                launchCoordinator: CreateCoordinator(),
                readyTimeout: TimeSpan.FromSeconds(2),
                pollInterval: TimeSpan.FromMilliseconds(10));
            using var launcherB = CreateSut(
                new McpServerOptions(),
                isAlreadyRunning: () => false,
                processStarter: StartFakeProcess,
                ipcReadinessProbe: _ => Task.FromResult(Volatile.Read(ref ipcReadyFlag) == 1),
                launchCoordinator: CreateCoordinator(),
                readyTimeout: TimeSpan.FromSeconds(2),
                pollInterval: TimeSpan.FromMilliseconds(10));

            // Act
            AppLaunchResult[] results = await Task.WhenAll(
                launcherA.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: true),
                launcherB.LaunchAsync(AppLaunchMode.Normal, waitForReady: true));

            // Assert
            Assert.Equal(1, startCount);
            AppLaunchResult launched = Assert.Single(results, r => r.Launched && !r.AlreadyRunning);
            AppLaunchResult joined = Assert.Single(results, r => !r.Launched && r.AlreadyRunning);
            Assert.True(launched.IpcReady);
            Assert.True(joined.IpcReady);
            Assert.Equal(WpfAppLauncher.MODE_EXISTING, joined.Mode);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// 子プロセス生成直後に 1 回目の要求がキャンセルされても、セッション共有の起動記録が確定され、後続の別ランチャーが二重起動しないことを検証します。
    /// </summary>
    [Fact]
    public async Task LaunchAsync_子プロセス生成直後にキャンセルされた場合_起動記録が確定され別ランチャーが二重起動しない()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorLaunchCancel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            int startCount = 0;
            var activePids = new HashSet<int>();
            object syncRoot = new();
            string scopeKey = $"test-{Guid.NewGuid():N}";
            using var ctsA = new CancellationTokenSource();

            IAppLaunchCoordinator CreateCoordinator() => new SessionFileAppLaunchCoordinator(
                tempDir,
                scopeKey,
                identity =>
                {
                    lock (syncRoot)
                    {
                        return activePids.Contains(identity.ProcessId);
                    }
                });

            WpfAppLauncher.ILaunchedProcess StartAndCancel(ProcessStartInfo startInfo)
            {
                _ = startInfo;
                int pid = 30000 + Interlocked.Increment(ref startCount);
                lock (syncRoot)
                {
                    activePids.Add(pid);
                }

                // 子プロセス生成直後に呼び出し元トークンがキャンセルされるケースを再現
                ctsA.Cancel();
                return new FakeLaunchedProcess(pid);
            }

            using var launcherA = CreateSut(
                new McpServerOptions(),
                isAlreadyRunning: () => false,
                processStarter: StartAndCancel,
                ipcReadinessProbe: _ => Task.FromResult(false),
                launchCoordinator: CreateCoordinator());
            using var launcherB = CreateSut(
                new McpServerOptions(),
                isAlreadyRunning: () => false,
                processStarter: _ =>
                {
                    int pid = 30000 + Interlocked.Increment(ref startCount);
                    return new FakeLaunchedProcess(pid);
                },
                ipcReadinessProbe: _ => Task.FromResult(false),
                launchCoordinator: CreateCoordinator());

            // Act
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => launcherA.LaunchAsync(AppLaunchMode.MiniPlayer, waitForReady: true, cancellationToken: ctsA.Token));
            AppLaunchResult second = await launcherB.LaunchAsync(AppLaunchMode.Normal, waitForReady: false);

            // Assert
            Assert.Equal(1, startCount);
            Assert.False(second.Launched);
            Assert.True(second.AlreadyRunning);
            Assert.Equal(WpfAppLauncher.MODE_EXISTING, second.Mode);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// SessionFileAppLaunchCoordinator の既定保存先ディレクトリが TMP/TEMP ではなく LocalApplicationData 配下の共通ディレクトリとなることを検証します。
    /// </summary>
    [Fact]
    public void GetDefaultCoordinatorDirectory_既定保存先_LocalApplicationData配下の共通ディレクトリを返す()
    {
        // Arrange
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AudioEffector",
            "ipc");

        // Act
        string actual = SessionFileAppLaunchCoordinator.GetDefaultCoordinatorDirectory();

        // Assert
        Assert.Equal(expected, actual);
    }

    private static WpfAppLauncher CreateSut(
        McpServerOptions options,
        string baseDirectory = @"C:\Apps\AudioEffector",
        Func<string, bool>? fileExists = null,
        Func<bool>? isAlreadyRunning = null,
        Func<ProcessStartInfo, WpfAppLauncher.ILaunchedProcess?>? processStarter = null,
        Func<CancellationToken, Task<bool>>? ipcReadinessProbe = null,
        IAppLaunchCoordinator? launchCoordinator = null,
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
            ipcReadinessProbe ?? (_ => Task.FromResult(true)),
            launchCoordinator,
            readyTimeout ?? TimeSpan.FromSeconds(1),
            pollInterval ?? TimeSpan.FromMilliseconds(10));
    }

    private sealed class FakeLaunchedProcess(int processId = 12345) : WpfAppLauncher.ILaunchedProcess
    {
        public LaunchedProcessIdentity Identity { get; } = new(processId, 1000L);

        public bool HasExited { get; set; }

        public int ExitCode { get; init; }

        public void Dispose()
        {
        }
    }
}

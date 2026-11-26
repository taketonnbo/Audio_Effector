using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Infrastructure;
using AudioEffector.McpServer.DependencyInjection;
using AudioEffector.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace AudioEffector.Tests.McpServer.Hosting;

public sealed class StdioMcpIntegrationTests : IDisposable
{
    private readonly string _tempDataDir;

    public StdioMcpIntegrationTests()
    {
        _tempDataDir = Path.Combine(Path.GetTempPath(), $"AudioEffectorMcpIntegration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDataDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDataDir))
        {
            try
            {
                Directory.Delete(_tempDataDir, recursive: true);
            }
            catch (IOException)
            {
                // 子プロセス終了直後のハンドル解放遅延は無視する
            }
        }
    }

    /// <summary>
    /// AudioEffector.McpServer を stdio モードで実プロセス起動し、initialize → tools/list → tools/call（ping / get_server_info / 未知ツールエラー）の一連の通信と stdout 非汚染を検証します。
    /// </summary>
    [Fact]
    public async Task Stdio通信_起動からツール実行とエラー応答まで_JSONRPC往復が成功しstdoutが汚染されない()
    {
        // Arrange: 隔離データディレクトリへ 2 件の楽曲を事前登録する
        await SeedTracksAsync(_tempDataDir, 2);

        using Process sut = StartMcpServerProcess(_tempDataDir);
        var stderrTask = sut.StandardError.ReadToEndAsync();
        var allStdoutLines = new List<string>();

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // Act & Assert 1: initialize
            await SendJsonLineAsync(
                sut.StandardInput,
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"AudioEffector.Tests","version":"1.0.0"}}}""",
                timeoutCts.Token);

            string initLine = await ReadStdoutLineAsync(sut.StandardOutput, allStdoutLines, timeoutCts.Token);
            using (JsonDocument initDoc = JsonDocument.Parse(initLine))
            {
                JsonElement root = initDoc.RootElement;
                Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
                Assert.Equal(1, root.GetProperty("id").GetInt32());
                JsonElement serverInfo = root.GetProperty("result").GetProperty("serverInfo");
                Assert.Equal(McpServiceCollectionExtensions.SERVER_NAME, serverInfo.GetProperty("name").GetString());
                Assert.False(string.IsNullOrWhiteSpace(serverInfo.GetProperty("version").GetString()));
            }

            // notifications/initialized を送信
            await SendJsonLineAsync(
                sut.StandardInput,
                """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
                timeoutCts.Token);

            // Act & Assert 2: tools/list
            await SendJsonLineAsync(
                sut.StandardInput,
                """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""",
                timeoutCts.Token);

            string listLine = await ReadStdoutLineAsync(sut.StandardOutput, allStdoutLines, timeoutCts.Token);
            using (JsonDocument listDoc = JsonDocument.Parse(listLine))
            {
                JsonElement root = listDoc.RootElement;
                Assert.Equal(2, root.GetProperty("id").GetInt32());
                List<string?> toolNames = root
                    .GetProperty("result")
                    .GetProperty("tools")
                    .EnumerateArray()
                    .Select(t => t.GetProperty("name").GetString())
                    .ToList();
                Assert.Contains("ping", toolNames);
                Assert.Contains("get_server_info", toolNames);
                Assert.Contains("search_tracks", toolNames);
                Assert.Contains("search_albums", toolNames);
                Assert.Contains("search_playlists", toolNames);
                Assert.Contains("launch_app", toolNames);
                Assert.Contains("play_track", toolNames);
            }

            // Act & Assert 3: tools/call (ping)
            await SendJsonLineAsync(
                sut.StandardInput,
                """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"ping","arguments":{"message":"疎通テスト"}}}""",
                timeoutCts.Token);

            string pingLine = await ReadStdoutLineAsync(sut.StandardOutput, allStdoutLines, timeoutCts.Token);
            using (JsonDocument pingDoc = JsonDocument.Parse(pingLine))
            {
                JsonElement result = pingDoc.RootElement.GetProperty("result");
                Assert.False(result.TryGetProperty("isError", out JsonElement isErr) && isErr.GetBoolean());
                string payloadText = result.GetProperty("content")[0].GetProperty("text").GetString()!;

                using JsonDocument payloadDoc = JsonDocument.Parse(payloadText);
                Assert.True(payloadDoc.RootElement.GetProperty("ok").GetBoolean());
                JsonElement data = payloadDoc.RootElement.GetProperty("data");
                Assert.Equal("ok", data.GetProperty("status").GetString());
                Assert.Equal("stdio", data.GetProperty("transport").GetString());
                Assert.Equal("疎通テスト", data.GetProperty("echo").GetString());
            }

            // Act & Assert 4: tools/call (get_server_info)
            await SendJsonLineAsync(
                sut.StandardInput,
                """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_server_info","arguments":{}}}""",
                timeoutCts.Token);

            string infoLine = await ReadStdoutLineAsync(sut.StandardOutput, allStdoutLines, timeoutCts.Token);
            using (JsonDocument infoDoc = JsonDocument.Parse(infoLine))
            {
                JsonElement result = infoDoc.RootElement.GetProperty("result");
                Assert.False(result.TryGetProperty("isError", out JsonElement isErr) && isErr.GetBoolean());
                string payloadText = result.GetProperty("content")[0].GetProperty("text").GetString()!;

                using JsonDocument payloadDoc = JsonDocument.Parse(payloadText);
                Assert.True(payloadDoc.RootElement.GetProperty("ok").GetBoolean());
                JsonElement data = payloadDoc.RootElement.GetProperty("data");
                Assert.Equal(2, data.GetProperty("libraryTrackCount").GetInt32());
                Assert.Equal(_tempDataDir, data.GetProperty("dataDirectory").GetString());
                Assert.Equal(SystemTools.WPF_IPC_STATUS_NOT_CONFIGURED, data.GetProperty("wpfIpcStatus").GetString());
            }

            // Act & Assert 4b: tools/call (search_tracks)
            await SendJsonLineAsync(
                sut.StandardInput,
                """{"jsonrpc":"2.0","id":40,"method":"tools/call","params":{"name":"search_tracks","arguments":{"keyword":"統合テスト曲 1","limit":10,"offset":0}}}""",
                timeoutCts.Token);

            string searchLine = await ReadStdoutLineAsync(sut.StandardOutput, allStdoutLines, timeoutCts.Token);
            using (JsonDocument searchDoc = JsonDocument.Parse(searchLine))
            {
                JsonElement result = searchDoc.RootElement.GetProperty("result");
                Assert.False(result.TryGetProperty("isError", out JsonElement isErr) && isErr.GetBoolean());
                string payloadText = result.GetProperty("content")[0].GetProperty("text").GetString()!;

                using JsonDocument payloadDoc = JsonDocument.Parse(payloadText);
                Assert.True(payloadDoc.RootElement.GetProperty("ok").GetBoolean());
                JsonElement data = payloadDoc.RootElement.GetProperty("data");
                Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
                Assert.Equal(1, data.GetProperty("count").GetInt32());
                JsonElement track = Assert.Single(data.GetProperty("tracks").EnumerateArray());
                Assert.Equal("統合テスト曲 1", track.GetProperty("title").GetString());
                Assert.Equal("テストアーティスト", track.GetProperty("artist").GetString());
            }

            // Act & Assert 4b-2: tools/call (search_playlists を search_albums より先に呼び、旧WPF形式と最新お気に入り反映を検証)
            await SendJsonLineAsync(
                sut.StandardInput,
                """{"jsonrpc":"2.0","id":402,"method":"tools/call","params":{"name":"search_playlists","arguments":{"keyword":"WPF保存プレイリスト","includeTracks":true,"limit":10,"offset":0}}}""",
                timeoutCts.Token);

            string searchPlaylistsLine = await ReadStdoutLineAsync(sut.StandardOutput, allStdoutLines, timeoutCts.Token);
            using (JsonDocument searchPlaylistsDoc = JsonDocument.Parse(searchPlaylistsLine))
            {
                JsonElement result = searchPlaylistsDoc.RootElement.GetProperty("result");
                Assert.False(result.TryGetProperty("isError", out JsonElement isErr) && isErr.GetBoolean());
                string payloadText = result.GetProperty("content")[0].GetProperty("text").GetString()!;

                using JsonDocument payloadDoc = JsonDocument.Parse(payloadText);
                Assert.True(payloadDoc.RootElement.GetProperty("ok").GetBoolean());
                JsonElement data = payloadDoc.RootElement.GetProperty("data");
                Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
                Assert.Equal(1, data.GetProperty("count").GetInt32());
                JsonElement playlist = Assert.Single(data.GetProperty("playlists").EnumerateArray());
                Assert.NotEqual(Guid.Empty, playlist.GetProperty("id").GetGuid());
                Assert.Equal("WPF保存プレイリスト", playlist.GetProperty("name").GetString());
                Assert.Equal(2, playlist.GetProperty("trackCount").GetInt32());
                List<JsonElement> tracks = playlist.GetProperty("tracks").EnumerateArray().ToList();
                Assert.Equal(2, tracks.Count);
                Assert.Equal("統合テスト曲 2", tracks[0].GetProperty("title").GetString());
                Assert.False(tracks[0].GetProperty("isFavorite").GetBoolean());
                Assert.Equal("統合テスト曲 1", tracks[1].GetProperty("title").GetString());
                Assert.True(tracks[1].GetProperty("isFavorite").GetBoolean());
            }

            // Act & Assert 4b-3: tools/call (search_albums)
            await SendJsonLineAsync(
                sut.StandardInput,
                """{"jsonrpc":"2.0","id":401,"method":"tools/call","params":{"name":"search_albums","arguments":{"keyword":"テストアルバム","includeTracks":true,"limit":10,"offset":0}}}""",
                timeoutCts.Token);

            string searchAlbumsLine = await ReadStdoutLineAsync(sut.StandardOutput, allStdoutLines, timeoutCts.Token);
            using (JsonDocument searchAlbumsDoc = JsonDocument.Parse(searchAlbumsLine))
            {
                JsonElement result = searchAlbumsDoc.RootElement.GetProperty("result");
                Assert.False(result.TryGetProperty("isError", out JsonElement isErr) && isErr.GetBoolean());
                string payloadText = result.GetProperty("content")[0].GetProperty("text").GetString()!;

                using JsonDocument payloadDoc = JsonDocument.Parse(payloadText);
                Assert.True(payloadDoc.RootElement.GetProperty("ok").GetBoolean());
                JsonElement data = payloadDoc.RootElement.GetProperty("data");
                Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
                Assert.Equal(1, data.GetProperty("count").GetInt32());
                JsonElement album = Assert.Single(data.GetProperty("albums").EnumerateArray());
                Assert.NotEqual(Guid.Empty, album.GetProperty("id").GetGuid());
                Assert.Equal("テストアルバム", album.GetProperty("name").GetString());
                Assert.Equal("テストアーティスト", album.GetProperty("artist").GetString());
                Assert.Equal(2, album.GetProperty("trackCount").GetInt32());
                Assert.Equal(2, album.GetProperty("tracks").GetArrayLength());
            }

            // Act & Assert 4c: tools/call (launch_app 不正 mode パラメータ検証)
            await SendJsonLineAsync(
                sut.StandardInput,
                """{"jsonrpc":"2.0","id":41,"method":"tools/call","params":{"name":"launch_app","arguments":{"mode":"invalid_mode"}}}""",
                timeoutCts.Token);

            string launchErrLine = await ReadStdoutLineAsync(sut.StandardOutput, allStdoutLines, timeoutCts.Token);
            using (JsonDocument launchErrDoc = JsonDocument.Parse(launchErrLine))
            {
                JsonElement result = launchErrDoc.RootElement.GetProperty("result");
                Assert.True(result.GetProperty("isError").GetBoolean());
                string payloadText = result.GetProperty("content")[0].GetProperty("text").GetString()!;

                using JsonDocument payloadDoc = JsonDocument.Parse(payloadText);
                Assert.False(payloadDoc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("INVALID_PARAMETER", payloadDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
            }

            // Act & Assert 4d: tools/call (play_track 引数未指定パラメータ検証)
            await SendJsonLineAsync(
                sut.StandardInput,
                """{"jsonrpc":"2.0","id":42,"method":"tools/call","params":{"name":"play_track","arguments":{}}}""",
                timeoutCts.Token);

            string playErrLine = await ReadStdoutLineAsync(sut.StandardOutput, allStdoutLines, timeoutCts.Token);
            using (JsonDocument playErrDoc = JsonDocument.Parse(playErrLine))
            {
                JsonElement result = playErrDoc.RootElement.GetProperty("result");
                Assert.True(result.GetProperty("isError").GetBoolean());
                string payloadText = result.GetProperty("content")[0].GetProperty("text").GetString()!;

                using JsonDocument payloadDoc = JsonDocument.Parse(payloadText);
                Assert.False(payloadDoc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("INVALID_PARAMETER", payloadDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
            }

            // Act & Assert 5: 存在しないツール呼び出し（エラー発生時も stdout が有効な JSON-RPC のみであること）
            await SendJsonLineAsync(
                sut.StandardInput,
                """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"non_existent_tool","arguments":{}}}""",
                timeoutCts.Token);

            string unknownLine = await ReadStdoutLineAsync(sut.StandardOutput, allStdoutLines, timeoutCts.Token);
            using (JsonDocument unknownDoc = JsonDocument.Parse(unknownLine))
            {
                JsonElement root = unknownDoc.RootElement;
                Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
                Assert.Equal(5, root.GetProperty("id").GetInt32());
                bool hasError = root.TryGetProperty("error", out _)
                    || (root.TryGetProperty("result", out JsonElement res)
                        && res.TryGetProperty("isError", out JsonElement resErr)
                        && resErr.GetBoolean());
                Assert.True(hasError);
            }

            // Act & Assert 6: stdin EOF でグレースフルシャットダウンし、残余 stdout に空行やゴミが一切出ないこと
            sut.StandardInput.Close();
            while (await sut.StandardOutput.ReadLineAsync(timeoutCts.Token) is { } trailingLine)
            {
                allStdoutLines.Add(trailingLine);
            }

            await sut.WaitForExitAsync(timeoutCts.Token);
            string stderrOutput = await stderrTask;

            Assert.Equal(0, sut.ExitCode);
            Assert.NotEmpty(allStdoutLines);

            // 全フェーズの stdout 行（EOF までの全行）が単独の空行を含まずすべて有効な JSON-RPC 2.0 オブジェクトであることを検証
            foreach (string line in allStdoutLines)
            {
                Assert.False(string.IsNullOrWhiteSpace(line), "stdout に空行または空白行が混入しました。");
                using JsonDocument doc = JsonDocument.Parse(line);
                Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
                Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
            }

            // Debug レベル起動時のホストログは stderr 側へ分離出力されていること（stdout 非汚染の補完確認）
            Assert.False(string.IsNullOrWhiteSpace(stderrOutput));
        }
        finally
        {
            if (!sut.HasExited)
            {
                sut.Kill(entireProcessTree: true);
            }
        }
    }

    private static async Task SeedTracksAsync(string dataDirectory, int count)
    {
        var services = new ServiceCollection();
        services.AddInfrastructureServices(dataDirectory);
        await using ServiceProvider provider = services.BuildServiceProvider();
        ITrackRepository repo = provider.GetRequiredService<ITrackRepository>();

        var tracks = Enumerable.Range(1, count)
            .Select(i => new Track
            {
                Title = $"統合テスト曲 {i}",
                Artist = "テストアーティスト",
                Album = "テストアルバム",
                FilePath = Path.Combine(dataDirectory, $"track_{i}.flac"),
            })
            .ToList();

        await repo.SaveRangeAsync(tracks);

        // favorites.json に 1 曲目のみ登録（tracks.json の IsFavorite は false のまま）
        IFavoriteRepository favoriteRepo = provider.GetRequiredService<IFavoriteRepository>();
        await favoriteRepo.AddOrUpdateAsync(FavoriteTrackEntry.FromTrack(tracks[0]));

        // 旧 WPF 保存形式（Name と TrackPaths のみ、Id 欠落）で playlists.json を書き出す
        string playlistsJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                Name = "WPF保存プレイリスト",
                TrackPaths = new[] { tracks[1].FilePath, tracks[0].FilePath },
            },
        });
        await File.WriteAllTextAsync(Path.Combine(dataDirectory, "playlists.json"), playlistsJson);
    }

    private static Process StartMcpServerProcess(string dataDirectory)
    {
        string baseDir = AppContext.BaseDirectory;
        string exePath = Path.Combine(baseDir, "AudioEffector.McpServer.exe");
        string dllPath = Path.Combine(baseDir, "AudioEffector.McpServer.dll");

        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = utf8NoBom,
            StandardOutputEncoding = utf8NoBom,
            StandardErrorEncoding = utf8NoBom,
            WorkingDirectory = baseDir,
        };

        if (File.Exists(exePath))
        {
            startInfo.FileName = exePath;
        }
        else
        {
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add(dllPath);
        }

        startInfo.ArgumentList.Add("--transport");
        startInfo.ArgumentList.Add("stdio");
        startInfo.ArgumentList.Add("--data-dir");
        startInfo.ArgumentList.Add(dataDirectory);
        startInfo.ArgumentList.Add("--log-level");
        startInfo.ArgumentList.Add("Debug");

        Process? process = Process.Start(startInfo);
        Assert.NotNull(process);
        return process;
    }

    private static async Task SendJsonLineAsync(StreamWriter stdin, string jsonLine, CancellationToken cancellationToken)
    {
        await stdin.WriteLineAsync(jsonLine.AsMemory(), cancellationToken);
        await stdin.FlushAsync(cancellationToken);
    }

    private static async Task<string> ReadStdoutLineAsync(
        StreamReader stdout,
        List<string> collectedLines,
        CancellationToken cancellationToken)
    {
        string? line = await stdout.ReadLineAsync(cancellationToken);
        Assert.NotNull(line);
        collectedLines.Add(line);
        Assert.False(string.IsNullOrWhiteSpace(line), "stdout に空行または空白行が混入しました。");
        return line;
    }
}

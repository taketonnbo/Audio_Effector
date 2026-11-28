using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Presentation.Converters;
using AudioEffector.Presentation.ViewModels;
using AudioEffector.Presentation.Views;
using AudioEffector.Tests.Presentation.Diagnostics;
using Moq;
using Xunit;
using Track = AudioEffector.Domain.Entities.Track;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="LrcGeneratorView"/> の実ホスト Window 上での全状態 XAML バインディングエラー 0 件・UI 仮想化・ScrollViewer ビューポート検証・Button.IsEnabled 実反映・テーマ両対応を検証するテストクラス
/// </summary>
public sealed class LrcGeneratorViewTests
{
    private sealed class DispatcherUiDispatcherStub(System.Windows.Threading.Dispatcher dispatcher) : IUiDispatcher
    {
        public void Invoke(Action action)
        {
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Invoke(action);
            }
        }

        public void BeginInvoke(Action action)
        {
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                _ = dispatcher.BeginInvoke(action);
            }
        }
    }

    private sealed class StubAlignmentEngine : IAudioAlignmentEngine
    {
        public string EngineName => "StubEngine";
        public Func<AlignmentRequest, IProgress<AlignmentProgress>?, CancellationToken, Task<AlignmentResult>>? Handler { get; set; }

        public Task<AlignmentResult> GenerateAsync(
            AlignmentRequest request,
            IProgress<AlignmentProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (Handler is not null)
            {
                return Handler(request, progress, cancellationToken);
            }

            var lines = Enumerable.Range(0, 30)
                .Select(i => new LyricLine(i, TimeSpan.FromSeconds((i + 1) * 4), $"プレビュー行 #{i + 1:00} の歌詞テキスト"))
                .ToArray();
            var doc = new LyricsDocument(lines, isSynced: true, title: request.Title);
            return Task.FromResult(new AlignmentResult(
                doc,
                "[00:04.00]プレビュー行 #01 の歌詞テキスト",
                Array.Empty<AlignmentSegment>(),
                request.Mode,
                EngineName));
        }
    }

    private sealed class StubTextEncodingService : ITextEncodingService
    {
        public Exception? SaveException { get; set; }

        public Task<TextEncodingInspectionResult> InspectAsync(string textFilePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new TextEncodingInspectionResult(textFilePath, false, false, "Shift-JIS (CP932)", 2, 2));

        public Task<string> ReadAsUtf8TextAsync(string textFilePath, bool allowEncodingConversion, CancellationToken cancellationToken = default)
            => Task.FromResult("歌詞一行目\n歌詞二行目");

        public Task SaveLrcFileAsync(string destinationPath, string lrcContent, bool overwrite, CancellationToken cancellationToken = default)
        {
            if (SaveException is not null)
            {
                return Task.FromException(SaveException);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StubModelManager(bool available) : ILocalSpeechModelManager
    {
        public bool IsAvailable { get; set; } = available;
        public Func<IProgress<double>?, CancellationToken, Task<LocalSpeechModelInfo>>? DownloadHandler { get; set; }

        public LocalSpeechModelInfo GetActiveModelInfo()
            => new("base", "Whisper (base)", "ggml-base.bin", @"C:\models\ggml-base.bin", IsAvailable, 140_000_000);

        public IReadOnlyList<LocalSpeechModelInfo> GetSupportedModels() => [GetActiveModelInfo()];

        public void SelectModel(string modelId) { }

        public void SetCustomModelPath(string? modelFilePath)
        {
            IsAvailable = !string.IsNullOrWhiteSpace(modelFilePath);
        }

        public Task<LocalSpeechModelInfo> DownloadModelAsync(
            string? modelId = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (DownloadHandler is not null)
            {
                return DownloadHandler(progress, cancellationToken);
            }

            progress?.Report(1.0);
            IsAvailable = true;
            return Task.FromResult(GetActiveModelInfo());
        }
    }

    private sealed class StubLrcParser : ILrcParser
    {
        public LyricsDocument Parse(string content) => LyricsDocument.Empty;

        public string Format(LyricsDocument document) => "[00:04.00]formatted";
    }

    private sealed class StubLyricsProvider : ILyricsProvider
    {
        public Task<LyricsDocument?> LoadLyricsAsync(Track track, CancellationToken cancellationToken = default)
            => Task.FromResult<LyricsDocument?>(null);
    }

    private static Task RunInStaThreadAsync(Func<Task> action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));

            var frame = new System.Windows.Threading.DispatcherFrame();
            async void RunAsync()
            {
                try
                {
                    if (System.Windows.Application.Current == null)
                    {
                        _ = new System.Windows.Application
                        {
                            ShutdownMode = ShutdownMode.OnExplicitShutdown,
                        };
                    }

                    await action();
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    frame.Continue = false;
                }
            }

            try
            {
                dispatcher.BeginInvoke(new Action(RunAsync));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            finally
            {
                dispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static async Task RunWithHostWindowAsync(
        LrcGeneratorView view,
        Func<Window, Task> action,
        double width = 440,
        double height = 480)
    {
        var hostWindow = new Window
        {
            Content = view,
            Width = width,
            Height = height,
            Left = -32000,
            Top = -32000,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Opacity = 0,
            ShowInTaskbar = false,
            ShowActivated = false,
        };

        try
        {
            hostWindow.Show();
            hostWindow.UpdateLayout();
            await action(hostWindow);
        }
        finally
        {
            hostWindow.Close();
        }
    }

    private static void EnsureApplicationResources()
    {
        var appResources = System.Windows.Application.Current?.Resources;
        if (appResources == null)
        {
            return;
        }

        if (!appResources.Contains("BoolToVis"))
        {
            appResources["BoolToVis"] = new BoolToVisConverter();
        }

        if (!appResources.Contains("InvertBool"))
        {
            appResources["InvertBool"] = new InvertBoolConverter();
        }

        if (!appResources.Contains("NullToVis"))
        {
            appResources["NullToVis"] = new NullToVisConverter();
        }

        if (!appResources.Contains("NeonComboBoxStyle"))
        {
            appResources["NeonComboBoxStyle"] = new Style(typeof(ComboBox));
        }
    }

    private static void AttachThemeResources(ResourceDictionary target, string themeFileName)
    {
        target.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/AudioEffector;component/Presentation/Themes/{themeFileName}"),
        });
        target.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/AudioEffector;component/Presentation/Themes/ScrollBarResources.xaml"),
        });
        target["BoolToVis"] = new BoolToVisConverter();
        target["InvertBool"] = new InvertBoolConverter();
        target["NullToVis"] = new NullToVisConverter();
        target["NeonComboBoxStyle"] = new Style(typeof(ComboBox));
    }

    private static (LrcGeneratorViewModel ViewModel, StubAlignmentEngine Engine, StubTextEncodingService TextSvc, StubModelManager ModelMgr) CreateViewModel(
        bool modelAvailable,
        Func<string, bool>? fileExistsFunc = null)
    {
        var engine = new StubAlignmentEngine();
        var textSvc = new StubTextEncodingService();
        var modelMgr = new StubModelManager(modelAvailable);
        var eventBus = new InMemoryEventBus();
        var lyricsSvc = new LyricsApplicationService(new StubLyricsProvider(), eventBus);
        var genSvc = new LrcGenerationApplicationService(engine, textSvc, modelMgr, new StubLrcParser(), lyricsSvc);
        var audioMock = new Mock<IAudioService>();
        var vm = new LrcGeneratorViewModel(
            genSvc,
            audioMock.Object,
            new DispatcherUiDispatcherStub(System.Windows.Threading.Dispatcher.CurrentDispatcher),
            selectTextFileFunc: null,
            selectModelFileFunc: null,
            openFolderAction: _ => { },
            fileExistsFunc: fileExistsFunc ?? (_ => true));
        return (vm, engine, textSvc, modelMgr);
    }

    /// <summary>
    /// ダーク／ライト両テーマ・オフスクリーンホスト Window 上で全 6 排他状態・確認バー・行編集・保存エラーを描画し、
    /// バインディングエラー 0 件・UI 仮想化 4 属性・ScrollViewer ビューポート正常・高負荷エフェクト 0 件を検証します。
    /// </summary>
    [Theory]
    [InlineData("DarkTheme.xaml")]
    [InlineData("LightTheme.xaml")]
    public Task LrcGeneratorView_両テーマで全6状態および確認バーと行編集を描画_バインディングエラー0件と仮想化とビューポート正常を満たす(string themeFileName)
    {
        return RunInStaThreadAsync(async () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"ae_view_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            string audioPath = Path.Combine(tempDir, "ThemeTest.flac");
            string lrcPath = Path.Combine(tempDir, "ThemeTest.lrc");
            File.WriteAllText(audioPath, "dummy");
            File.WriteAllText(lrcPath, "[00:01.00]existing");

            try
            {
                EnsureApplicationResources();
                using var listener = new BindingErrorTraceListener();
                var (vm, engine, textSvc, modelMgr) = CreateViewModel(modelAvailable: false, fileExistsFunc: File.Exists);
                using (vm)
                {
                    vm.SetTargetTrack(new Track { FilePath = audioPath, Title = "ThemeTest", Artist = "Artist" });

                    var sut = new LrcGeneratorView { DataContext = vm };
                    AttachThemeResources(sut.Resources, themeFileName);

                    await RunWithHostWindowAsync(sut, async host =>
                    {
                        // 状態 1: モデル未配置ガイド
                        Assert.True(vm.IsModelMissingState);
                        host.UpdateLayout();

                        // 状態 2: モデルダウンロード中（キャンセル中テキスト切替含む）
                        var dlGate = new TaskCompletionSource<LocalSpeechModelInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
                        modelMgr.DownloadHandler = async (progress, ct) =>
                        {
                            progress?.Report(0.35);
                            using var reg = ct.Register(() => dlGate.TrySetCanceled(ct));
                            return await dlGate.Task;
                        };
                        Task dlTask = vm.DownloadDefaultModelAsync();
                        Assert.True(vm.IsModelDownloadingState);
                        host.UpdateLayout();
                        dlGate.TrySetResult(new LocalSpeechModelInfo("base", "Whisper (base)", "ggml-base.bin", @"C:\models\ggml-base.bin", true, 140_000_000));
                        modelMgr.IsAvailable = true;
                        await dlTask;
                        host.UpdateLayout();

                        // 状態 3: 入力・待機状態＋非 UTF-8 警告バー
                        Assert.True(vm.IsInputState);
                        await vm.InspectAndSelectTextFileAsync(@"C:\Lyrics\sjis.txt");
                        host.UpdateLayout();
                        Assert.True(vm.IsEncodingWarningVisible);

                        // 状態 4: 生成実行中（不定進捗フェーズ含む）
                        var genGate = new TaskCompletionSource<AlignmentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                        engine.Handler = async (req, progress, ct) =>
                        {
                            progress?.Report(new AlignmentProgress(AlignmentPhase.LoadingModel, 0.2, "モデル読み込み中"));
                            return await genGate.Task;
                        };
                        Task genTask = vm.TranscribeFromAudioAsync();
                        Assert.True(vm.IsGeneratingState);
                        Assert.True(vm.IsProgressIndeterminate);
                        host.UpdateLayout();

                        // 状態 6: エラー状態（失敗遷移）
                        genGate.TrySetException(new InvalidOperationException("テスト用エラー"));
                        await genTask;
                        Assert.True(vm.IsErrorState);
                        host.UpdateLayout();

                        // エラーから復帰 → 正常生成で状態 5: 結果プレビュー
                        vm.ReturnFromErrorCommand.Execute(null);
                        engine.Handler = null;
                        await vm.TranscribeFromAudioAsync();
                        Assert.True(vm.IsPreviewState);
                        host.UpdateLayout();

                        // プレビュー ListBox の仮想化 4 属性と ScrollViewer ビューポート検証
                        var listBox = Assert.IsType<ListBox>(sut.FindName("PreviewLinesListBox"));
                        Assert.True(VirtualizingPanel.GetIsVirtualizing(listBox));
                        Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(listBox));
                        Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(listBox));
                        Assert.True(ScrollViewer.GetCanContentScroll(listBox));

                        ScrollViewer? scrollViewer = FindVisualChild<ScrollViewer>(listBox);
                        Assert.NotNull(scrollViewer);
                        Assert.True(listBox.ActualHeight > 0);
                        Assert.True(scrollViewer!.ViewportHeight > 0);
                        Assert.True(scrollViewer.ViewportHeight >= listBox.ActualHeight * 0.5);

                        // 個別行インライン編集モード描画
                        vm.PreviewLines[0].BeginEditCommand.Execute(null);
                        host.UpdateLayout();
                        vm.PreviewLines[0].CancelEditCommand.Execute(null);

                        // 上書き確認バー描画
                        await vm.SaveLrcRequestedAsync();
                        Assert.True(vm.IsOverwriteConfirmVisible);
                        host.UpdateLayout();

                        // 保存エラーバナー描画
                        textSvc.SaveException = new IOException("保存エラー検証");
                        await vm.ExecuteSaveAsync(overwrite: true);
                        Assert.True(vm.HasSaveError);
                        host.UpdateLayout();

                        var saveErrorBanner = Assert.IsType<Border>(sut.FindName("SaveErrorBanner"));
                        Assert.Equal(Visibility.Visible, saveErrorBanner.Visibility);

                        // バインディングエラー 0 件・高負荷エフェクト 0 件
                        Assert.Empty(listener.Errors);
                        Assert.False(HasHeavyEffect(sut));
                    });
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        });
    }

    /// <summary>
    /// オフスクリーンホスト Window 上で Button.IsEnabled が ViewModel の状態遷移に連動して即時反映されることを検証します。
    /// </summary>
    [Fact]
    public Task LrcGeneratorView_状態遷移に応じたButtonIsEnabled実反映_欠損警告承諾処理中ガード保存後有効化を満たす()
    {
        return RunInStaThreadAsync(async () =>
        {
            EnsureApplicationResources();
            using var listener = new BindingErrorTraceListener();
            var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Music\Exists.flac" };
            var (vm, engine, _, _) = CreateViewModel(modelAvailable: true, fileExistsFunc: p => existingPaths.Contains(p));

            using (vm)
            {
                var sut = new LrcGeneratorView { DataContext = vm };
                AttachThemeResources(sut.Resources, "DarkTheme.xaml");

                await RunWithHostWindowAsync(sut, async host =>
                {
                    var generateFromTextBtn = Assert.IsType<Button>(sut.FindName("GenerateFromTextButton"));
                    var transcribeAudioBtn = Assert.IsType<Button>(sut.FindName("TranscribeAudioButton"));
                    var backToLyricsBtn = Assert.IsType<Button>(sut.FindName("BackToLyricsButton"));
                    var closeGeneratorBtn = Assert.IsType<Button>(sut.FindName("CloseGeneratorButton"));
                    var openSavedFolderBtn = Assert.IsType<Button>(sut.FindName("OpenSavedFolderButton"));
                    var missingBanner = Assert.IsType<Border>(sut.FindName("TargetAudioMissingBanner"));

                    // 1. 音声欠損時: 両生成ボタン非活性・欠損バナー表示
                    vm.SetTargetTrack(new Track { FilePath = @"C:\Music\Missing.flac", Title = "Missing" });
                    host.UpdateLayout();
                    Assert.Equal(Visibility.Visible, missingBanner.Visibility);
                    Assert.False(generateFromTextBtn.IsEnabled);
                    Assert.False(transcribeAudioBtn.IsEnabled);

                    // 2. 実在楽曲へ切替＋非 UTF-8 選択: テキスト生成は非活性、音声文字起こしは活性
                    vm.SetTargetTrack(new Track { FilePath = @"C:\Music\Exists.flac", Title = "Exists" });
                    await vm.InspectAndSelectTextFileAsync(@"C:\Lyrics\sjis.txt");
                    host.UpdateLayout();
                    Assert.Equal(Visibility.Collapsed, missingBanner.Visibility);
                    Assert.False(generateFromTextBtn.IsEnabled);
                    Assert.True(transcribeAudioBtn.IsEnabled);

                    // 3. 非 UTF-8 変換承諾: GenerateFromTextButton.IsEnabled が即時 true
                    vm.ConfirmEncodingConversionCommand.Execute(null);
                    host.UpdateLayout();
                    Assert.True(generateFromTextBtn.IsEnabled);

                    // 4. 生成実行中: 戻る・閉じるボタンが非活性
                    var gate = new TaskCompletionSource<AlignmentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                    engine.Handler = (_, _, _) => gate.Task;
                    Task runTask = vm.TranscribeFromAudioAsync();
                    host.UpdateLayout();
                    Assert.False(backToLyricsBtn.IsEnabled);
                    Assert.False(closeGeneratorBtn.IsEnabled);

                    // 5. 生成完了 → プレビュー状態: 戻る・閉じるは再活性、保存前はフォルダを開くが非活性
                    var doc = new LyricsDocument([new LyricLine(0, TimeSpan.FromSeconds(2), "Line1")], isSynced: true, title: "Exists");
                    gate.TrySetResult(new AlignmentResult(doc, "[00:02.00]Line1", [], LrcGenerationMode.AudioTranscription, "Stub"));
                    await runTask;
                    host.UpdateLayout();
                    Assert.True(backToLyricsBtn.IsEnabled);
                    Assert.True(closeGeneratorBtn.IsEnabled);
                    Assert.False(openSavedFolderBtn.IsEnabled);

                    // 6. 保存成功後: OpenSavedFolderButton.IsEnabled が即時 true
                    await vm.ExecuteSaveAsync(overwrite: true);
                    host.UpdateLayout();
                    Assert.True(openSavedFolderBtn.IsEnabled);
                    Assert.Empty(listener.Errors);
                });
            }
        });
    }

    /// <summary>
    /// 結果プレビュー状態のフッターに「歌詞表示で確認」ボタンが存在せず、
    /// 保存系フッターボタンおよびヘッダーの「← 歌詞へ戻る」ボタンが両テーマで正常動作することを検証します。
    /// </summary>
    [Theory]
    [InlineData("DarkTheme.xaml")]
    [InlineData("LightTheme.xaml")]
    public Task LrcGeneratorView_プレビュー状態描画時_フッターに歌詞表示で確認ボタンが存在せずヘッダー戻るボタンと保存系ボタンが維持される(string themeFileName)
    {
        return RunInStaThreadAsync(async () =>
        {
            // Arrange
            EnsureApplicationResources();
            using var listener = new BindingErrorTraceListener();
            var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Music\PreviewCheck.flac" };
            var (vm, engine, _, _) = CreateViewModel(modelAvailable: true, fileExistsFunc: p => existingPaths.Contains(p));

            using (vm)
            {
                vm.SetTargetTrack(new Track { FilePath = @"C:\Music\PreviewCheck.flac", Title = "PreviewCheck", Artist = "Artist" });
                var sut = new LrcGeneratorView { DataContext = vm };
                AttachThemeResources(sut.Resources, themeFileName);

                await RunWithHostWindowAsync(sut, async host =>
                {
                    var backToLyricsBtn = Assert.IsType<Button>(sut.FindName("BackToLyricsButton"));
                    var saveLrcBtn = Assert.IsType<Button>(sut.FindName("SaveLrcButton"));
                    var openSavedFolderBtn = Assert.IsType<Button>(sut.FindName("OpenSavedFolderButton"));

                    // Act: 処理中ガード → プレビュー状態へ遷移
                    var gate = new TaskCompletionSource<AlignmentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                    engine.Handler = (_, _, _) => gate.Task;
                    Task runTask = vm.TranscribeFromAudioAsync();
                    host.UpdateLayout();

                    // Assert: 生成処理中はヘッダーの「← 歌詞へ戻る」が非活性
                    Assert.False(backToLyricsBtn.IsEnabled);

                    var doc = new LyricsDocument([new LyricLine(0, TimeSpan.FromSeconds(3), "Line 1")], isSynced: true, title: "PreviewCheck");
                    gate.TrySetResult(new AlignmentResult(doc, "[00:03.00]Line 1", [], LrcGenerationMode.AudioTranscription, "Stub"));
                    await runTask;
                    host.UpdateLayout();

                    // Assert: フッターに「歌詞表示で確認」ボタンが存在しないこと
                    List<Button> allButtons = FindVisualChildren<Button>(sut);
                    Assert.DoesNotContain(allButtons, b => string.Equals(b.Content as string, "歌詞表示で確認", StringComparison.Ordinal));
                    Assert.DoesNotContain(allButtons, b => string.Equals(AutomationProperties.GetName(b), "同期歌詞表示パネルで確認", StringComparison.Ordinal));

                    // Assert: ヘッダーの「← 歌詞へ戻る」ボタンが維持・活性でイベント発火すること
                    bool backRequested = false;
                    vm.BackToLyricsRequested += () => backRequested = true;
                    Assert.Equal("← 歌詞へ戻る", backToLyricsBtn.Content);
                    Assert.True(backToLyricsBtn.IsEnabled);
                    backToLyricsBtn.Command.Execute(null);
                    Assert.True(backRequested);

                    // Assert: フッターの保存系ボタンが維持され、保存前→保存後で活性遷移すること
                    Assert.Equal("LRC を保存", saveLrcBtn.Content);
                    Assert.True(saveLrcBtn.IsEnabled);
                    Assert.Equal("保存先フォルダを開く", openSavedFolderBtn.Content);
                    Assert.False(openSavedFolderBtn.IsEnabled);

                    await vm.ExecuteSaveAsync(overwrite: true);
                    host.UpdateLayout();
                    Assert.True(openSavedFolderBtn.IsEnabled);

                    // Assert: バインディングエラー 0 件・高負荷エフェクト 0 件
                    Assert.Empty(listener.Errors);
                    Assert.False(HasHeavyEffect(sut));
                });
            }
        });
    }

    private static List<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var results = new List<T>();
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                results.Add(match);
            }

            results.AddRange(FindVisualChildren<T>(child));
        }

        return results;
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            T? found = FindVisualChild<T>(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool HasHeavyEffect(DependencyObject root)
    {
        if (root is UIElement element && (element.Effect is DropShadowEffect || element.Effect is BlurEffect))
        {
            return true;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
        {
            if (HasHeavyEffect(VisualTreeHelper.GetChild(root, i)))
            {
                return true;
            }
        }

        return false;
    }
}

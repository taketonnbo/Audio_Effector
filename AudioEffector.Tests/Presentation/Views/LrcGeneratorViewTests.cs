using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
/// <see cref="LrcGeneratorView"/> の XAML バインディングエラー 0 件・UI 仮想化・テーマ両対応・高負荷エフェクト非使用を検証するテストクラス
/// </summary>
public sealed class LrcGeneratorViewTests
{
    private sealed class ImmediateDispatcherStub : IUiDispatcher
    {
        public void Invoke(Action action) => action();

        public void BeginInvoke(Action action) => action();
    }

    private sealed class StubAlignmentEngine : IAudioAlignmentEngine
    {
        public string EngineName => "StubEngine";

        public Task<AlignmentResult> GenerateAsync(
            AlignmentRequest request,
            IProgress<AlignmentProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var doc = new LyricsDocument(
                [
                    new LyricLine(0, TimeSpan.FromSeconds(3), "プレビュー一行目"),
                    new LyricLine(1, TimeSpan.FromSeconds(8), "プレビュー二行目"),
                ],
                isSynced: true,
                title: request.Title);
            return Task.FromResult(new AlignmentResult(
                doc,
                "[00:03.00]プレビュー一行目\r\n[00:08.00]プレビュー二行目",
                Array.Empty<AlignmentSegment>(),
                request.Mode,
                EngineName));
        }
    }

    private sealed class StubTextEncodingService : ITextEncodingService
    {
        public Task<TextEncodingInspectionResult> InspectAsync(string textFilePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new TextEncodingInspectionResult(textFilePath, false, false, "Shift-JIS (CP932)", 2, 2));

        public Task<string> ReadAsUtf8TextAsync(string textFilePath, bool allowEncodingConversion, CancellationToken cancellationToken = default)
            => Task.FromResult("歌詞一行目\n歌詞二行目");

        public Task SaveLrcFileAsync(string destinationPath, string lrcContent, bool overwrite, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubModelManager(bool available) : ILocalSpeechModelManager
    {
        public LocalSpeechModelInfo GetActiveModelInfo()
            => new("base", "Whisper (base)", "ggml-base.bin", @"C:\models\ggml-base.bin", available, 140_000_000);

        public IReadOnlyList<LocalSpeechModelInfo> GetSupportedModels() => [GetActiveModelInfo()];
        public void SelectModel(string modelId) { }
        public void SetCustomModelPath(string? modelFilePath) { }
        public Task<LocalSpeechModelInfo> DownloadModelAsync(string? modelId = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(GetActiveModelInfo());
    }

    private sealed class StubLrcParser : ILrcParser
    {
        public LyricsDocument Parse(string content) => LyricsDocument.Empty;
        public string Format(LyricsDocument document) => "[00:03.00]formatted";
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

    private static LrcGeneratorViewModel CreateViewModel(bool modelAvailable)
    {
        var eventBus = new InMemoryEventBus();
        var lyricsSvc = new LyricsApplicationService(new StubLyricsProvider(), eventBus);
        var genSvc = new LrcGenerationApplicationService(
            new StubAlignmentEngine(),
            new StubTextEncodingService(),
            new StubModelManager(modelAvailable),
            new StubLrcParser(),
            lyricsSvc);
        var audioMock = new Mock<IAudioService>();
        return new LrcGeneratorViewModel(genSvc, audioMock.Object, new ImmediateDispatcherStub());
    }

    /// <summary>
    /// ダーク／ライト両テーマで LrcGeneratorView を実体化し、入力・警告・プレビューの各状態でバインディングエラー 0 件・UI 仮想化 4 属性・高負荷エフェクト 0 件であることを検証します。
    /// </summary>
    [Theory]
    [InlineData("DarkTheme.xaml")]
    [InlineData("LightTheme.xaml")]
    public Task LrcGeneratorView_両テーマで入力およびプレビュー描画_バインディングエラー0件と仮想化と軽量描画を満たす(string themeFileName)
    {
        return RunInStaThreadAsync(async () =>
        {
            // Arrange
            EnsureApplicationResources();
            using var listener = new BindingErrorTraceListener();
            using var vm = CreateViewModel(modelAvailable: true);
            vm.SetTargetTrack(new Track { FilePath = @"C:\Music\ThemeTest.flac", Title = "ThemeTest", Artist = "Artist" });

            var sut = new LrcGeneratorView { DataContext = vm };
            AttachThemeResources(sut.Resources, themeFileName);

            // Act: 入力状態＋非UTF-8警告状態を描画
            await vm.InspectAndSelectTextFileAsync(@"C:\Lyrics\sjis.txt");
            sut.Measure(new Size(420, 480));
            sut.Arrange(new Rect(0, 0, 420, 480));
            sut.UpdateLayout();

            // Act: プレビュー状態を描画
            await vm.TranscribeFromAudioAsync();
            sut.Measure(new Size(420, 480));
            sut.Arrange(new Rect(0, 0, 420, 480));
            sut.UpdateLayout();

            var listBox = sut.FindName("PreviewLinesListBox") as ListBox;

            // Assert
            Assert.Empty(listener.Errors);
            Assert.NotNull(listBox);
            Assert.True(VirtualizingPanel.GetIsVirtualizing(listBox!));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(listBox));
            Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(listBox));
            Assert.True(ScrollViewer.GetCanContentScroll(listBox));
            Assert.False(HasHeavyEffect(sut));
        });
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

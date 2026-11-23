using System;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.ViewModels;
using AudioEffector.Presentation.Views;
using Xunit;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="WindowShortcutHandler"/> のメディアキー・カスタムショートカット発火、二重発火防止およびフォーカスガードを検証するテストクラス
/// </summary>
public sealed class WindowShortcutHandlerTests
{
    private sealed class FakeShortcutCommandTarget : IShortcutCommandTarget
    {
        public bool IsPlaying { get; set; }
        public int TogglePlayPauseCount { get; private set; }
        public int StopCount { get; private set; }
        public int NextCount { get; private set; }
        public int PreviousCount { get; private set; }
        public int ToggleMuteCount { get; private set; }
        public int IncreaseVolumeCount { get; private set; }
        public int DecreaseVolumeCount { get; private set; }

        public ICommand TogglePlayPauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PreviousCommand { get; }
        public ICommand ToggleMuteCommand { get; }
        public ICommand IncreaseVolumeCommand { get; }
        public ICommand DecreaseVolumeCommand { get; }

        public FakeShortcutCommandTarget()
        {
            TogglePlayPauseCommand = new RelayCommand(_ =>
            {
                TogglePlayPauseCount++;
                IsPlaying = !IsPlaying;
            });
            StopCommand = new RelayCommand(_ => StopCount++);
            NextCommand = new RelayCommand(_ => NextCount++);
            PreviousCommand = new RelayCommand(_ => PreviousCount++);
            ToggleMuteCommand = new RelayCommand(_ => ToggleMuteCount++);
            IncreaseVolumeCommand = new RelayCommand(_ => IncreaseVolumeCount++);
            DecreaseVolumeCommand = new RelayCommand(_ => DecreaseVolumeCount++);
        }
    }

    private static void RunInStaThread(Action action)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            throw threadEx;
        }
    }

    /// <summary>
    /// カスタムショートカット未設定時でも、WM_APPCOMMAND の各メディアコマンドが対応する再生・音量コマンドを実行することを検証します。
    /// </summary>
    [Fact]
    public void HandleAppCommandCode_各種メディアコマンド受信時_対応するコマンドを実行すること()
    {
        // Arrange
        long nowMs = 1000;
        var sut = new WindowShortcutHandler(() => nowMs, 1.0);
        var target = new FakeShortcutCommandTarget();

        // Act
        Assert.True(sut.HandleAppCommandCode(ShortcutKeyHelper.APPCOMMAND_MEDIA_PLAY_PAUSE, target));
        Assert.True(sut.HandleAppCommandCode(ShortcutKeyHelper.APPCOMMAND_MEDIA_STOP, target));
        Assert.True(sut.HandleAppCommandCode(ShortcutKeyHelper.APPCOMMAND_MEDIA_NEXTTRACK, target));
        Assert.True(sut.HandleAppCommandCode(ShortcutKeyHelper.APPCOMMAND_MEDIA_PREVIOUSTRACK, target));
        Assert.True(sut.HandleAppCommandCode(ShortcutKeyHelper.APPCOMMAND_VOLUME_MUTE, target));
        Assert.True(sut.HandleAppCommandCode(ShortcutKeyHelper.APPCOMMAND_VOLUME_UP, target));
        Assert.True(sut.HandleAppCommandCode(ShortcutKeyHelper.APPCOMMAND_VOLUME_DOWN, target));

        // Assert
        Assert.Equal(1, target.TogglePlayPauseCount);
        Assert.Equal(1, target.StopCount);
        Assert.Equal(1, target.NextCount);
        Assert.Equal(1, target.PreviousCount);
        Assert.Equal(1, target.ToggleMuteCount);
        Assert.Equal(1, target.IncreaseVolumeCount);
        Assert.Equal(1, target.DecreaseVolumeCount);
    }

    /// <summary>
    /// APPCOMMAND_MEDIA_PLAY および APPCOMMAND_MEDIA_PAUSE が現在の再生状態に応じて必要な場合のみトグルを実行することを検証します。
    /// </summary>
    [Fact]
    public void HandleAppCommandCode_再生専用および一時停止専用コマンド受信時_再生状態に応じて正しく制御すること()
    {
        // Arrange
        long nowMs = 1000;
        var sut = new WindowShortcutHandler(() => nowMs, 1.0);
        var target = new FakeShortcutCommandTarget { IsPlaying = false };

        // Act & Assert (停止中に PAUSE -> 実行しない)
        Assert.True(sut.HandleAppCommandCode(ShortcutKeyHelper.APPCOMMAND_MEDIA_PAUSE, target));
        Assert.Equal(0, target.TogglePlayPauseCount);

        // Act & Assert (停止中に PLAY -> 再生開始)
        nowMs += 300;
        Assert.True(sut.HandleAppCommandCode(ShortcutKeyHelper.APPCOMMAND_MEDIA_PLAY, target));
        Assert.Equal(1, target.TogglePlayPauseCount);
        Assert.True(target.IsPlaying);

        // Act & Assert (再生中に PLAY -> 重複トグルしない)
        nowMs += 300;
        Assert.True(sut.HandleAppCommandCode(ShortcutKeyHelper.APPCOMMAND_MEDIA_PLAY, target));
        Assert.Equal(1, target.TogglePlayPauseCount);
        Assert.True(target.IsPlaying);

        // Act & Assert (再生中に PAUSE -> 一時停止)
        nowMs += 300;
        Assert.True(sut.HandleAppCommandCode(ShortcutKeyHelper.APPCOMMAND_MEDIA_PAUSE, target));
        Assert.Equal(2, target.TogglePlayPauseCount);
        Assert.False(target.IsPlaying);
    }

    /// <summary>
    /// 同一メディアキーが WM_KEYDOWN と WM_APPCOMMAND の両方から短時間内に連続送出された場合、二重発火が抑止されることを検証します。
    /// </summary>
    [Fact]
    public void HandleAppCommandCode_KeyDown直後に同一メディアキーのAppCommandを受信した場合_二重発火を抑止すること()
    {
        // Arrange
        long nowMs = 1000;
        var sut = new WindowShortcutHandler(() => nowMs, 1.0);
        var target = new FakeShortcutCommandTarget();

        // Act
        bool firstHandled = sut.HandleKeyInput(Key.MediaPlayPause, ModifierKeys.None, false, target, null);
        nowMs += 30; // 150ms 以内に WM_APPCOMMAND が続けて届く
        bool secondHandled = sut.HandleAppCommandCode(ShortcutKeyHelper.APPCOMMAND_MEDIA_PLAY_PAUSE, target);

        // Assert
        Assert.True(firstHandled);
        Assert.True(secondHandled);
        Assert.Equal(1, target.TogglePlayPauseCount);
        Assert.True(target.IsPlaying);
    }

    /// <summary>
    /// ユーザーがメディアキーを別機能（停止）へ明示的に割り当てている場合、標準動作よりユーザー設定が優先されることを検証します。
    /// </summary>
    [Fact]
    public void HandleAppCommandCode_メディアキーが別機能へ割り当てられている場合_ユーザー設定を優先すること()
    {
        // Arrange
        long nowMs = 1000;
        var sut = new WindowShortcutHandler(() => nowMs, 1.0);
        sut.UpdateSettings(new AppSettings
        {
            StopShortcut = new ShortcutKeyConfig { Key = Key.MediaPlayPause, Modifiers = ModifierKeys.None }
        });
        var target = new FakeShortcutCommandTarget();

        // Act
        bool handled = sut.HandleAppCommandCode(ShortcutKeyHelper.APPCOMMAND_MEDIA_PLAY_PAUSE, target);

        // Assert
        Assert.True(handled);
        Assert.Equal(1, target.StopCount);
        Assert.Equal(0, target.TogglePlayPauseCount);
    }

    /// <summary>
    /// 修飾キーなしの単一キー（Space・P）がボタンフォーカス中でも発火し、編集可能な TextBox フォーカス時はスキップされつつメディアキーは有効であることを検証します。
    /// </summary>
    [Fact]
    public void HandleKeyInput_カスタム単一キーとTextBoxフォーカス時_適切に発火およびガードされること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            long nowMs = 1000;
            var sut = new WindowShortcutHandler(() => nowMs, 1.0);
            sut.UpdateSettings(new AppSettings
            {
                PlayPauseShortcut = new ShortcutKeyConfig { Key = Key.Space, Modifiers = ModifierKeys.None },
                MuteShortcut = new ShortcutKeyConfig { Key = Key.P, Modifiers = ModifierKeys.None }
            });
            var target = new FakeShortcutCommandTarget();
            var button = new Button();
            var textBox = new TextBox();

            // Act
            bool spaceOnButton = sut.HandleKeyInput(Key.Space, ModifierKeys.None, false, target, button);
            bool pOnButton = sut.HandleKeyInput(Key.P, ModifierKeys.None, false, target, button);
            bool spaceOnTextBox = sut.HandleKeyInput(Key.Space, ModifierKeys.None, false, target, textBox);
            bool pOnTextBox = sut.HandleKeyInput(Key.P, ModifierKeys.None, false, target, textBox);
            bool mediaOnTextBox = sut.HandleKeyInput(Key.MediaPlayPause, ModifierKeys.None, false, target, textBox);

            // Assert
            Assert.True(spaceOnButton);
            Assert.True(pOnButton);
            Assert.False(spaceOnTextBox);
            Assert.False(pOnTextBox);
            Assert.True(mediaOnTextBox);
            Assert.Equal(2, target.TogglePlayPauseCount);
            Assert.Equal(1, target.ToggleMuteCount);
        });
    }
}

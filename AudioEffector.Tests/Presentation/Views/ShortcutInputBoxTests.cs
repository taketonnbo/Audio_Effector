using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.Views;
using Xunit;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="ShortcutInputBox"/> のメディアキー・単一キー・IME処理キーおよび WM_APPCOMMAND 入力割り当てを検証するテストクラス
/// </summary>
public sealed class ShortcutInputBoxTests
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

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
    /// 修飾キーなしの単一アルファベットキー（P）およびハードウェアメディアキー（MediaPlayPause）をエラーなく割り当てられることを検証します。
    /// </summary>
    [Fact]
    public void TryAssignShortcut_単一アルファベットキーとメディアキーを指定した場合_正しく割り当てられること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var sut = new ShortcutInputBox();

            // Act
            bool assignedSingle = sut.TryAssignShortcut(Key.P, ModifierKeys.None);
            var singleResult = sut.Shortcut;

            bool assignedMedia = sut.TryAssignShortcut(Key.MediaPlayPause, ModifierKeys.None);
            var mediaResult = sut.Shortcut;

            // Assert
            Assert.True(assignedSingle);
            Assert.NotNull(singleResult);
            Assert.Equal(Key.P, singleResult.Key);
            Assert.Equal(ModifierKeys.None, singleResult.Modifiers);

            Assert.True(assignedMedia);
            Assert.NotNull(mediaResult);
            Assert.Equal(Key.MediaPlayPause, mediaResult.Key);
            Assert.Equal(ModifierKeys.None, mediaResult.Modifiers);
        });
    }

    /// <summary>
    /// 修飾キー単体や IME モード切替キーを渡した場合、誤って割り当てられないことを検証します。
    /// </summary>
    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.ImeConvert)]
    [InlineData(Key.KanaMode)]
    [InlineData(Key.CapsLock)]
    public void TryAssignShortcut_無視対象キーを指定した場合_割り当てを拒否すること(Key ignoredKey)
    {
        RunInStaThread(() =>
        {
            // Arrange
            var sut = new ShortcutInputBox();

            // Act
            bool assigned = sut.TryAssignShortcut(ignoredKey, ModifierKeys.None);

            // Assert
            Assert.False(assigned);
            Assert.Null(sut.Shortcut);
        });
    }

    /// <summary>
    /// InputTextBox がフォーカスを持つ状態で WM_APPCOMMAND を受信した場合、対応するメディアキーが割り当てられることを検証します。
    /// </summary>
    [Fact]
    public void WndProc_フォーカス中にWM_APPCOMMANDを受信した場合_対応するメディアキーが割り当てられること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var sut = new ShortcutInputBox();
            var hostWindow = new Window
            {
                Content = sut,
                Width = 320,
                Height = 120,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -2000,
                Top = -2000,
                ShowInTaskbar = false
            };

            try
            {
                hostWindow.Show();
                hostWindow.Activate();

                var inputBox = Assert.IsType<TextBox>(sut.FindName("InputTextBox"));
                Keyboard.Focus(inputBox);

                IntPtr hwnd = new WindowInteropHelper(hostWindow).Handle;
                IntPtr lParam = new IntPtr(ShortcutKeyHelper.APPCOMMAND_MEDIA_PLAY_PAUSE << 16);

                // Act
                SendMessage(hwnd, ShortcutKeyHelper.WM_APPCOMMAND, hwnd, lParam);

                // Assert
                Assert.NotNull(sut.Shortcut);
                Assert.Equal(Key.MediaPlayPause, sut.Shortcut.Key);
                Assert.Equal(ModifierKeys.None, sut.Shortcut.Modifiers);
                Assert.Equal("MediaPlayPause (再生/一時停止)", inputBox.Text);
            }
            finally
            {
                hostWindow.Close();
            }
        });
    }

    /// <summary>
    /// WM_APPCOMMAND の直後に同一メディアキーの PreviewKeyDown が逆順で着信した場合でも、二重割り当てが発生せず 1 回のみ設定されることを検証します。
    /// </summary>
    [Fact]
    public void InputTextBox_PreviewKeyDown_WM_APPCOMMAND直後に同一メディアキーが着信した場合_二重処理を抑止すること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var sut = new ShortcutInputBox();
            var hostWindow = new Window
            {
                Content = sut,
                Width = 320,
                Height = 120,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -2000,
                Top = -2000,
                ShowInTaskbar = false
            };

            try
            {
                hostWindow.Show();
                hostWindow.Activate();

                var inputBox = Assert.IsType<TextBox>(sut.FindName("InputTextBox"));
                Keyboard.Focus(inputBox);

                IntPtr hwnd = new WindowInteropHelper(hostWindow).Handle;
                IntPtr lParam = new IntPtr(ShortcutKeyHelper.APPCOMMAND_MEDIA_PLAY_PAUSE << 16);
                var presentationSource = PresentationSource.FromVisual(hostWindow);
                Assert.NotNull(presentationSource);

                // Act (WM_APPCOMMAND → 直後に PreviewKeyDown の逆順着信)
                SendMessage(hwnd, ShortcutKeyHelper.WM_APPCOMMAND, hwnd, lParam);
                var firstAssignedInstance = sut.Shortcut;

                var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, presentationSource!, 0, Key.MediaPlayPause)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                };
                inputBox.RaiseEvent(keyArgs);

                // Assert
                Assert.True(keyArgs.Handled);
                Assert.NotNull(firstAssignedInstance);
                Assert.Same(firstAssignedInstance, sut.Shortcut);
            }
            finally
            {
                hostWindow.Close();
            }
        });
    }
}

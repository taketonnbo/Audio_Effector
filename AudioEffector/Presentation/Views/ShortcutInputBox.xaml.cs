using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// ショートカットキー入力用のユーザーコントロール
/// </summary>
public partial class ShortcutInputBox : UserControl
{
    private const double MediaKeyDuplicateGuardMs = 150.0;

    private enum MediaKeyCaptureSource
    {
        None,
        KeyDown,
        AppCommand
    }

    private HwndSource? _hwndSource;
    private Key _lastCapturedMediaKey = Key.None;
    private MediaKeyCaptureSource _lastCapturedMediaKeySource = MediaKeyCaptureSource.None;
    private long _lastCapturedMediaKeyTimestamp;

    /// <summary>
    /// ショートカットキー設定を表す依存関係プロパティ
    /// </summary>
    public static readonly DependencyProperty ShortcutProperty =
        DependencyProperty.Register("Shortcut", typeof(ShortcutKeyConfig), typeof(ShortcutInputBox), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnShortcutChanged));

    /// <summary>
    /// ショートカットキー設定を取得または設定します
    /// </summary>
    public ShortcutKeyConfig Shortcut
    {
        get { return (ShortcutKeyConfig)GetValue(ShortcutProperty); }
        set { SetValue(ShortcutProperty, value); }
    }

    /// <summary>
    /// コントロールの表示ラベルを表す依存関係プロパティ
    /// </summary>
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register("Label", typeof(string), typeof(ShortcutInputBox), new PropertyMetadata(string.Empty));

    /// <summary>
    /// コントロールの表示ラベルを取得または設定します
    /// </summary>
    public string Label
    {
        get { return (string)GetValue(LabelProperty); }
        set { SetValue(LabelProperty, value); }
    }

    /// <summary>
    /// インスタンスを初期化します
    /// </summary>
    public ShortcutInputBox()
    {
        InitializeComponent();
        this.Unloaded += ShortcutInputBox_Unloaded;
    }

    private static void OnShortcutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ShortcutInputBox)d;
        control.UpdateDisplayText();
    }

    private void ShortcutInputBox_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachAppCommandHook();
    }

    private void InputTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        InputTextBox.Focus();
        e.Handled = true;
    }

    private void InputTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        InputTextBox.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 0, 120, 215));
        InputTextBox.Text = "キーを入力してください...";
        AttachAppCommandHook();
    }

    private void InputTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        DetachAppCommandHook();
        InputTextBox.Background = System.Windows.Media.Brushes.Transparent;
        UpdateDisplayText();
    }

    private void AttachAppCommandHook()
    {
        DetachAppCommandHook();

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            _hwndSource = source;
            _hwndSource.AddHook(WndProc);
        }
    }

    private void DetachAppCommandHook()
    {
        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != ShortcutKeyHelper.WM_APPCOMMAND || !InputTextBox.IsKeyboardFocused)
        {
            return IntPtr.Zero;
        }

        int appCommand = ShortcutKeyHelper.GetAppCommand(lParam);
        if (!ShortcutKeyHelper.TryMapAppCommandToKey(appCommand, out Key mediaKey))
        {
            return IntPtr.Zero;
        }

        handled = true;

        // 直前に PreviewKeyDown 経由で同じメディアキーを処理済みの場合は二重処理を抑止する
        if (IsCrossSourceRecentMediaKeyCapture(mediaKey, MediaKeyCaptureSource.AppCommand))
        {
            return new IntPtr(1);
        }

        RecordMediaKeyCapture(mediaKey, MediaKeyCaptureSource.AppCommand);
        TryAssignShortcut(mediaKey, Keyboard.Modifiers);
        return new IntPtr(1);
    }

    private void UpdateDisplayText()
    {
        InputTextBox.Text = ShortcutKeyHelper.FormatShortcutText(Shortcut);
    }

    private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = ShortcutKeyHelper.ResolveKey(e);

        // 修飾キー単体やIMEモード切替キーは無視する
        if (ShortcutKeyHelper.IsIgnoredKey(key))
        {
            e.Handled = true;
            return;
        }

        // Delete または BackSpace で割り当てをクリアする
        if (key == Key.Delete || key == Key.Back)
        {
            Shortcut = ShortcutKeyHelper.CreateConfig(Key.None, ModifierKeys.None);
            WarningTextBlock.Visibility = Visibility.Collapsed;
            UpdateDisplayText();
            e.Handled = true;
            return;
        }

        // 直前に WM_APPCOMMAND 経由で同じメディアキーを処理済みの場合は二重処理を抑止する
        if (ShortcutKeyHelper.IsMediaKey(key))
        {
            if (IsCrossSourceRecentMediaKeyCapture(key, MediaKeyCaptureSource.KeyDown))
            {
                e.Handled = true;
                return;
            }

            RecordMediaKeyCapture(key, MediaKeyCaptureSource.KeyDown);
        }

        var modifiers = Keyboard.Modifiers;
        TryAssignShortcut(key, modifiers);
        e.Handled = true;
    }

    /// <summary>
    /// 指定されたキーと修飾キーの組み合わせを検証し、問題なければショートカットとして割り当てます
    /// </summary>
    /// <param name="key">割り当てる実キー</param>
    /// <param name="modifiers">修飾キー</param>
    /// <returns>割り当てに成功した場合は <c>true</c></returns>
    public bool TryAssignShortcut(Key key, ModifierKeys modifiers)
    {
        if (ShortcutKeyHelper.IsIgnoredKey(key))
        {
            return false;
        }

        // アプリ内他機能との重複チェック
        bool isInternalDuplicate = false;
        if (this.DataContext != null)
        {
            var properties = this.DataContext.GetType().GetProperties()
                .Where(p => p.PropertyType == typeof(ShortcutKeyConfig));
            foreach (var prop in properties)
            {
                var sc = prop.GetValue(this.DataContext) as ShortcutKeyConfig;
                if (sc != null && ShortcutKeyHelper.ToWpfKey(sc.Key) == key && ShortcutKeyHelper.ToWpfModifiers(sc.Modifiers) == modifiers)
                {
                    if (!ReferenceEquals(sc, Shortcut))
                    {
                        isInternalDuplicate = true;
                        break;
                    }
                }
            }
        }

        if (isInternalDuplicate)
        {
            System.Windows.MessageBox.Show(
                "このショートカットは既に別の機能に割り当てられています。\n重複するショートカットは設定できません。",
                "ショートカットの重複エラー",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        // ハードウェアメディアキーはOSや他メディアアプリがグローバル利用しているケースが多いため、
        // アプリ内ショートカット設定時は RegisterHotKey 競合でブロックしない
        if (!ShortcutKeyHelper.IsMediaKey(key))
        {
            try
            {
                if (IsShortcutInUse(key, modifiers))
                {
                    System.Windows.MessageBox.Show(
                        "このショートカットは他のアプリまたはシステムで既に使われているため、正しく動作しない可能性があります。\n別のキーの組み合わせをお試しください。",
                        "ショートカットの競合警告",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);

                    WarningTextBlock.Text = "⚠️ このショートカットは他のアプリで既に使われています。";
                    WarningTextBlock.Visibility = System.Windows.Visibility.Visible;

                    return false;
                }
                else
                {
                    WarningTextBlock.Visibility = System.Windows.Visibility.Collapsed;
                }
            }
            catch
            {
                // Ignore PInvoke exceptions
                WarningTextBlock.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            WarningTextBlock.Visibility = Visibility.Collapsed;
        }

        // ViewModelのsetterを確実に発火させるため新しいインスタンスを生成する
        var newConfig = ShortcutKeyHelper.CreateConfig(key, modifiers);
        Shortcut = newConfig;

        // バインディング通知が発生しないケースに備えて表示テキストを強制更新する
        UpdateDisplayText();
        return true;
    }

    private bool IsCrossSourceRecentMediaKeyCapture(Key mediaKey, MediaKeyCaptureSource currentSource)
    {
        if (_lastCapturedMediaKey != mediaKey ||
            _lastCapturedMediaKeySource == MediaKeyCaptureSource.None ||
            _lastCapturedMediaKeySource == currentSource)
        {
            return false;
        }

        double elapsedMs = (Stopwatch.GetTimestamp() - _lastCapturedMediaKeyTimestamp) * 1000.0 / Stopwatch.Frequency;
        return elapsedMs >= 0 && elapsedMs <= MediaKeyDuplicateGuardMs;
    }

    private void RecordMediaKeyCapture(Key mediaKey, MediaKeyCaptureSource source)
    {
        _lastCapturedMediaKey = mediaKey;
        _lastCapturedMediaKeySource = source;
        _lastCapturedMediaKeyTimestamp = Stopwatch.GetTimestamp();
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private bool IsShortcutInUse(Key key, ModifierKeys modifiers)
    {
        if (key == Key.None) return false;

        uint fsModifiers = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) fsModifiers |= 0x0001;
        if (modifiers.HasFlag(ModifierKeys.Control)) fsModifiers |= 0x0002;
        if (modifiers.HasFlag(ModifierKeys.Shift)) fsModifiers |= 0x0004;
        if (modifiers.HasFlag(ModifierKeys.Windows)) fsModifiers |= 0x0008;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        int id = 9999; // Random ID

        // If we can register it, it's NOT in use globally.
        bool success = RegisterHotKey(IntPtr.Zero, id, fsModifiers, vk);
        if (success)
        {
            UnregisterHotKey(IntPtr.Zero, id);
            return false;
        }

        return true; // Register failed -> in use
    }
}

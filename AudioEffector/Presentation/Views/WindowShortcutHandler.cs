using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// ウィンドウ単位のキーボードショートカットおよびハードウェアメディアキー（<c>WM_KEYDOWN</c> / <c>WM_APPCOMMAND</c>）のディスパッチを管理するクラス
/// </summary>
public sealed class WindowShortcutHandler
{
    private const double DuplicateGuardWindowMs = 150.0;

    private enum MediaKeySource
    {
        None,
        KeyDown,
        AppCommand
    }

    private readonly Func<long> _timestampProvider;
    private readonly double _ticksPerMillisecond;
    private AppSettings _settings = new();
    private Key _lastMediaKey = Key.None;
    private MediaKeySource _lastMediaKeySource = MediaKeySource.None;
    private long _lastMediaKeyTimestamp;

    /// <summary>
    /// インスタンスを初期化します
    /// </summary>
    public WindowShortcutHandler()
        : this(Stopwatch.GetTimestamp, Stopwatch.Frequency / 1000.0)
    {
    }

    /// <summary>
    /// タイムスタンプ取得関数を指定してインスタンスを初期化します
    /// </summary>
    /// <param name="timestampProvider">現在時刻ティックを返す関数</param>
    /// <param name="ticksPerMillisecond">1ミリ秒あたりのティック数</param>
    public WindowShortcutHandler(Func<long> timestampProvider, double ticksPerMillisecond)
    {
        _timestampProvider = timestampProvider ?? throw new ArgumentNullException(nameof(timestampProvider));
        _ticksPerMillisecond = ticksPerMillisecond > 0 ? ticksPerMillisecond : 1.0;
    }

    /// <summary>
    /// 現在のショートカット設定を更新します
    /// </summary>
    /// <param name="settings">アプリケーション設定</param>
    public void UpdateSettings(AppSettings? settings)
    {
        _settings = settings ?? new AppSettings();
    }

    /// <summary>
    /// <c>PreviewKeyDown</c> イベントを処理し、該当するショートカットまたは標準メディアキーのコマンドを実行します
    /// </summary>
    /// <param name="e">キーイベント引数</param>
    /// <param name="viewModel">対象となるメインViewModel</param>
    /// <param name="focusedElement">現在フォーカスを持つUI要素</param>
    /// <returns>コマンドを実行または抑止処理した場合は <c>true</c></returns>
    public bool HandlePreviewKeyDown(KeyEventArgs e, IShortcutCommandTarget? viewModel, IInputElement? focusedElement)
    {
        ArgumentNullException.ThrowIfNull(e);

        Key resolvedKey = ShortcutKeyHelper.ResolveKey(e);
        bool handled = HandleKeyInput(resolvedKey, Keyboard.Modifiers, e.IsRepeat, viewModel, focusedElement);
        if (handled)
        {
            e.Handled = true;
        }

        return handled;
    }

    /// <summary>
    /// 解決済みキー入力からカスタムショートカットまたは標準メディアキーを判定して実行します
    /// </summary>
    /// <param name="key">解決済みのキー</param>
    /// <param name="modifiers">修飾キー</param>
    /// <param name="isRepeat">キーリピートかどうか</param>
    /// <param name="viewModel">対象となるコマンド実行先</param>
    /// <param name="focusedElement">現在フォーカスを持つUI要素</param>
    /// <returns>入力を処理した場合は <c>true</c></returns>
    public bool HandleKeyInput(Key key, ModifierKeys modifiers, bool isRepeat, IShortcutCommandTarget? viewModel, IInputElement? focusedElement)
    {
        if (viewModel == null || ShortcutKeyHelper.IsIgnoredKey(key))
        {
            return false;
        }

        if (ShortcutKeyHelper.ShouldSkipForTextInput(key, modifiers, focusedElement))
        {
            return false;
        }

        bool isMediaKey = ShortcutKeyHelper.IsMediaKey(key);

        // 1. ユーザー設定のカスタムショートカットを最優先で判定する
        if (TryResolveCustomCommand(key, modifiers, viewModel, out var customCommand, out bool allowRepeat))
        {
            if (isMediaKey && IsCrossSourceDuplicate(key, MediaKeySource.KeyDown))
            {
                return true;
            }

            if (isRepeat && !allowRepeat)
            {
                return true;
            }

            ExecuteCommand(customCommand);
            if (isMediaKey)
            {
                RecordMediaKey(key, MediaKeySource.KeyDown);
            }

            return true;
        }

        // 2. 修飾キーなしのハードウェアメディアキーは標準動作へフォールバックする
        if (modifiers == ModifierKeys.None && isMediaKey)
        {
            if (IsCrossSourceDuplicate(key, MediaKeySource.KeyDown))
            {
                return true;
            }

            if (isRepeat)
            {
                return true;
            }

            if (TryExecuteDefaultMediaKey(key, viewModel))
            {
                RecordMediaKey(key, MediaKeySource.KeyDown);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Win32 <c>WM_APPCOMMAND</c> メッセージを処理し、該当するメディアコマンドを実行します
    /// </summary>
    /// <param name="lParam">Win32 メッセージの lParam</param>
    /// <param name="viewModel">対象となるメインViewModel</param>
    /// <returns>コマンドを処理した場合は <c>true</c></returns>
    public bool HandleAppCommand(IntPtr lParam, IShortcutCommandTarget? viewModel)
    {
        int appCommand = ShortcutKeyHelper.GetAppCommand(lParam);
        return HandleAppCommandCode(appCommand, viewModel, Keyboard.Modifiers);
    }

    /// <summary>
    /// アプリケーションコマンド番号を処理し、該当するメディアコマンドを実行します
    /// </summary>
    /// <param name="appCommand">アプリケーションコマンド番号</param>
    /// <param name="viewModel">対象となるコマンド実行先</param>
    /// <param name="modifiers">押下中の修飾キー</param>
    /// <returns>コマンドを処理した場合は <c>true</c></returns>
    public bool HandleAppCommandCode(int appCommand, IShortcutCommandTarget? viewModel, ModifierKeys modifiers = ModifierKeys.None)
    {
        if (viewModel == null)
        {
            return false;
        }

        if (!ShortcutKeyHelper.TryMapAppCommandToKey(appCommand, out Key mediaKey))
        {
            return false;
        }

        // WM_KEYDOWN と WM_APPCOMMAND の双方を送出するドライバ環境での二重発火を抑止する
        if (IsCrossSourceDuplicate(mediaKey, MediaKeySource.AppCommand))
        {
            return true;
        }

        // ユーザーが該当メディアキー（修飾キー付きを含む）を明示的に割り当てている場合はユーザー設定を優先する
        if (TryResolveCustomCommand(mediaKey, modifiers, viewModel, out var customCommand, out _))
        {
            ExecuteCommand(customCommand);
            RecordMediaKey(mediaKey, MediaKeySource.AppCommand);
            return true;
        }

        // 修飾キーが押下されている場合は標準メディアキー動作へフォールバックしない
        if (modifiers != ModifierKeys.None)
        {
            return false;
        }

        // APPCOMMAND_MEDIA_PLAY / APPCOMMAND_MEDIA_PAUSE は現在の再生状態に応じて意図通りの操作を行う
        if (appCommand == ShortcutKeyHelper.APPCOMMAND_MEDIA_PLAY)
        {
            if (!viewModel.IsPlaying)
            {
                ExecuteCommand(viewModel.TogglePlayPauseCommand);
            }

            RecordMediaKey(mediaKey, MediaKeySource.AppCommand);
            return true;
        }

        if (appCommand == ShortcutKeyHelper.APPCOMMAND_MEDIA_PAUSE)
        {
            if (viewModel.IsPlaying)
            {
                ExecuteCommand(viewModel.TogglePlayPauseCommand);
            }

            RecordMediaKey(mediaKey, MediaKeySource.AppCommand);
            return true;
        }

        if (TryExecuteDefaultMediaKey(mediaKey, viewModel))
        {
            RecordMediaKey(mediaKey, MediaKeySource.AppCommand);
            return true;
        }

        return false;
    }

    private bool TryResolveCustomCommand(
        Key key,
        ModifierKeys modifiers,
        IShortcutCommandTarget viewModel,
        out ICommand? command,
        out bool allowRepeat)
    {
        if (Matches(_settings.PlayPauseShortcut, key, modifiers))
        {
            command = viewModel.TogglePlayPauseCommand;
            allowRepeat = false;
            return true;
        }

        if (Matches(_settings.StopShortcut, key, modifiers))
        {
            command = viewModel.StopCommand;
            allowRepeat = false;
            return true;
        }

        if (Matches(_settings.NextShortcut, key, modifiers))
        {
            command = viewModel.NextCommand;
            allowRepeat = false;
            return true;
        }

        if (Matches(_settings.PreviousShortcut, key, modifiers))
        {
            command = viewModel.PreviousCommand;
            allowRepeat = false;
            return true;
        }

        if (Matches(_settings.MuteShortcut, key, modifiers))
        {
            command = viewModel.ToggleMuteCommand;
            allowRepeat = false;
            return true;
        }

        if (Matches(_settings.VolumeUpShortcut, key, modifiers))
        {
            command = viewModel.IncreaseVolumeCommand;
            allowRepeat = true;
            return true;
        }

        if (Matches(_settings.VolumeDownShortcut, key, modifiers))
        {
            command = viewModel.DecreaseVolumeCommand;
            allowRepeat = true;
            return true;
        }

        command = null;
        allowRepeat = false;
        return false;
    }

    private static bool TryExecuteDefaultMediaKey(Key key, IShortcutCommandTarget viewModel)
    {
        ICommand? command = key switch
        {
            Key.MediaPlayPause => viewModel.TogglePlayPauseCommand,
            Key.MediaStop => viewModel.StopCommand,
            Key.MediaNextTrack => viewModel.NextCommand,
            Key.MediaPreviousTrack => viewModel.PreviousCommand,
            _ => null
        };

        if (command == null)
        {
            return false;
        }

        ExecuteCommand(command);
        return true;
    }

    private static bool Matches(ShortcutKeyConfig? config, Key key, ModifierKeys modifiers)
    {
        if (config == null)
        {
            return false;
        }

        Key configKey = ShortcutKeyHelper.ToWpfKey(config.Key);
        return configKey != Key.None && configKey == key && ShortcutKeyHelper.ToWpfModifiers(config.Modifiers) == modifiers;
    }

    private static void ExecuteCommand(ICommand? command)
    {
        if (command != null && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private bool IsCrossSourceDuplicate(Key mediaKey, MediaKeySource currentSource)
    {
        if (_lastMediaKey != mediaKey || _lastMediaKeySource == MediaKeySource.None || _lastMediaKeySource == currentSource)
        {
            return false;
        }

        long now = _timestampProvider();
        double elapsedMs = (now - _lastMediaKeyTimestamp) / _ticksPerMillisecond;
        return elapsedMs >= 0 && elapsedMs <= DuplicateGuardWindowMs;
    }

    private void RecordMediaKey(Key mediaKey, MediaKeySource source)
    {
        _lastMediaKey = mediaKey;
        _lastMediaKeySource = source;
        _lastMediaKeyTimestamp = _timestampProvider();
    }
}

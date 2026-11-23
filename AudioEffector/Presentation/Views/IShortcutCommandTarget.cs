using System.Windows.Input;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// ウィンドウショートカットおよびハードウェアメディアキーのコマンド実行先を表すインターフェース
/// </summary>
public interface IShortcutCommandTarget
{
    /// <summary>現在再生中かどうか</summary>
    bool IsPlaying { get; }

    /// <summary>再生/一時停止切替コマンド</summary>
    ICommand TogglePlayPauseCommand { get; }

    /// <summary>停止コマンド</summary>
    ICommand StopCommand { get; }

    /// <summary>次の曲コマンド</summary>
    ICommand NextCommand { get; }

    /// <summary>前の曲コマンド</summary>
    ICommand PreviousCommand { get; }

    /// <summary>ミュート切替コマンド</summary>
    ICommand ToggleMuteCommand { get; }

    /// <summary>音量上げコマンド</summary>
    ICommand IncreaseVolumeCommand { get; }

    /// <summary>音量下げコマンド</summary>
    ICommand DecreaseVolumeCommand { get; }
}

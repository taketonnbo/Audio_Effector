using System;

namespace AudioEffector;

/// <summary>
/// Audio Effector（WPF 本体）のコマンドライン起動オプションを表します。
/// </summary>
/// <param name="StartInMiniPlayer">ミニプレイヤー状態で初期起動する場合は <see langword="true"/>。</param>
/// <param name="ForceNormalWindow">通常ウィンドウでの明示起動が指定された場合は <see langword="true"/>（<c>StartMinimized</c> 設定を抑止）。</param>
public sealed record AppStartupOptions(bool StartInMiniPlayer, bool ForceNormalWindow = false)
{
    /// <summary>
    /// ミニプレイヤー状態で初期起動するためのコマンドライン引数。
    /// </summary>
    public const string MINI_PLAYER_ARGUMENT = "--mini-player";

    /// <summary>
    /// 通常ウィンドウ状態で初期起動するためのコマンドライン引数。
    /// </summary>
    public const string NORMAL_ARGUMENT = "--normal";

    /// <summary>
    /// コマンドライン引数配列を解析して <see cref="AppStartupOptions"/> を生成します。
    /// モード指定が複数含まれる場合は最後に指定されたモードを優先します。
    /// </summary>
    /// <param name="args">コマンドライン引数配列。</param>
    /// <returns>解析結果の起動オプション。</returns>
    public static AppStartupOptions Parse(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return new AppStartupOptions(StartInMiniPlayer: false, ForceNormalWindow: false);
        }

        bool startInMiniPlayer = false;
        bool forceNormalWindow = false;

        foreach (string arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            string trimmed = arg.Trim();
            if (string.Equals(trimmed, MINI_PLAYER_ARGUMENT, StringComparison.OrdinalIgnoreCase))
            {
                startInMiniPlayer = true;
                forceNormalWindow = false;
            }
            else if (string.Equals(trimmed, NORMAL_ARGUMENT, StringComparison.OrdinalIgnoreCase))
            {
                startInMiniPlayer = false;
                forceNormalWindow = true;
            }
        }

        return new AppStartupOptions(StartInMiniPlayer: startInMiniPlayer, ForceNormalWindow: forceNormalWindow);
    }
}

using System;
using System.Linq;

namespace AudioEffector;

/// <summary>
/// Audio Effector（WPF 本体）のコマンドライン起動オプションを表します。
/// </summary>
/// <param name="StartInMiniPlayer">ミニプレイヤー状態で初期起動する場合は <see langword="true"/>。</param>
public sealed record AppStartupOptions(bool StartInMiniPlayer)
{
    /// <summary>
    /// ミニプレイヤー状態で初期起動するためのコマンドライン引数。
    /// </summary>
    public const string MINI_PLAYER_ARGUMENT = "--mini-player";

    /// <summary>
    /// コマンドライン引数配列を解析して <see cref="AppStartupOptions"/> を生成します。
    /// </summary>
    /// <param name="args">コマンドライン引数配列。</param>
    /// <returns>解析結果の起動オプション。</returns>
    public static AppStartupOptions Parse(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return new AppStartupOptions(StartInMiniPlayer: false);
        }

        bool startInMiniPlayer = args.Any(arg =>
            !string.IsNullOrWhiteSpace(arg)
            && string.Equals(arg.Trim(), MINI_PLAYER_ARGUMENT, StringComparison.OrdinalIgnoreCase));

        return new AppStartupOptions(StartInMiniPlayer: startInMiniPlayer);
    }
}

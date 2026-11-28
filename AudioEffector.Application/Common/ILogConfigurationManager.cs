using System.Collections.Generic;

namespace AudioEffector.Application.Common;

/// <summary>
/// ログ出力レベルおよびログ保存先ディレクトリの動的構成を管理するサービスインターフェース
/// </summary>
public interface ILogConfigurationManager
{
    /// <summary>
    /// 設定画面で選択可能なログ出力レベル一覧を取得します
    /// </summary>
    IReadOnlyList<string> AvailableLogLevels { get; }

    /// <summary>
    /// 既定のログ保存先ディレクトリ（実行ディレクトリ配下の logs）の絶対パスを取得します
    /// </summary>
    string DefaultLogDirectory { get; }

    /// <summary>
    /// 現在適用されているログ保存先ディレクトリの絶対パスを取得します
    /// </summary>
    string CurrentLogDirectory { get; }

    /// <summary>
    /// 現在適用されているログ出力レベル名を取得します
    /// </summary>
    string CurrentLogLevel { get; }

    /// <summary>
    /// 既定ディレクトリ以外のカスタムログ保存先ディレクトリが有効かどうかを取得します
    /// </summary>
    bool IsCustomLogDirectory { get; }

    /// <summary>
    /// ログ出力レベル文字列を正規化します（未指定・不正値は "Info" へフォールバック）
    /// </summary>
    /// <param name="logLevel">正規化対象のログ出力レベル名</param>
    /// <returns>正規化済みのログ出力レベル名</returns>
    string NormalizeLogLevel(string? logLevel);

    /// <summary>
    /// 指定されたログ出力レベルおよび保存先ディレクトリをロギング構成へ即時適用します
    /// </summary>
    /// <param name="logLevel">適用するログ出力レベル（Trace / Debug / Info / Warn / Error / Off）</param>
    /// <param name="logDirectoryPath">ログ保存先ディレクトリパス（null または空白時は既定の logs フォルダ）</param>
    /// <returns>適用結果</returns>
    LogConfigurationApplyResult ApplyConfiguration(string? logLevel, string? logDirectoryPath);
}

/// <summary>
/// ログ出力設定の動的適用結果を表すレコード
/// </summary>
/// <param name="Succeeded">ロギング構成の適用に成功したかどうか</param>
/// <param name="AppliedLogLevel">実際に適用された正規化済みログ出力レベル</param>
/// <param name="EffectiveLogDirectory">実際に適用されたログ保存先ディレクトリの絶対パス</param>
/// <param name="UsedFallbackDirectory">指定先へのアクセス・書き込み失敗により既定ディレクトリへフォールバックした場合は true</param>
/// <param name="WarningMessage">フォールバックやエラー発生時のメッセージ（正常時は null）</param>
public sealed record LogConfigurationApplyResult(
    bool Succeeded,
    string AppliedLogLevel,
    string EffectiveLogDirectory,
    bool UsedFallbackDirectory,
    string? WarningMessage = null);

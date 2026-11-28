using System.Collections.Generic;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// ユーザープリセット・プレイリスト・お気に入り・再生履歴・ライブラリデータの保存先ディレクトリ変更と既存データ移行を統括するサービスインターフェース
/// </summary>
public interface IDataDirectoryMigrationService
{
    /// <summary>
    /// 移行および切替管理の対象となるデータファイル名一覧を取得します（settings.json は対象外）
    /// </summary>
    IReadOnlyList<string> ManagedFileNames { get; }

    /// <summary>
    /// 指定されたディレクトリに管理対象の既存データファイルが 1 件以上存在するかどうかを判定します
    /// </summary>
    /// <param name="sourceDirectory">確認対象のディレクトリパス</param>
    /// <returns>既存データファイルが存在する場合は true</returns>
    bool HasExistingDataFiles(string sourceDirectory);

    /// <summary>
    /// 2 つのディレクトリパスが正規化後に同一ディレクトリを指すかどうかを判定します
    /// </summary>
    /// <param name="pathA">比較対象のパス 1</param>
    /// <param name="pathB">比較対象のパス 2</param>
    /// <returns>同一ディレクトリであれば true</returns>
    bool IsSameDirectory(string? pathA, string? pathB);

    /// <summary>
    /// データ保存先ディレクトリを変更し、指定された方式で既存データファイルを移行したうえで設定とプロバイダへ即時反映します
    /// </summary>
    /// <param name="targetDirectory">新しい保存先ディレクトリパス（null または空白時は既定ディレクトリへリセット）</param>
    /// <param name="migrationMode">既存データファイルの移行方式</param>
    /// <returns>変更および移行の実行結果</returns>
    DataDirectoryChangeResult ChangeDataDirectory(string? targetDirectory, DataMigrationMode migrationMode);
}

/// <summary>
/// データ保存先ディレクトリ変更時の既存データ移行方式を表します
/// </summary>
public enum DataMigrationMode
{
    /// <summary>既存データファイルを移行せず、保存先パスのみ切り替える</summary>
    None,

    /// <summary>既存データファイルを新しい保存先へコピーし、元のファイルを保持する</summary>
    Copy,

    /// <summary>既存データファイルを新しい保存先へ移動し、元のファイルを削除する</summary>
    Move
}

/// <summary>
/// データ保存先ディレクトリ変更およびデータ移行の実行結果を表します
/// </summary>
/// <param name="Succeeded">変更処理が成功したかどうか</param>
/// <param name="Changed">実際に保存先パスが変更されたかどうか（同一ディレクトリ選択時は false）</param>
/// <param name="EffectivePath">変更後に有効となっているデータ保存先ディレクトリの絶対パス</param>
/// <param name="MigratedFileCount">コピーまたは移動されたデータファイル件数</param>
/// <param name="ErrorMessage">失敗時または警告時のメッセージ（正常時は null）</param>
public sealed record DataDirectoryChangeResult(
    bool Succeeded,
    bool Changed,
    string EffectivePath,
    int MigratedFileCount,
    string? ErrorMessage = null);

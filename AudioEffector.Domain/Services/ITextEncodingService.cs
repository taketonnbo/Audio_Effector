using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Services;

/// <summary>
/// 入力テキストファイルの文字コード判定・UTF-8 変換読み込みおよび LRC ファイルの UTF-8 書き出しを定義するポートです
/// </summary>
public interface ITextEncodingService
{
    /// <summary>
    /// 指定されたテキストファイルの文字コードおよび行数を非同期で検査します
    /// </summary>
    /// <param name="textFilePath">検査対象のテキストファイルパス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>検査結果 <see cref="TextEncodingInspectionResult"/></returns>
    Task<TextEncodingInspectionResult> InspectAsync(
        string textFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたテキストファイルを UTF-8 文字列として非同期読み込みします。
    /// 非 UTF-8 ファイルの場合、<paramref name="allowEncodingConversion"/> が <see langword="true"/> のときのみ内部変換を許可し、元ファイル自体は変更しません。
    /// </summary>
    /// <param name="textFilePath">読み込み対象のテキストファイルパス</param>
    /// <param name="allowEncodingConversion">非 UTF-8 からの変換をユーザーが承諾済みかどうか</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>UTF-8 相当へデコードされたテキスト全文</returns>
    Task<string> ReadAsUtf8TextAsync(
        string textFilePath,
        bool allowEncodingConversion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたパスへ LRC コンテンツを UTF-8（BOM なし）で安全に非同期保存します
    /// </summary>
    /// <param name="destinationPath">保存先のファイルパス</param>
    /// <param name="lrcContent">保存する LRC 文字列</param>
    /// <param name="overwrite">既存ファイルが存在する場合に上書きを許可するかどうか</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task SaveLrcFileAsync(
        string destinationPath,
        string lrcContent,
        bool overwrite,
        CancellationToken cancellationToken = default);
}

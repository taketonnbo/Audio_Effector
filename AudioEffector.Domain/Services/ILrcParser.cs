using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Services;

/// <summary>
/// LRC 形式のテキストを <see cref="LyricsDocument"/> へ解析し、また <see cref="LyricsDocument"/> を LRC 形式テキストへシリアライズする契約を定義します
/// </summary>
public interface ILrcParser
{
    /// <summary>
    /// LRC 形式またはプレーンテキスト形式の歌詞文字列を解析して <see cref="LyricsDocument"/> を構築します
    /// </summary>
    /// <param name="content">解析対象の歌詞テキスト</param>
    /// <returns>解析結果の <see cref="LyricsDocument"/></returns>
    LyricsDocument Parse(string content);

    /// <summary>
    /// <see cref="LyricsDocument"/> を LRC 形式のテキスト文字列へフォーマットします
    /// </summary>
    /// <param name="document">フォーマット対象の歌詞ドキュメント</param>
    /// <returns>LRC 形式に整形された文字列</returns>
    string Format(LyricsDocument document);

    /// <summary>
    /// LRC テキスト内の <c>[re:...]</c> メタデータタグから生成元種別を高速に判別します
    /// </summary>
    /// <param name="lrcContent">判定対象の LRC テキスト（または先頭メタデータ部）</param>
    /// <returns>判別された <see cref="LrcOriginType"/>（タグ未付与・他ツール由来時は <see cref="LrcOriginType.ManualOrExternal"/>）</returns>
    LrcOriginType ClassifyOrigin(string lrcContent);
}

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// 楽曲ライブラリの複合条件検索およびページネーション条件を表すクエリモデルです。
/// </summary>
/// <param name="Keyword">タイトル・アーティスト・アルバムを対象とした部分一致キーワード（大文字・小文字を区別しない、省略可）。</param>
/// <param name="Artist">アーティスト名での部分一致絞り込みフィルタ（大文字・小文字を区別しない、省略可）。</param>
/// <param name="Album">アルバム名での部分一致絞り込みフィルタ（大文字・小文字を区別しない、省略可）。</param>
/// <param name="Genre">ジャンル名での部分一致絞り込みフィルタ（大文字・小文字を区別しない、省略可）。</param>
/// <param name="IsFavoriteOnly">お気に入り登録済みの楽曲のみに絞り込むかどうか（既定値: <see langword="false"/>）。</param>
/// <param name="Limit">返却する最大件数（1〜200、既定値: 50）。</param>
/// <param name="Offset">ページネーション用の取得開始位置（0 以上、既定値: 0）。</param>
public sealed record TrackSearchQuery(
    string? Keyword = null,
    string? Artist = null,
    string? Album = null,
    string? Genre = null,
    bool IsFavoriteOnly = false,
    int Limit = TrackSearchQuery.DEFAULT_LIMIT,
    int Offset = TrackSearchQuery.DEFAULT_OFFSET)
{
    /// <summary>
    /// 返却件数の既定値。
    /// </summary>
    public const int DEFAULT_LIMIT = 50;

    /// <summary>
    /// 返却件数の最小許容値。
    /// </summary>
    public const int MIN_LIMIT = 1;

    /// <summary>
    /// 返却件数の最大許容値。
    /// </summary>
    public const int MAX_LIMIT = 200;

    /// <summary>
    /// 取得開始位置の既定値。
    /// </summary>
    public const int DEFAULT_OFFSET = 0;
}

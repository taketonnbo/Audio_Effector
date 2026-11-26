namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// アルバム一覧の複合条件検索およびページネーション条件を表すクエリモデルです。
/// </summary>
/// <param name="Keyword">アルバム名またはアーティスト名を対象とした部分一致キーワード（大文字・小文字を区別しない、省略可）。</param>
/// <param name="Artist">アーティスト名での部分一致絞り込みフィルタ（大文字・小文字を区別しない、省略可）。</param>
/// <param name="Genre">収録楽曲のジャンル名での部分一致絞り込みフィルタ（大文字・小文字を区別しない、省略可）。</param>
/// <param name="Year">リリース年での絞り込み条件（0 以上、省略可）。</param>
/// <param name="IncludeTracks">収録トラックの詳細（最新のお気に入り状態を含む）を必要とするかどうか（既定値: <see langword="false"/>）。</param>
/// <param name="Limit">返却する最大件数（1〜200、既定値: 50）。</param>
/// <param name="Offset">ページネーション用の取得開始位置（0 以上、既定値: 0）。</param>
public sealed record AlbumSearchQuery(
    string? Keyword = null,
    string? Artist = null,
    string? Genre = null,
    int? Year = null,
    bool IncludeTracks = false,
    int Limit = AlbumSearchQuery.DEFAULT_LIMIT,
    int Offset = AlbumSearchQuery.DEFAULT_OFFSET)
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

    /// <summary>
    /// リリース年の最小許容値。
    /// </summary>
    public const int MIN_YEAR = 0;
}

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// プレイリスト一覧の検索およびページネーション条件を表すクエリモデルです。
/// </summary>
/// <param name="Keyword">プレイリスト名を対象とした部分一致キーワード（大文字・小文字を区別しない、省略可）。</param>
/// <param name="Limit">返却する最大件数（1〜200、既定値: 50）。</param>
/// <param name="Offset">ページネーション用の取得開始位置（0 以上、既定値: 0）。</param>
public sealed record PlaylistSearchQuery(
    string? Keyword = null,
    int Limit = PlaylistSearchQuery.DEFAULT_LIMIT,
    int Offset = PlaylistSearchQuery.DEFAULT_OFFSET)
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

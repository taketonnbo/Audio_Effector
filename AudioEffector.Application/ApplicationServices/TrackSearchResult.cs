using AudioEffector.Domain.Entities;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// 楽曲ライブラリの複合条件検索結果（ページネーション情報付き）を表すモデルです。
/// </summary>
/// <param name="Tracks">ページネーション適用後に返却される楽曲一覧。</param>
/// <param name="TotalCount">検索条件に一致した総件数（ページネーション適用前）。</param>
/// <param name="Offset">適用された取得開始位置。</param>
/// <param name="Limit">適用された最大返却件数。</param>
public sealed record TrackSearchResult(
    IReadOnlyList<Track> Tracks,
    int TotalCount,
    int Offset,
    int Limit);

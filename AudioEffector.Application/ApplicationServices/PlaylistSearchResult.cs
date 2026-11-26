using AudioEffector.Domain.Entities;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// プレイリスト一覧の検索結果（ページネーション情報付き）を表すモデルです。
/// </summary>
/// <param name="Playlists">ページネーション適用後に返却されるプレイリスト一覧。</param>
/// <param name="TotalCount">検索条件に一致した総件数（ページネーション適用前）。</param>
/// <param name="Offset">適用された取得開始位置。</param>
/// <param name="Limit">適用された最大返却件数。</param>
public sealed record PlaylistSearchResult(
    IReadOnlyList<UserPlaylist> Playlists,
    int TotalCount,
    int Offset,
    int Limit);

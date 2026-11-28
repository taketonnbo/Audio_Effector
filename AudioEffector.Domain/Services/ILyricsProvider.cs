using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Domain.Services;

/// <summary>
/// 楽曲エンティティに対応する歌詞データを非同期で探索・読み込むポートインターフェースです。
/// </summary>
public interface ILyricsProvider
{
    /// <summary>
    /// 指定された楽曲に対応する歌詞データを探索し、非同期で読み込みます。
    /// </summary>
    /// <param name="track">歌詞を取得する対象楽曲。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>取得できた場合は <see cref="LyricsDocument"/>、該当する歌詞が見つからないまたは読み込めない場合は <see langword="null"/>。</returns>
    Task<LyricsDocument?> LoadLyricsAsync(Track track, CancellationToken cancellationToken = default);
}

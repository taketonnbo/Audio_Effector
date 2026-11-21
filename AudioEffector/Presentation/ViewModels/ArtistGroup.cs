using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// アーティスト単位で集約されたアルバムおよび楽曲情報を保持する表示用グループモデル
/// </summary>
public sealed class ArtistGroup
{
    /// <summary>
    /// アーティスト名
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 所属アルバム一覧
    /// </summary>
    public IReadOnlyList<Album> Albums { get; }

    /// <summary>
    /// 所属全楽曲一覧
    /// </summary>
    public IReadOnlyList<Track> Tracks { get; }

    /// <summary>
    /// 所属アルバム数
    /// </summary>
    public int AlbumCount => Albums.Count;

    /// <summary>
    /// 所属楽曲数
    /// </summary>
    public int TrackCount => Tracks.Count;

    /// <summary>
    /// アバター／代表ジャケット読み込み用の楽曲ファイルパス
    /// </summary>
    public string RepresentativeTrackFilePath { get; }

    /// <summary>
    /// アルバム数・楽曲数のサマリー表示文字列（例: "4 アルバム • 68 曲"）
    /// </summary>
    public string SummaryText { get; }

    /// <summary>
    /// ArtistGroupを初期化します
    /// </summary>
    /// <param name="name">アーティスト名</param>
    /// <param name="albums">所属アルバム一覧</param>
    /// <param name="tracks">所属楽曲一覧</param>
    public ArtistGroup(string name, IReadOnlyList<Album> albums, IReadOnlyList<Track> tracks)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "Unknown Artist" : name.Trim();
        Albums = albums ?? Array.Empty<Album>();
        Tracks = tracks ?? Array.Empty<Track>();
        RepresentativeTrackFilePath = Tracks.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.FilePath))?.FilePath ?? string.Empty;
        SummaryText = string.Format(CultureInfo.InvariantCulture, "{0} アルバム • {1} 曲", AlbumCount, TrackCount);
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// 音声アライメント／文字起こしエンジンへ渡すエンジン非依存のリクエストモデルです
/// </summary>
public sealed record AlignmentRequest
{
    /// <summary>
    /// 解析対象の音声ファイルパスを取得します
    /// </summary>
    public string AudioFilePath { get; }

    /// <summary>
    /// LRC 生成モード（テキスト強制アライメントまたは音声直接文字起こし）を取得します
    /// </summary>
    public LrcGenerationMode Mode { get; }

    /// <summary>
    /// テキスト強制アライメントモードで使用する UTF-8 歌詞行リストを取得します（音声直接文字起こしモード時は空）
    /// </summary>
    public IReadOnlyList<string> LyricsLines { get; }

    /// <summary>
    /// 音声認識の対象言語コード（例: <c>ja</c>, <c>en</c>、自動判定時は <see langword="null"/> または <c>auto</c>）を取得します
    /// </summary>
    public string? LanguageCode { get; }

    /// <summary>
    /// 使用するモデルファイルの明示パスを取得します（未指定時はエンジン既定のアクティブモデルを使用）
    /// </summary>
    public string? ModelPath { get; }

    /// <summary>
    /// 出力 LRC ヘッダ（<c>[ti:]</c>）へ設定する曲名を取得します
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// 出力 LRC ヘッダ（<c>[ar:]</c>）へ設定するアーティスト名を取得します
    /// </summary>
    public string? Artist { get; }

    /// <summary>
    /// 出力 LRC ヘッダ（<c>[al:]</c>）へ設定するアルバム名を取得します
    /// </summary>
    public string? Album { get; }

    /// <summary>
    /// <see cref="AlignmentRequest"/> の新しいインスタンスを初期化します
    /// </summary>
    /// <param name="audioFilePath">解析対象の音声ファイルパス</param>
    /// <param name="mode">LRC 生成モード</param>
    /// <param name="lyricsLines">歌詞行リスト（<see cref="LrcGenerationMode.TextAlignment"/> 時に指定）</param>
    /// <param name="languageCode">言語コード（省略時は自動判定）</param>
    /// <param name="modelPath">明示的なモデルファイルパス（任意）</param>
    /// <param name="title">楽曲タイトル（任意）</param>
    /// <param name="artist">アーティスト名（任意）</param>
    /// <param name="album">アルバム名（任意）</param>
    public AlignmentRequest(
        string audioFilePath,
        LrcGenerationMode mode,
        IEnumerable<string>? lyricsLines = null,
        string? languageCode = null,
        string? modelPath = null,
        string? title = null,
        string? artist = null,
        string? album = null)
    {
        if (string.IsNullOrWhiteSpace(audioFilePath))
        {
            throw new ArgumentException("音声ファイルパスは必須です。", nameof(audioFilePath));
        }

        AudioFilePath = audioFilePath.Trim();
        Mode = mode;
        LyricsLines = lyricsLines is null
            ? Array.Empty<string>()
            : new ReadOnlyCollection<string>(lyricsLines.Select(line => line ?? string.Empty).ToArray());
        LanguageCode = string.IsNullOrWhiteSpace(languageCode) ? null : languageCode.Trim();
        ModelPath = string.IsNullOrWhiteSpace(modelPath) ? null : modelPath.Trim();
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        Artist = string.IsNullOrWhiteSpace(artist) ? null : artist.Trim();
        Album = string.IsNullOrWhiteSpace(album) ? null : album.Trim();
    }
}

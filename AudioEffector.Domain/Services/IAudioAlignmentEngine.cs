using System;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Services;

/// <summary>
/// 音声データからタイムタグ付き同期歌詞（LRC）を生成する解析エンジンの抽象ポートを定義します。
/// ローカルモデル実装（Whisper 等）および将来の外部音声認識／アライメント API 実装を透過的に差し替え可能とします。
/// </summary>
public interface IAudioAlignmentEngine
{
    /// <summary>
    /// 解析エンジンの識別名を取得します
    /// </summary>
    string EngineName { get; }

    /// <summary>
    /// 指定されたリクエスト（テキスト強制アライメントまたは音声直接文字起こし）に基づいて同期歌詞を非同期生成します
    /// </summary>
    /// <param name="request">音声ファイル・生成モード・歌詞行・言語等を含むリクエスト</param>
    /// <param name="progress">進捗通知レシーバー（任意）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>生成された同期歌詞ドキュメントおよび LRC 文字列を含む <see cref="AlignmentResult"/></returns>
    Task<AlignmentResult> GenerateAsync(
        AlignmentRequest request,
        IProgress<AlignmentProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

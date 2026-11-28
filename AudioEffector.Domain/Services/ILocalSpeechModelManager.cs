using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Services;

/// <summary>
/// ローカル音声認識モデルファイルの配置状態確認・手動ファイル指定・自動ダウンロードを管理するポートを定義します
/// </summary>
public interface ILocalSpeechModelManager
{
    /// <summary>
    /// 現在アクティブなモデル情報および利用可否を取得します
    /// </summary>
    /// <returns>アクティブな <see cref="LocalSpeechModelInfo"/></returns>
    LocalSpeechModelInfo GetActiveModelInfo();

    /// <summary>
    /// ダウンロードまたは切替に対応している標準モデル一覧を取得します
    /// </summary>
    /// <returns>対応モデル情報一覧</returns>
    IReadOnlyList<LocalSpeechModelInfo> GetSupportedModels();

    /// <summary>
    /// 使用する既定の標準モデル識別子（例: <c>tiny</c>, <c>base</c>, <c>small</c>）を選択します
    /// </summary>
    /// <param name="modelId">モデル識別子</param>
    void SelectModel(string modelId);

    /// <summary>
    /// ユーザーが手動配置したローカルモデルファイル（<c>.bin</c> 等）のパスを設定します（<see langword="null"/> 指定時は標準モデルへ復帰）
    /// </summary>
    /// <param name="modelFilePath">モデルファイルパス（解除時は <see langword="null"/>）</param>
    void SetCustomModelPath(string? modelFilePath);

    /// <summary>
    /// 指定された標準モデルをローカル格納ディレクトリへ非同期ダウンロードします
    /// </summary>
    /// <param name="modelId">ダウンロード対象のモデル識別子（省略時は既定モデル）</param>
    /// <param name="progress">ダウンロード進捗（0.0〜1.0、任意）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>ダウンロード完了後の <see cref="LocalSpeechModelInfo"/></returns>
    Task<LocalSpeechModelInfo> DownloadModelAsync(
        string? modelId = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

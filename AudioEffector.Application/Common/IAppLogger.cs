using System;

namespace AudioEffector.Application.Common;

/// <summary>
/// アプリケーション層から外部ロギング実装へ直接依存せずにログを記録するための抽象ロガー契約を定義します
/// </summary>
public interface IAppLogger
{
    /// <summary>
    /// 情報レベルのログを出力します
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    void Info(string message);

    /// <summary>
    /// 警告レベルのログを出力します
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    /// <param name="exception">関連する例外（省略可能）</param>
    void Warn(string message, Exception? exception = null);

    /// <summary>
    /// エラーレベルのログを出力します
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    /// <param name="exception">関連する例外（省略可能）</param>
    void Error(string message, Exception? exception = null);
}

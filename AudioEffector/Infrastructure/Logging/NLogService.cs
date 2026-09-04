using System;
using NLog;

namespace AudioEffector.Infrastructure.Logging;

/// <summary>
/// NLogを利用してアプリケーション全体のログ出力を行うサービス具象クラス
/// </summary>
public class NLogService
{
    private readonly ILogger _logger;

    /// <summary>
    /// 指定された名前のロガーでNLogServiceを初期化します
    /// </summary>
    /// <param name="name">ロガー名（未指定時はデフォルトロガー）</param>
    public NLogService(string? name = null)
    {
        _logger = string.IsNullOrEmpty(name)
            ? LogManager.GetCurrentClassLogger()
            : LogManager.GetLogger(name);
    }

    /// <summary>
    /// デバッグレベルのログを出力します
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    public void Debug(string message)
    {
        _logger.Debug(message);
    }

    /// <summary>
    /// 情報レベルのログを出力します
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    public void Info(string message)
    {
        _logger.Info(message);
    }

    /// <summary>
    /// 警告レベルのログを出力します
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    /// <param name="exception">関連する例外（省略可能）</param>
    public void Warn(string message, Exception? exception = null)
    {
        if (exception != null)
        {
            _logger.Warn(exception, message);
        }
        else
        {
            _logger.Warn(message);
        }
    }

    /// <summary>
    /// エラーレベルのログを出力します
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    /// <param name="exception">関連する例外（省略可能）</param>
    public void Error(string message, Exception? exception = null)
    {
        if (exception != null)
        {
            _logger.Error(exception, message);
        }
        else
        {
            _logger.Error(message);
        }
    }
}

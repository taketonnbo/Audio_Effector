using System;

namespace AudioEffector.Application.Common;

/// <summary>
/// LRC 自動生成・モデルダウンロード・ファイル入出力で発生した外部ライブラリや OS 起因の例外を、
/// ユーザー向けの分かりやすい日本語メッセージへラップして伝達するための例外クラスです
/// </summary>
public sealed class LrcGenerationException : Exception
{
    /// <summary>
    /// 原因カテゴリとユーザー向けメッセージを指定して <see cref="LrcGenerationException"/> を初期化します
    /// </summary>
    /// <param name="category">エラーの原因カテゴリ</param>
    /// <param name="userMessage">UI へ安全に表示可能な日本語エラーメッセージ</param>
    /// <param name="innerException">元の技術的例外（ログおよび診断用）</param>
    /// <param name="isLogged">送出元ですでにログ記録済みかどうか</param>
    public LrcGenerationException(
        LrcGenerationErrorCategory category,
        string userMessage,
        Exception? innerException = null,
        bool isLogged = false)
        : base(string.IsNullOrWhiteSpace(userMessage) ? LrcGenerationErrorMessageResolver.DEFAULT_GENERATION_ERROR_MESSAGE : userMessage, innerException)
    {
        Category = category;
        IsLogged = isLogged;
    }

    /// <summary>
    /// エラーの原因カテゴリを取得します
    /// </summary>
    public LrcGenerationErrorCategory Category { get; }

    /// <summary>
    /// 送出元ですでに詳細ログが記録済みかどうかを取得します
    /// </summary>
    public bool IsLogged { get; }
}

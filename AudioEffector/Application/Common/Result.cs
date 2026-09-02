using System;

namespace AudioEffector.Application.Common;

/// <summary>
/// 処理の成否およびエラーメッセージを表す汎用結果型
/// </summary>
public class Result
{
    /// <summary>
    /// 処理が成功したかどうか
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// 処理が失敗したかどうか
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// 失敗時のエラーメッセージ
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// Resultを初期化します
    /// </summary>
    /// <param name="isSuccess">成功フラグ</param>
    /// <param name="errorMessage">エラーメッセージ</param>
    protected Result(bool isSuccess, string errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// 成功結果を生成します
    /// </summary>
    /// <returns>成功のResultインスタンス</returns>
    public static Result Success() => new(true, string.Empty);

    /// <summary>
    /// 失敗結果を生成します
    /// </summary>
    /// <param name="errorMessage">エラーメッセージ</param>
    /// <returns>失敗のResultインスタンス</returns>
    public static Result Failure(string errorMessage) => new(false, errorMessage);

    /// <summary>
    /// 値を保持する成功結果を生成します
    /// </summary>
    /// <typeparam name="T">値の型</typeparam>
    /// <param name="value">成功時の返却値</param>
    /// <returns>成功のResult(T)インスタンス</returns>
    public static Result<T> Success<T>(T value) => new(value, true, string.Empty);

    /// <summary>
    /// 値を保持する失敗結果を生成します
    /// </summary>
    /// <typeparam name="T">値の型</typeparam>
    /// <param name="errorMessage">エラーメッセージ</param>
    /// <returns>失敗のResult(T)インスタンス</returns>
    public static Result<T> Failure<T>(string errorMessage) => new(default, false, errorMessage);
}

/// <summary>
/// 成功時の返却値を保持する汎用結果型
/// </summary>
/// <typeparam name="T">返却値の型</typeparam>
public class Result<T> : Result
{
    private readonly T? _value;

    /// <summary>
    /// 成功時の返却値を取得します（失敗時にアクセスするとInvalidOperationExceptionが発生します）
    /// </summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"失敗したResultから値を取得することはできません: {ErrorMessage}");

    /// <summary>
    /// Result(T)を初期化します
    /// </summary>
    /// <param name="value">返却値</param>
    /// <param name="isSuccess">成功フラグ</param>
    /// <param name="errorMessage">エラーメッセージ</param>
    internal Result(T? value, bool isSuccess, string errorMessage)
        : base(isSuccess, errorMessage)
    {
        _value = value;
    }
}

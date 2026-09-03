using System;
using System.Reflection;
using System.Threading.Tasks;
using NLog;

namespace AudioEffector.Infrastructure.Logging;

/// <summary>
/// メソッド呼び出しのロギングを透過的に行うプロキシクラス
/// </summary>
/// <typeparam name="T">対象インターフェース型</typeparam>
public class LoggingProxy<T> : DispatchProxy
{
    private T _target = default!;
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod == null) return null;

        try
        {
            var className = typeof(T).Name;
            var methodName = targetMethod.Name;

            var attr = targetMethod.GetCustomAttribute<LogDescriptionAttribute>();
            var description = attr != null ? $" - 処理内容: {attr.Description}" : "";

            Logger.Info($"[{className}] {methodName} called.{description}");

            var result = targetMethod.Invoke(_target, args);

            // 非同期メソッドの場合の完了・例外ハンドリング
            if (result is Task task)
            {
                task.ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                    {
                        Logger.Error(t.Exception, $"[{className}] {methodName} failed.");
                    }
                }, TaskContinuationOptions.ExecuteSynchronously);
            }

            return result;
        }
        catch (TargetInvocationException ex)
        {
            Logger.Error(ex.InnerException ?? ex, $"[{typeof(T).Name}] {targetMethod.Name} threw an exception.");
            throw ex.InnerException ?? ex;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"[{typeof(T).Name}] {targetMethod.Name} threw an exception.");
            throw;
        }
    }

    /// <summary>
    /// 対象オブジェクトのロギングプロキシインスタンスを生成します
    /// </summary>
    /// <param name="target">対象オブジェクト</param>
    /// <returns>プロキシインスタンス</returns>
    public static T Create(T target)
    {
        object proxy = Create<T, LoggingProxy<T>>();
        ((LoggingProxy<T>)proxy)._target = target;
        return (T)proxy;
    }
}

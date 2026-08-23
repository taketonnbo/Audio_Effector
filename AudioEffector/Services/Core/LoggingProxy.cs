using NLog;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace AudioEffector.Services
{
    public class LoggingProxy<T> : DispatchProxy
    {
        private T _target;
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

                // Handle async methods if necessary, to log when they finish or error out
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

        public static T Create(T target)
        {
            object proxy = Create<T, LoggingProxy<T>>();
            ((LoggingProxy<T>)proxy)._target = target;
            return (T)proxy;
        }
    }
}

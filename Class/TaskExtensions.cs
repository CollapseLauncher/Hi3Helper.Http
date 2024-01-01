using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    internal static class TaskExtensions
    {
        internal const int DefaultTimeoutSec = 10;
        internal const int DefaultRetryAttempt = 5;

        internal static async Task<T> RetryTimeoutAfter<T>(Func<Task<T>> taskFunction, CancellationToken token = default, int timeout = DefaultTimeoutSec, int retryAttempt = DefaultRetryAttempt)
        {
            int retryTotal = retryAttempt;
            int lastTaskID = 0;
            while (retryTotal > 0)
            {
                try
                {
                    Task<T> taskDelegated = taskFunction();
                    lastTaskID = taskDelegated.Id;
                    Task<T> completedTask = await Task.WhenAny(taskDelegated, ThrowExceptionAfterTimeout<T>(timeout, token));
                    if (completedTask == taskDelegated)
                        return await taskDelegated;
                }
                catch (TaskCanceledException) { throw; }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    string msg = $"The operation for task ID: {lastTaskID} has timed out! Retrying attempt left: {retryTotal--}";
#if DEBUG
                    Console.WriteLine(msg);
#else
                    Http.PushLog(msg, DownloadLogSeverity.Warning);
#endif
                    await Task.Delay(1000); // Wait 1s interval before retrying
                    continue;
                }
            }
            throw new TimeoutException($"The operation for task ID: {lastTaskID} has timed out!");
        }

        internal static async Task<T> TimeoutAfter<T>(this Task<T> task, CancellationToken token = default, int timeout = DefaultTimeoutSec)
        {
            Task<T> completedTask = await Task.WhenAny(task, ThrowExceptionAfterTimeout<T>(timeout, token));
            if (completedTask == task)
                return await task;

            throw new TimeoutException($"The operation for task ID: {task.Id} has timed out!");
        }

        private static async Task<T> ThrowExceptionAfterTimeout<T>(int timeout, CancellationToken token = default)
        {
            int timeoutMs = timeout * 1000;
            await Task.Delay(timeoutMs, token);
            return default;
        }
    }
}

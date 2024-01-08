using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    internal static class TaskExtensions
    {
        internal const int DefaultTimeoutSec = 10;
        internal const int DefaultRetryAttempt = 5;

#if NETCOREAPP
        internal static async ValueTask TaskWhenAll(this IAsyncEnumerable<Task> tasks, CancellationToken token, int taskCount)
        {
            ParallelOptions parallelOptions = new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = taskCount };
            await Parallel.ForEachAsync(tasks, parallelOptions, async (task, _) =>
            {
                await task;
            });
        }
#endif

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
                    Task<T> completedTask = await Task.WhenAny(taskDelegated, ThrowExceptionAfterTimeout<T>(timeout, taskDelegated, token));
                    if (completedTask == taskDelegated)
                        return await taskDelegated;
                }
                catch (TaskCanceledException) { throw; }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    string msg = $"The operation for task ID: {lastTaskID} has timed out! Retrying attempt left: {retryTotal--}";
                    Http.PushLog(msg, DownloadLogSeverity.Warning);
                    await Task.Delay(1000); // Wait 1s interval before retrying
                    continue;
                }
            }
            throw new TimeoutException($"The operation for task ID: {lastTaskID} has timed out!");
        }

        internal static async Task<T> TimeoutAfter<T>(this Task<T> task, CancellationToken token = default, int timeout = DefaultTimeoutSec)
        {
            Task<T> completedTask = await Task.WhenAny(task, ThrowExceptionAfterTimeout<T>(timeout, task, token));
            return await completedTask;
        }

        private static async Task<T> ThrowExceptionAfterTimeout<T>(int timeout, Task mainTask, CancellationToken token = default)
        {
            int timeoutMs = timeout * 1000;
            await Task.Delay(timeoutMs, token);
            if (!(mainTask.IsCompleted ||
#if NETCOREAPP
                mainTask.IsCompletedSuccessfully ||
#endif
                mainTask.IsCanceled || mainTask.IsFaulted || mainTask.Exception != null))
                throw new TimeoutException($"The operation for task has timed out!");

            return default;
        }
    }
}

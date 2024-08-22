using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http.Legacy
{
    internal delegate
#if NET6_0_OR_GREATER
        ValueTask<TResult>
#else
        Task<TResult>
#endif
        ActionTimeoutValueTaskCallback<TResult>(CancellationToken token);

    internal delegate void ActionOnTimeOutRetry(int retryAttemptCount, int retryAttemptTotal, int timeOutSecond,
                                                int timeOutStep);

    internal static class TaskExtensions
    {
        internal const int DefaultTimeoutSec = 10;
        internal const int DefaultRetryAttempt = 5;

        internal static async
#if NET6_0_OR_GREATER
            ValueTask<TResult>
#else
            Task<TResult>
#endif
            WaitForRetryAsync<TResult>(Func<ActionTimeoutValueTaskCallback<TResult>> funcCallback, int? timeout = null,
                                       int? timeoutStep = null, int? retryAttempt = null,
                                       ActionOnTimeOutRetry actionOnRetry = null, CancellationToken fromToken = default)
        {
            if (timeout == null)
            {
                timeout = DefaultTimeoutSec;
            }

            if (retryAttempt == null)
            {
                retryAttempt = DefaultRetryAttempt;
            }

            if (timeoutStep == null)
            {
                timeoutStep = 0;
            }

            int retryAttemptCurrent = 1;
            while (retryAttemptCurrent < retryAttempt)
            {
                fromToken.ThrowIfCancellationRequested();
                CancellationTokenSource innerCancellationToken = null;
                CancellationTokenSource consolidatedToken = null;

                try
                {
                    innerCancellationToken =
                        new CancellationTokenSource(TimeSpan.FromSeconds(timeout ?? DefaultTimeoutSec));
                    consolidatedToken =
                        CancellationTokenSource.CreateLinkedTokenSource(innerCancellationToken.Token, fromToken);

                    ActionTimeoutValueTaskCallback<TResult> delegateCallback = funcCallback();
                    return await delegateCallback(consolidatedToken.Token);
                }
                catch (TaskCanceledException)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    actionOnRetry?.Invoke(retryAttemptCurrent, retryAttempt ?? 0, timeout ?? 0, timeoutStep ?? 0);

                    if (ex is TimeoutException)
                    {
                        string msg =
                            $"The operation has timed out! Retrying attempt left: {retryAttemptCurrent}/{retryAttempt}";
                        Http.PushLog(msg, DownloadLogSeverity.Warning);
                    }
                    else
                    {
                        string msg =
                            $"The operation has thrown an exception! Retrying attempt left: {retryAttemptCurrent}/{retryAttempt}\r\n{ex}";
                        Http.PushLog(msg, DownloadLogSeverity.Error);
                    }

                    retryAttemptCurrent++;
                    timeout += timeoutStep;
                }
                finally
                {
                    innerCancellationToken?.Dispose();
                    consolidatedToken?.Dispose();
                }
            }

            throw new TimeoutException("The operation has timed out!");
        }
    }
}

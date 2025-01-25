using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http.Legacy
{
    public sealed partial class Http
    {
        internal async
#if NET6_0_OR_GREATER
            ValueTask
#else
            Task
#endif
            TaskWhenAllSession(IAsyncEnumerable<Session> sessions, CancellationToken token, int taskCount)
        {
            ParallelOptions parallelOptions = new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = taskCount };
#if NET6_0_OR_GREATER
            await Parallel.ForEachAsync(sessions, parallelOptions, async (session, innerToken) =>
            {
                await SessionTaskRunnerContainer(session, innerToken);
            });
#else
            ActionBlock<Session> actionBlock = new ActionBlock<Session>(
             async session =>
             {
                 await SessionTaskRunnerContainer(session, parallelOptions.CancellationToken);
             },
             new ExecutionDataflowBlockOptions
             {
                 MaxDegreeOfParallelism = parallelOptions.MaxDegreeOfParallelism,
                 CancellationToken = parallelOptions.CancellationToken
             });

            await foreach (Session session in sessions)
            {
                await actionBlock.SendAsync(session, parallelOptions.CancellationToken);
            }

            actionBlock.Complete();
            await actionBlock.Completion;
#endif
        }

        private async Task SessionTaskRunnerContainer(Session session, CancellationToken token)
        {
            if (session == null) return;
            DownloadEvent @event = new DownloadEvent();

            CancellationTokenSource innerTimeoutToken, cooperatedToken;
            while (true)
            {
                bool allowDispose = false;
                try
                {
                    DownloadState = DownloadState.Downloading;
                    session.SessionState = DownloadState.Downloading;

                    innerTimeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec));
                    cooperatedToken = CancellationTokenSource.CreateLinkedTokenSource(token, innerTimeoutToken.Token);

                    int read;
                    byte[] buffer = new byte[BufferSize];

                    // Read Stream into Buffer
                    while ((read = await session.StreamInput.ReadAsync(buffer, 0, BufferSize, cooperatedToken.Token)) > 0)
                    {
                        // Write Buffer to the output Stream
#if NET6_0_OR_GREATER
                        cooperatedToken.Token.ThrowIfCancellationRequested();
                        session.StreamOutput.Write(buffer, 0, read);
#else
                        await session.StreamOutput
                            .WriteAsync(Buffer, 0, Read, cooperatedToken.Token);
#endif
                        // Increment as last OffsetStart adjusted
                        session.OffsetStart += read;
                        // Set Inner Session Status
                        session.SessionState = DownloadState.Downloading;
                        // Reset session retry attempt
                        session.SessionRetryAttempt = 1;

                        // Reset the timeout token
                        innerTimeoutToken.Dispose();
                        cooperatedToken.Dispose();

                        // Reinitialize source token
                        innerTimeoutToken =
                            new CancellationTokenSource(TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec));
                        cooperatedToken =
                            CancellationTokenSource.CreateLinkedTokenSource(token, innerTimeoutToken.Token);

                        // Lock SizeAttribute to avoid race condition while updating status
                        // Increment SizeDownloaded attribute
                        Interlocked.Add(ref _sizeAttribute.SizeDownloaded, read);
                        Interlocked.Add(ref _sizeAttribute.SizeDownloadedLast, read);

                        // Update download state
                        @event.UpdateDownloadEvent(
                                _sizeAttribute.SizeDownloadedLast,
                                _sizeAttribute.SizeDownloaded,
                                _sizeAttribute.SizeTotalToDownload,
                                read,
                                _sessionsStopwatch.Elapsed.TotalSeconds,
                                DownloadState
                                );
                        UpdateProgress(@event);
                    }

                    allowDispose = true;
                    return;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    DownloadState = DownloadState.CancelledDownloading;
                    session.SessionState = DownloadState.CancelledDownloading;
                    allowDispose = true;
                    throw;
                }
                catch (Exception ex)
                {
                    PushLog($"An error has occurred on session ID: {session.SessionID}. The session will retry to re-establish the connection...\r\nException: {ex}", DownloadLogSeverity.Warning);
                    Tuple<bool, Exception> retryStatus = await session.TryReinitializeRequest(token);
                    if (retryStatus.Item1 && retryStatus.Item2 == null) continue;

                    allowDispose = true;
                    DownloadState = DownloadState.FailedDownloading;
                    session.SessionState = DownloadState.FailedDownloading;

                    if (ex is TaskCanceledException && !token.IsCancellationRequested)
                        throw new TimeoutException($"Request for session ID: {session.SessionID} has timed out!", ex);

                    throw retryStatus.Item2 != null ? retryStatus.Item2 : ex;
                }
                finally
                {
                    if (allowDispose)
                    {
#if NET6_0_OR_GREATER
                        await session.DisposeAsync();
#else
                        session.Dispose();
#endif
                        PushLog($"Disposed session ID {session.SessionID}!", DownloadLogSeverity.Info);
                    }
                }
            }
        }
    }
}

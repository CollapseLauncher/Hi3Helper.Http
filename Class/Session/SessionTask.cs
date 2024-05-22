using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Hi3Helper.Http
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
            using (CancellationTokenSource actionToken = new CancellationTokenSource())
            {
                using (CancellationTokenSource linkedToken = CancellationTokenSource
                          .CreateLinkedTokenSource(actionToken.Token, parallelOptions.CancellationToken))
                {
                    ActionBlock<Session> actionBlock = new ActionBlock<Session>(
                     async session =>
                     {
                         await SessionTaskRunnerContainer(session, linkedToken.Token);
                     },
                     new ExecutionDataflowBlockOptions
                     {
                         MaxDegreeOfParallelism = parallelOptions.MaxDegreeOfParallelism,
                         CancellationToken = linkedToken.Token
                     });

                    await foreach (Session session in sessions)
                    {
                        await actionBlock.SendAsync(session, linkedToken.Token);
                    }

                    actionBlock.Complete();
                    await actionBlock.Completion;
                }
            }
#endif
        }

        private async Task SessionTaskRunnerContainer(Session session, CancellationToken token)
        {
            if (session == null) return;
            DownloadEvent Event = new DownloadEvent();

            while (true)
            {
                bool AllowDispose = false;
                try
                {
                    this.DownloadState = DownloadState.Downloading;
                    session.SessionState = DownloadState.Downloading;

                    CancellationTokenSource innerTimeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec));
                    CancellationTokenSource cooperatedToken = CancellationTokenSource.CreateLinkedTokenSource(token, innerTimeoutToken.Token);

                    int Read;
                    byte[] Buffer = new byte[_bufferSize];

                    // Read Stream into Buffer
                    while ((Read = await session.StreamInput.ReadAsync(Buffer, 0, _bufferSize, cooperatedToken.Token)) > 0)
                    {
                        // Write Buffer to the output Stream
#if NET6_0_OR_GREATER
                        cooperatedToken.Token.ThrowIfCancellationRequested();
                        session.StreamOutput.Write(Buffer, 0, Read);
#else
                        await session.StreamOutput
                            .WriteAsync(Buffer, 0, Read, cooperatedToken.Token);
#endif
                        // Increment as last OffsetStart adjusted
                        session.OffsetStart += Read;
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
                        Interlocked.Add(ref this.SizeAttribute.SizeDownloaded, Read);
                        Interlocked.Add(ref this.SizeAttribute.SizeDownloadedLast, Read);

                        // Update download state
                        Event.UpdateDownloadEvent(
                                this.SizeAttribute.SizeDownloadedLast,
                                this.SizeAttribute.SizeDownloaded,
                                this.SizeAttribute.SizeTotalToDownload,
                                Read,
                                this.SessionsStopwatch.Elapsed.TotalSeconds,
                                this.DownloadState
                                );
                        this.UpdateProgress(Event);
                    }

                    AllowDispose = true;
                    return;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    this.DownloadState = DownloadState.CancelledDownloading;
                    session.SessionState = DownloadState.CancelledDownloading;
                    AllowDispose = true;
                    throw;
                }
                catch (Exception ex)
                {
                    PushLog($"An error has occurred on session ID: {session.SessionID}. The session will retry to re-establish the connection...\r\nException: {ex}", DownloadLogSeverity.Warning);
                    Tuple<bool, Exception> retryStatus = await session.TryReinitializeRequest(token);
                    if (retryStatus.Item1 && retryStatus.Item2 == null) continue;

                    AllowDispose = true;
                    this.DownloadState = DownloadState.FailedDownloading;
                    session.SessionState = DownloadState.FailedDownloading;
                    throw retryStatus.Item2 != null ? retryStatus.Item2 : ex;
                }
                finally
                {
                    if (AllowDispose)
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

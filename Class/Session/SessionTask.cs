using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class Http
    {

#if NETSTANDARD
        private async Task RunMultiSessionTasks()
        {
            List<Task> tasks = new List<Task>();

            foreach (Session session in this.Sessions)
            {
                tasks.Add(RetryableContainer(session));
            }

            await Task.WhenAll(tasks);

            this.DownloadState = MultisessionState.FinishedNeedMerge;
        }

#elif NETCOREAPP
        private IEnumerable<Task> RunMultiSessionTasks()
        {
            foreach (Session session in this.Sessions)
            {
                yield return Task.Run(() => RetryableContainer(session));
            }
        }
#endif

#if NETSTANDARD
        private async Task RetryableContainer(Session session)
#elif NETCOREAPP
        private void RetryableContainer(Session session)
#endif
        {
#if NETSTANDARD
            if (session == null) return;
#elif NETCOREAPP
            if (session == null) return;
#endif
            using (session)
            {
                CancellationToken InnerToken = this.InnerConnectionTokenSource.Token;
                bool StillRetry = true;
                while (StillRetry)
                {
                    session.SessionRetryAttempt++;
                    try
                    {
                        IOReadWriteSession(session, InnerToken);
                        StillRetry = false;
                    }
                    catch (TaskCanceledException)
                    {
                        StillRetry = false;
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        StillRetry = false;
                        throw;
                    }
                    catch (Exception ex)
                    {
#if NETSTANDARD
                        await session.TryReinitializeRequest(this._client);
#elif NETCOREAPP
                        session.TryReinitializeRequest(this._client);
#endif
                        if (session.SessionRetryAttempt > this.RetryMax)
                        {
                            StillRetry = false;
                            this.DownloadState = MultisessionState.FailedDownloading;
                            this.InnerConnectionTokenSource.Cancel();
                            PushLog($"[Retry {session.SessionRetryAttempt}/{this.RetryMax}] Retry attempt has been exceeded on session ID {session.SessionID}! Retrying...\r\nURL: {this.PathURL}\r\nException: {ex}", LogSeverity.Error);
                            throw;
                        }
                        PushLog($"[Retry {session.SessionRetryAttempt}/{this.RetryMax}] Error has occured on session ID {session.SessionID}!\r\nURL: {this.PathURL}\r\nException: {ex}", LogSeverity.Warning);
                    }
                }
            }
        }
    }
}

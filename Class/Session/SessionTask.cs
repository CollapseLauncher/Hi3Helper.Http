using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class Http
    {
        public async Task RunMultiSessionTasks()
        {
            List<Task> tasks = new List<Task>();

            foreach (Session session in this.Sessions)
            {
                tasks.Add(RetryableContainer(session));
            }

            await Task.WhenAll(tasks);

            this.DownloadState = MultisessionState.FinishedNeedMerge;
        }

        private async Task RetryableContainer(Session session)
        {
            if (session == null) return;
            using (session)
            {
                CancellationToken InnerToken = this.InnerConnectionTokenSource.Token;
                bool StillRetry = true;
                while (StillRetry)
                {
                    session.SessionRetryAttempt++;
                    try
                    {
                        await Task.Run(() => IOReadWriteSession(session, InnerToken));
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
                        await session.TryReinitializeRequest(this._client);
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

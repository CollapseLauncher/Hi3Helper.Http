using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{

    public partial class Http
    {
        private IEnumerable<Task> RunMultiSessionTasks()
        {
            foreach (Session session in this.Sessions)
            {
#if NETCOREAPP
                yield return Task.Run(() => RetryableContainer(session));
#else
                yield return RetryableContainer(session);
#endif
            }
        }

#if NETCOREAPP           
        private void RetryableContainer(Session session)
#else
        private async Task RetryableContainer(Session session)
#endif
        {
            if (session == null) return;

            bool StillRetry = true;
            while (StillRetry)
            {
                session.SessionRetryAttempt++;
                try
                {
#if NETCOREAPP
                    IOReadWriteSession(session);
#else
                    await Task.Run(() => IOReadWriteSession(session));
#endif
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
#if NETCOREAPP
                    session.TryReinitializeRequest();
#else
                    await session.TryReinitializeRequest();
#endif
                    if (session.SessionRetryAttempt > this.RetryMax)
                    {
                        StillRetry = false;
                        this.DownloadState = DownloadState.FailedDownloading;
                        PushLog($"[Retry {session.SessionRetryAttempt}/{this.RetryMax}] Retry attempt has been exceeded on session ID {session.SessionID}! Retrying...\r\nURL: {this.PathURL}\r\nException: {ex}", DownloadLogSeverity.Error);
                        throw;
                    }
                    PushLog($"[Retry {session.SessionRetryAttempt}/{this.RetryMax}] Error has occured on session ID {session.SessionID}!\r\nURL: {this.PathURL}\r\nException: {ex}", DownloadLogSeverity.Warning);
                }
                finally
                {
                    session.Dispose();
#if DEBUG
                    PushLog($"Disposed session ID {session.SessionID}!", DownloadLogSeverity.Info);
#endif
                }
            }
        }
    }
}

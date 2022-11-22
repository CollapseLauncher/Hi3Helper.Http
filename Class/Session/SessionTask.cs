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
#if NETSTANDARD
                yield return RetryableContainer(session);
#elif NETCOREAPP
                yield return Task.Run(() => RetryableContainer(session));
#endif
            }
        }

#if NETSTANDARD
        private async Task RetryableContainer(Session session)
#elif NETCOREAPP
        private void RetryableContainer(Session session)
#endif
        {
            if (session == null) return;

            bool StillRetry = true;
            while (StillRetry)
            {
                session.SessionRetryAttempt++;
                try
                {
#if NETSTANDARD
                    await Task.Run(() => IOReadWriteSession(session));
#elif NETCOREAPP
                    IOReadWriteSession(session);
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
#if NETSTANDARD
                    await session.TryReinitializeRequest();
#elif NETCOREAPP
                    session.TryReinitializeRequest();
#endif
                    if (session.SessionRetryAttempt > this.RetryMax)
                    {
                        StillRetry = false;
                        this.DownloadState = MultisessionState.FailedDownloading;
                        PushLog($"[Retry {session.SessionRetryAttempt}/{this.RetryMax}] Retry attempt has been exceeded on session ID {session.SessionID}! Retrying...\r\nURL: {this.PathURL}\r\nException: {ex}", LogSeverity.Error);
                        throw;
                    }
                    PushLog($"[Retry {session.SessionRetryAttempt}/{this.RetryMax}] Error has occured on session ID {session.SessionID}!\r\nURL: {this.PathURL}\r\nException: {ex}", LogSeverity.Warning);
                }
                finally
                {
                    session.Dispose();
#if DEBUG
                    PushLog($"Disposed session ID {session.SessionID}!", LogSeverity.Info);
#endif
                }
            }
        }
    }
}

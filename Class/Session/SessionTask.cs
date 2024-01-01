using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public sealed partial class Http
    {
        private IEnumerable<Task> RunMultiSessionTasks()
        {
            foreach (Session session in this.Sessions)
            {
                yield return RetryableContainer(session);
            }
        }

        private async Task RetryableContainer(Session session)
        {
            if (session == null) return;
            bool AllowDispose = false;

            while (true)
            {
                session.SessionRetryAttempt++;
                try
                {
                    await IOReadWriteSession(session);
                    AllowDispose = true;
                    return;
                }
                catch (TaskCanceledException)
                {
                    AllowDispose = true;
                    throw;
                }
                catch (OperationCanceledException)
                {
                    AllowDispose = true;
                    throw;
                }
                catch (Exception ex)
                {
                    await session.TryReinitializeRequest();
                    if (session.SessionRetryAttempt > this.RetryMax)
                    {
                        AllowDispose = true;
                        this.DownloadState = DownloadState.FailedDownloading;
                        PushLog($"[Retry {session.SessionRetryAttempt}/{this.RetryMax}] Retry attempt has been exceeded on session ID {session.SessionID}! Retrying...\r\nURL: {this.PathURL}\r\nException: {ex}", DownloadLogSeverity.Error);
                        throw;
                    }
                    PushLog($"[Retry {session.SessionRetryAttempt}/{this.RetryMax}] Error has occurred on session ID {session.SessionID}!\r\nURL: {this.PathURL}\r\nException: {ex}", DownloadLogSeverity.Warning);
                }
                finally
                {
                    if (AllowDispose)
                    {
#if NETCOREAPP
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

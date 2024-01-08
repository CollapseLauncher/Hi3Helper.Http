using System;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public sealed partial class Http
    {
        private async Task SessionTaskRunnerContainer(Session session)
        {
            if (session == null) return;
            while (true)
            {
                bool AllowDispose = false;
                try
                {
                    this.DownloadState = DownloadState.Downloading;
                    session.SessionState = DownloadState.Downloading;
                    await IOReadWriteSession(session);
                    AllowDispose = true;
                    return;
                }
                catch (TaskCanceledException)
                {
                    this.DownloadState = DownloadState.CancelledDownloading;
                    session.SessionState = DownloadState.CancelledDownloading;
                    AllowDispose = true;
                    throw;
                }
                catch (OperationCanceledException)
                {
                    this.DownloadState = DownloadState.CancelledDownloading;
                    session.SessionState = DownloadState.CancelledDownloading;
                    AllowDispose = true;
                    throw;
                }
                catch (Exception ex)
                {
                    PushLog($"An error has occurred on session ID: {session.SessionID}. The session will retry to re-establish the connection...\r\nException: {ex}", DownloadLogSeverity.Warning);
                    (bool, Exception) retryStatus = await session.TryReinitializeRequest();
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

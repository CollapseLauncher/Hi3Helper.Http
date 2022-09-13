using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class Http
    {
        public async Task<List<SessionAttribute>> GetSessionAttributeCollection(string URL, string OutputPath, bool Overwrite, byte Sessions, CancellationToken Token)
        {
            List<SessionAttribute> SessionAttributes = new List<SessionAttribute>();

            SessionState = MultisessionState.WaitingOnSession;

            this.SizeToBeDownloaded = await TryGetContentLength(URL, Token);

            long SliceSize = (long)Math.Ceiling((double)this.SizeToBeDownloaded / Sessions);

            for (long i = 0, t = 0; t < Sessions; t++)
            {
                SessionAttributes.Add(
                    new SessionAttribute(URL, OutputPath + string.Format(".{0:000}", t + 1), null,
                    Token, i, t + 1 == Sessions ? this.SizeToBeDownloaded : (i + SliceSize - 1), Overwrite)
                    { IsLastSession = t + 1 == Sessions });
                i += SliceSize;
            }

            CreateMetadataFile(OutputPath, new MetadataProp()
            {
                ChunkSize = Sessions,
                RemoteFileSize = this.SizeToBeDownloaded,
                CanOverwrite = Overwrite
            });

            return SessionAttributes;
        }

        private void GetLastExistedDownloadSize(ICollection<SessionAttribute> Attributes) => this.SizeDownloaded = Attributes.Sum(x => x.OutSize);

        private async Task<long> TryGetContentLength(string URL, CancellationToken Token)
        {
            byte CurrentRetry = 0;
            while (true)
            {
                try
                {
                    return await GetContentLength(URL, Token) ?? 0;
                }
                catch (HttpRequestException)
                {
                    CurrentRetry++;
                    if (CurrentRetry > this.MaxRetry)
                        throw;

                    PushLog($"Error while fetching File Size (Retry Attempt: {CurrentRetry})...", LogSeverity.Warning);
                    await Task.Delay((int)(this.RetryInterval), Token);
                }
            }
        }

        private async Task RetryableTaskContainer(SessionAttribute Session, bool IsMultisession = false)
        {
            bool CanDownload;
            if (IsMultisession)
                CanDownload = await GetSessionMultisession(Session);
            else
                CanDownload = await GetSession(Session);

            if (CanDownload) await Task.Run(() => StartWriteSession(Session));
        }

        private async Task StartRetryableTask(SessionAttribute Session, bool IsMultisession = false)
        {
            while (true)
            {
                bool CanThrow = Session.SessionRetry > this.MaxRetry;
                Task RetryTask = RetryableTaskContainer(Session, IsMultisession);
                try
                {
                    // Await InnerTask and watch for the throw
                    await RetryTask;
                    return;
                }
                catch (TaskCanceledException ex)
                {
                    throw new OperationCanceledException(string.Format("Task with SessionID: {0} has been cancelled!", RetryTask.Id), ex);
                }
                catch (OperationCanceledException ex)
                {
                    throw new OperationCanceledException(string.Format("Task with SessionID: {0} has been cancelled!", RetryTask.Id), ex);
                }
                catch (HttpHelperSessionNotReady) { if (CanThrow) throw; }
                catch (ArgumentOutOfRangeException) { throw; }
                catch (HttpHelperSessionHTTPError416) { throw; }
                catch (Exception) { if (CanThrow) throw; }
                finally
                {
                    // Return if the task is completed or throw
                    Session.DisposeInHttp();
                }

                PushLog(string.Format("Retrying task on SessionID: {0} (Retry: {1}/{2})...", RetryTask.Id, Session.SessionRetry, this.MaxRetry), LogSeverity.Warning);
                await Task.Delay((int)this.RetryInterval);
                Session.SessionRetry++;
            }
        }

        public async Task<long?> GetContentLength(string Input, CancellationToken token = new CancellationToken())
        {
            HttpResponseMessage response = await SendAsync(new HttpRequestMessage() { RequestUri = new Uri(Input) }, HttpCompletionOption.ResponseHeadersRead, token);

            long? Length = response.Content.Headers.ContentLength;

            response.Dispose();

            return Length;
        }

        private async Task TryAwaitOrDisposeStreamWhileFail(Task InnerTask, SessionAttribute Session = null)
        {
            try
            {
                await InnerTask;
                if (Session == null)
                    FinalizeMultisessionEventProgress();
            }
            catch (OperationCanceledException)
            {
                SessionState = MultisessionState.CancelledDownloading;
                throw;
            }
            catch (Exception ex)
            {
                SessionState = MultisessionState.FailedDownloading;
                PushLog($"Unhandled exception while downloading has occured!\r\n{ex}", LogSeverity.Error);
                throw new HttpHelperUnhandledError($"Unhandled exception while downloading has occured!\r\n{ex}", ex);
            }
            finally
            {
                TryDisposeSessionStream(Session);
            }
        }

        private void TryDisposeSessionStream(SessionAttribute Session)
        {
            if (Session == null)
                DisposeAllMultisessionStream();
            else if (Session.IsOutDisposable)
                Session.DisposeOutStream();
        }
    }
}

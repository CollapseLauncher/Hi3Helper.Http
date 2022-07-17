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
        public async Task<ICollection<SessionAttribute>> GetSessionAttributeCollection(string URL, string OutputPath, bool Overwrite, byte Sessions, CancellationToken Token)
        {
            ICollection<SessionAttribute> SessionAttributes = new List<SessionAttribute>();

            SessionState = MultisessionState.WaitingOnSession;

            this.SizeToBeDownloaded = await TryGetContentLength(URL, Token);
            WriteMetadataFile(OutputPath, new MetadataProp()
            {
                Sessions = Sessions,
                RemoteFileSize = this.SizeToBeDownloaded,
                CanOverwrite = Overwrite
            });

            long SliceSize = (long)Math.Ceiling((double)this.SizeToBeDownloaded / Sessions);

            for (long i = 0, t = 0; t < Sessions; t++)
            {
                SessionAttributes.Add(
                    new SessionAttribute(URL, OutputPath + string.Format(".{0:000}", t + 1), null,
                    Token, i, t + 1 == Sessions ? this.SizeToBeDownloaded : (i + SliceSize - 1), Overwrite)
                    { IsLastSession = t + 1 == Sessions });
                i += SliceSize;
            }

            return SessionAttributes;
        }

        private void GetLastExistedDownloadSize(ICollection<SessionAttribute> Attributes) => this.SizeDownloaded = Attributes.Sum(x => x.OutSize);

        private async Task<long> TryGetContentLength(string URL, CancellationToken Token)
        {
            byte CurrentRetry = 1;
            while (true)
            {
                try
                {
                    return await GetContentLength(URL, Token) ?? 0;
                }
                catch (HttpRequestException ex)
                {
                    if (CurrentRetry > this.MaxRetry)
                        throw new HttpRequestException(ex.ToString(), ex);

                    PushLog($"Error while fetching File Size (Retry Attempt: {CurrentRetry})...", LogSeverity.Warning);
                    await Task.Delay((int)(this.RetryInterval), Token);
                    CurrentRetry++;
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

            if (CanDownload) await StartWriteSession(Session);
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

                    // Return if the task is completed
                    Session.DisposeInHttp();
                    return;
                }
                catch (TaskCanceledException ex)
                {
                    Session.DisposeInHttp();
                    throw new TaskCanceledException(string.Format("Task with SessionID: {0} has been cancelled!", RetryTask.Id), ex);
                }
                catch (OperationCanceledException ex)
                {
                    Session.DisposeInHttp();
                    throw new OperationCanceledException(string.Format("Task with SessionID: {0} has been cancelled!", RetryTask.Id), ex);
                }
                catch (HttpHelperSessionNotReady ex)
                {
                    Session.DisposeInHttp();
                    if (CanThrow)
                    {
                        throw new HttpHelperSessionNotReady(ex.Message);
                    }
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    Session.DisposeInHttp();
                    throw new ArgumentOutOfRangeException(ex.Message, ex);
                }
                catch (HttpHelperSessionHTTPError416 ex)
                {
                    Session.DisposeInHttp();
                    throw new HttpHelperSessionHTTPError416(ex.Message);
                }
                catch (Exception ex)
                {
                    Session.DisposeInHttp();
                    if (CanThrow)
                        throw new Exception(string.Format("Unhandled exception has been thrown on SessionID: {0}\r\n{1}", RetryTask.Id, ex), ex);
                }

                PushLog(string.Format("Retrying task on SessionID: {0} (Retry: {1}/{2})...", RetryTask.Id, Session.SessionRetry, this.MaxRetry), LogSeverity.Warning);
                await Task.Delay((int)this.RetryInterval);
                Session.SessionRetry++;
            }
        }

        private async Task TryAwaitOrDisposeStreamWhileFail(Task InnerTask, SessionAttribute Session = null)
        {
            try
            {
                await InnerTask;
                if (Session == null)
                    FinalizeMultisessionEventProgress();
                TryDisposeSessionStream(Session);
            }
            catch (TaskCanceledException)
            {
                SessionState = MultisessionState.CancelledDownloading;
                TryDisposeSessionStream(Session);
                throw new OperationCanceledException();
            }
            catch (OperationCanceledException)
            {
                SessionState = MultisessionState.CancelledDownloading;
                TryDisposeSessionStream(Session);
                throw new OperationCanceledException();
            }
            catch (Exception ex)
            {
                SessionState = MultisessionState.FailedDownloading;
                TryDisposeSessionStream(Session);
                PushLog($"Unhandled exception while downloading has occured!\r\n{ex}", LogSeverity.Error);
                throw new HttpHelperUnhandledError($"Unhandled exception while downloading has occured!\r\n{ex}", ex);
            }
        }

        private void TryDisposeSessionStream(SessionAttribute Session)
        {
            if (Session == null)
                DisposeAllMultisessionStream();
            else if (Session.IsOutDisposable)
                Session.DisposeOutStream();
        }

        private void WriteMetadataFile(string PathOut, MetadataProp Metadata)
        {
            FileInfo file = new FileInfo(PathOut);
            if (file.Exists && Metadata.CanOverwrite)
                File.Delete(file.FullName);

            if (file.Exists && !Metadata.CanOverwrite && file.Length == Metadata.RemoteFileSize)
                throw new FileLoadException("File is already downloaded! Please consider to delete or move the existing file first.");

            using (BinaryWriter Writer = new BinaryWriter(new FileStream(PathOut + ".h3mtd", FileMode.Create, FileAccess.Write)))
            {
                Writer.Write(Metadata.Sessions);
                Writer.Write(Metadata.RemoteFileSize);
                Writer.Write(Metadata.CanOverwrite);
            }
        }

        private MetadataProp ReadMetadataFile(string PathOut)
        {
            MetadataProp ret = new MetadataProp();
            FileInfo file = new FileInfo(PathOut + ".h3mtd");

            if (!file.Exists)
                throw new HttpHelperSessionMetadataNotExist(string.Format("Metadata for \"{0}\" doesn't exist", PathOut));

            using (BinaryReader Reader = new BinaryReader(file.OpenRead()))
            {
                ret.Sessions = Reader.ReadByte();
                ret.RemoteFileSize = Reader.ReadInt64();
                ret.CanOverwrite = Reader.ReadBoolean();
            }

            return ret;
        }
    }
}

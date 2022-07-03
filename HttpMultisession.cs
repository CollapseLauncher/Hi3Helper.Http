using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class Http
    {
        public async Task DownloadMultisession(string URL, string OutPath, bool Overwrite = false,
            byte Sessions = 4, CancellationToken Token = new CancellationToken())
        {
            this.Sessions = Sessions;
            if (this.Sessions > this.MaxAllowedSessions)
                throw new HttpHelperAllowedSessionsMaxed(string.Format("You've maxed allowed Sessions ({1} has set and only <= {0} Sessions are allowed)", this.MaxAllowedSessions, this.Sessions));

            ResetAttributes();
            ICollection<Task> SessionTasks = new List<Task>();
            SessionAttributes = await GetSessionAttributeCollection(URL, OutPath, Overwrite, Sessions, Token);
            GetLastExistedDownloadSize(this.SessionAttributes);

            WaitForMultisessionReadyNoTask(Token);

            foreach (SessionAttribute Attr in this.SessionAttributes)
            {
                SessionTasks.Add(StartRetryableTask(Attr, true));
            }

            await TryAwaitOrDisposeStreamWhileFail(Task.WhenAll(SessionTasks));

            SessionState = MultisessionState.FinishedNeedMerge;
            SessionTasks.Clear();
        }

        public async void DownloadMultisessionNoTask(string URL, string OutPath, bool Overwrite = false,
            byte Sessions = 4, CancellationToken Token = new CancellationToken())
            => await DownloadMultisession(URL, OutPath, Overwrite, Sessions, Token);

        public async Task WaitForMultisessionReady(CancellationToken Token = new CancellationToken(), uint DelayInterval = 33)
        {
            try
            {
#if DEBUG
                PushLog("Waiting for all Sessions to be ready...", LogSeverity.Info);
#endif
                SessionState = MultisessionState.WaitingOnSession;
                while (SessionAttributes == null || SessionAttributes.All(x => x.SessionState != MultisessionState.Downloading))
                {
                    // Throw if cancel was requested
                    Token.ThrowIfCancellationRequested();
                    // Delay for 33 ms for each loop
                    await Task.Delay((int)DelayInterval);
                }
#if DEBUG
                PushLog("All Sessions are ready!", LogSeverity.Info);
#endif
                SessionState = MultisessionState.Downloading;
                WatchMultisessionEventProgress(Token);
            }
            catch (OperationCanceledException) { }
        }

        public async void WaitForMultisessionReadyNoTask(CancellationToken Token = new CancellationToken(), uint DelayInterval = 33)
            => await WaitForMultisessionReady(Token, DelayInterval);

        public async Task DeleteMultisessionChunks(string FileOut)
        {
            try
            {
                while (SessionState == MultisessionState.Downloading)
                    await Task.Delay(500);

                MetadataProp Metadata = ReadMetadataFile(FileOut);
                FileInfo FileChunk;
                for (byte i = 0; i < Metadata.Sessions; i++)
                {
                    FileChunk = new FileInfo(string.Format("{0}.{1:000}", FileOut, i + 1));
                    if (FileChunk.Exists) FileChunk.Delete();
                }

                FileChunk = new FileInfo(string.Format("{0}.h3mtd", FileOut));
                if (FileChunk.Exists) FileChunk.Delete();
            }
            catch (Exception ex)
            {
                PushLog(string.Format("Something went wrong while trying to clean up chunks!\r\n{0}", ex), LogSeverity.Warning);
            }
        }
    }
}

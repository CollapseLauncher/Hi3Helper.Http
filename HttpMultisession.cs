using System;
using System.Collections.Generic;
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
                SessionTasks.Add(StartRetryableTask(Task.Run(async () =>
                {
                    if (await GetSessionMultisession(Attr))
                        await StartSession(Attr);

                    Attr.DisposeOutStream();
                })));
            }

            await Task.WhenAll(SessionTasks);
            SessionTasks.Clear();

            FinalizeProgress();
        }

        public async void DownloadMultisessionNoTask(string URL, string OutPath, bool Overwrite = false,
            byte Sessions = 4, CancellationToken Token = new CancellationToken())
            => await DownloadMultisession(URL, OutPath, Overwrite, Sessions, Token);

        public void FinalizeProgress()
        {
            long i = this.SizeToBeDownloaded - this.SizeDownloaded;
            UpdateProgress(new DownloadEvent(this.SizeLastDownloaded, this.SizeToBeDownloaded, this.SizeToBeDownloaded,
                i < 0 ? 0 : i, this.SessionStopwatch.Elapsed.TotalSeconds, this.SessionState));
            this.SessionState = MultisessionState.FinishedNeedMerge;
        }

        public async Task WaitForMultisessionReady(CancellationToken Token = new CancellationToken(), uint DelayInterval = 33)
        {
#if DEBUG
            Console.WriteLine("Waiting for all Sessions to be ready...");
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
            Console.WriteLine("All Sessions are ready!");
#endif
            SessionState = MultisessionState.Downloading;
        }

        public async void WaitForMultisessionReadyNoTask(CancellationToken Token = new CancellationToken(), uint DelayInterval = 33)
            => await WaitForMultisessionReady(Token, DelayInterval);
    }
}

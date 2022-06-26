using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class Http
    {
        public async Task DownloadMultithread(string URL, string OutPath, bool Overwrite = false,
            byte Threads = 4, CancellationToken Token = new CancellationToken())
        {
            this.Threads = Threads;
            if (this.Threads > this.MaxAllowedThreads)
                throw new HttpHelperAllowedThreadsMaxed(string.Format("You've maxed allowed threads ({1} has set and only <= {0} threads are allowed)", this.MaxAllowedThreads, this.Threads));

            ResetAttributes();
            ICollection<Task> ThreadTasks = new List<Task>();
            SessionAttributes = await GetSessionAttributeCollection(URL, OutPath, Overwrite, Threads, Token);
            GetLastExistedDownloadSize(this.SessionAttributes);

            foreach (SessionAttribute Attr in this.SessionAttributes)
            {
                ThreadTasks.Add(StartRetryableTask(Task.Run(async () =>
                {
                    if (await GetSessionMultithread(Attr))
                        await StartSession(Attr);

                    Attr.DisposeOutStream();
                })));
            }

            await Task.WhenAll(ThreadTasks);
            ThreadTasks.Clear();

            FinalizeProgress();
        }

        public async void DownloadMultithreadNoTask(string URL, string OutPath, bool Overwrite = false,
            byte Threads = 4, CancellationToken Token = new CancellationToken())
            => await DownloadMultithread(URL, OutPath, Overwrite, Threads, Token);

        public void FinalizeProgress()
        {
            long i = this.SizeToBeDownloaded - this.SizeDownloaded;
            UpdateProgress(new DownloadEvent(this.SizeLastDownloaded, this.SizeToBeDownloaded, this.SizeToBeDownloaded, i, this.SessionStopwatch.Elapsed.TotalSeconds));
            Console.WriteLine("\r\nFile has been downloaded. Please use MergeMultithread() or MergeMultithreadNoTask() to merge it.");

            this.ThreadState = MultithreadState.FinishedNeedMerge;
        }

        public async Task WaitForMultithreadReady(CancellationToken Token = new CancellationToken(), uint DelayInterval = 33)
        {
#if DEBUG
            Console.WriteLine("Waiting for all threads to be ready...");
#endif
            ThreadState = MultithreadState.WaitingOnThread;
            while (SessionAttributes == null || SessionAttributes.All(x => x.ThreadState != MultithreadState.Downloading))
            {
                // Throw if cancel was requested
                Token.ThrowIfCancellationRequested();
                // Delay for 33 ms for each loop
                await Task.Delay((int)DelayInterval);
            }
#if DEBUG
            Console.WriteLine("All threads are ready!");
#endif
            ThreadState = MultithreadState.Downloading;
        }
    }
}

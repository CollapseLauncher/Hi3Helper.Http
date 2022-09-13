using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class HttpNew
    {
        // Download Progress Event Handler
        public event EventHandler<DownloadEvent> DownloadProgress;
        // Log for external listener
        public event EventHandler<DownloadLogEvent> DownloadLog;

        // Update Progress of the Download
        private void UpdateProgress(DownloadEvent Event) => DownloadProgress?.Invoke(this, Event);

        // Push log to listener
        private void PushLog(string message, LogSeverity severity) => DownloadLog?.Invoke(this, new DownloadLogEvent(message, severity));

        // Update Progress of the Multisession Download
        private async void WatchMultisessionEventProgress(string OutputPath, CancellationToken Token)
        {
            long MultisessionRead = 0;
            long SizeSum = this.Sessions.Sum(x => x.StreamOutputSize);
            long SizeMultisessionDownloaded = SizeSum;
            long SizeLastMultisessionDownloaded = 0;

            // Do loop if SessionState is Downloading or at least Token isn't cancelled
            while (this.DownloadState == MultisessionState.Downloading
                && !Token.IsCancellationRequested)
            {
                // Use .Sum() to summarize the OutSize on each session
                SizeSum = this.Sessions.Sum(x => x.StreamOutputSize);
                MultisessionRead = SizeSum - SizeMultisessionDownloaded;
                SizeLastMultisessionDownloaded += MultisessionRead;
                SizeMultisessionDownloaded = SizeSum;

                // Update progress to event
                UpdateProgress(new DownloadEvent(SizeLastMultisessionDownloaded, SizeMultisessionDownloaded,
                    this.SizeTotal, MultisessionRead, this.SessionsStopwatch.Elapsed.TotalSeconds,
                    this.DownloadState));

                // Delay 33ms before back to loop
                try
                {
                    /*
                    UpdateMetadataFile(OutputPath, new MetadataProp
                    {
                        RemoteFileSize = this.SizeToBeDownloaded,
                        ChunkSize = this.Sessions,
                        CanOverwrite = this.IsOverwrite
                    }, SessionAttributes);
                    */
                    await Task.Delay(250, Token);
                }
                catch (TaskCanceledException) { return; }
            }
        }

        private void FinalizeMultisessionEventProgress()
        {
            long Sum = this.SizeTotal - this.Sessions.Sum(x => x.StreamOutputSize);
            // Update progress to event
            UpdateProgress(new DownloadEvent(0, this.SizeTotal,
                this.SizeTotal, Sum, this.SessionsStopwatch.Elapsed.TotalSeconds,
                this.DownloadState));
        }
    }
}

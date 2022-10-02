using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class Http
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
            DownloadEvent Event = new DownloadEvent();

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
                Event.UpdateDownloadEvent(SizeLastMultisessionDownloaded, SizeMultisessionDownloaded,
                    this.SizeAttribute.SizeTotalToDownload, MultisessionRead, this.SessionsStopwatch.Elapsed.TotalSeconds,
                    this.DownloadState);
                UpdateProgress(Event);

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
            DownloadEvent Event = new DownloadEvent();
            long Sum = this.SizeAttribute.SizeTotalToDownload - this.Sessions.Sum(x => x.StreamOutputSize);

            // Update progress to event
            Event.UpdateDownloadEvent(0, this.SizeAttribute.SizeTotalToDownload,
                this.SizeAttribute.SizeTotalToDownload, Sum, this.SessionsStopwatch.Elapsed.TotalSeconds,
                this.DownloadState);
            UpdateProgress(Event);
        }
    }
}

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public enum MultisessionState
    {
        Idle, WaitingOnSession,
        Downloading, Merging,
        Finished, FinishedNeedMerge,
        FailedMerging, FailedDownloading,
        CancelledMerging, CancelledDownloading
    }

    public enum LogSeverity : int
    {
        Info = 0,
        Error = 1,
        Warning = 2
    }

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
        private async void WatchMultisessionEventProgress(CancellationToken Token)
        {
            long MultisessionRead = 0;
            long SizeSum = SessionAttributes.Sum(x => x.OutSize);
            long SizeMultisessionDownloaded = SizeSum;
            long SizeLastMultisessionDownloaded = 0;

            // Do loop if SessionState is Downloading or at least Token isn't cancelled
            while (SessionState == MultisessionState.Downloading
                && !Token.IsCancellationRequested)
            {
                // Use .Sum() to summarize the OutSize on each session
                SizeSum = SessionAttributes.Sum(x => x.OutSize);
                MultisessionRead = SizeSum - SizeMultisessionDownloaded;
                SizeLastMultisessionDownloaded += MultisessionRead;
                SizeMultisessionDownloaded = SizeSum;

                // Update progress to event
                UpdateProgress(new DownloadEvent(SizeLastMultisessionDownloaded, SizeMultisessionDownloaded,
                    this.SizeToBeDownloaded, MultisessionRead, this.SessionStopwatch.Elapsed.TotalSeconds,
                    this.SessionState));

                // Delay 33ms before back to loop
                try
                {
                    await Task.Delay(33, Token);
                }
                catch (TaskCanceledException) { return; }
            }
        }

        private void FinalizeMultisessionEventProgress()
        {
            long Sum = this.SizeToBeDownloaded - SessionAttributes.Sum(x => x.OutSize);
            // Update progress to event
            UpdateProgress(new DownloadEvent(0, this.SizeToBeDownloaded,
                this.SizeToBeDownloaded, Sum, this.SessionStopwatch.Elapsed.TotalSeconds,
                this.SessionState));
        }
    }

    public class DownloadLogEvent
    {
        public DownloadLogEvent(string message, LogSeverity severity)
        {
            this.Message = message;
            this.Severity = severity;
        }

        public string Message { get; private set; }
        public LogSeverity Severity { get; private set; }
    }

    public class DownloadEvent
    {
        public DownloadEvent(long SizeLastDownloaded, long SizeDownloaded, long SizeToBeDownloaded,
            long Read, double TotalSecond, MultisessionState state)
        {
            this.Speed = (long)(SizeLastDownloaded / TotalSecond);
            this.SizeDownloaded = SizeDownloaded;
            this.SizeToBeDownloaded = SizeToBeDownloaded;
            this.Read = Read;
            this.State = state;
        }

        public long SizeDownloaded { get; private set; }
        public long SizeToBeDownloaded { get; private set; }
        public double ProgressPercentage => Math.Round((SizeDownloaded / (double)SizeToBeDownloaded) * 100, 2);
        public long Read { get; private set; }
        public long Speed { get; private set; }
        public TimeSpan TimeLeft => checked(TimeSpan.FromSeconds((SizeToBeDownloaded - SizeDownloaded) / UnZeroed(Speed)));
        private long UnZeroed(long Input) => Math.Max(Input, 1);
        public MultisessionState State { get; set; }
    }
}

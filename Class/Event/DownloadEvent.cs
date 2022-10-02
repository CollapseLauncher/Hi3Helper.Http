using System;
using System.Collections.Generic;
using System.Text;

namespace Hi3Helper.Http
{
    public enum MultisessionState
    {
        Idle, WaitingOnSession,
        Downloading, Merging,
        Finished, FinishedNeedMerge,
        FailedMerging, FailedDownloading,
        CancelledMerging, CancelledDownloading,
        CheckingLastSessionIntegrity,
        CompleteLastSessionIntegrity
    }

    public enum LogSeverity : int
    {
        Info = 0,
        Error = 1,
        Warning = 2
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
            this.Speed = 0;
            this.SizeDownloaded = 0;
            this.SizeToBeDownloaded = 0;
            this.Read = 0;
            this.State = MultisessionState.Idle;
        }

        public DownloadEvent()
        {
            this.Speed = 0;
            this.SizeDownloaded = 0;
            this.SizeToBeDownloaded = 0;
            this.Read = 0;
            this.State = MultisessionState.Idle;
        }

        public void UpdateDownloadEvent(long SizeLastDownloaded, long SizeDownloaded, long SizeToBeDownloaded,
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

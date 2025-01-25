using System;

namespace Hi3Helper.Http.Legacy
{
    public sealed class DownloadEvent
    {
        public void UpdateDownloadEvent(long sizeLastDownloaded, long sizeDownloaded, long sizeToBeDownloaded,
                                        long read, double totalSecond, DownloadState state)
        {
            Speed = (long)(sizeLastDownloaded / totalSecond);
            SizeDownloaded = sizeDownloaded;
            SizeToBeDownloaded = sizeToBeDownloaded;
            Read = read;
            State = state;
        }

        public         long          SizeDownloaded       { get; set; }
        public         long          SizeToBeDownloaded   { get; set; }
        public         double        ProgressPercentage   => Math.Round(SizeDownloaded / (double)SizeToBeDownloaded * 100, 2);
        public         long          Read                 { get; set; }
        public         long          Speed                { get; set; }
        public         TimeSpan      TimeLeft             => checked(TimeSpan.FromSeconds((SizeToBeDownloaded - SizeDownloaded) / UnZeroed(Speed)));
        private static long          UnZeroed(long input) => Math.Max(input, 1);
        public         DownloadState State                { get; set; } = DownloadState.Idle;
    }
}

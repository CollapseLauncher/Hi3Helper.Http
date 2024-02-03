using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public sealed partial class Http
    {
        public async Task Merge(CancellationToken token)
        {
            if (this.DownloadState != DownloadState.FinishedNeedMerge)
            {
                throw new InvalidOperationException($"The download status is unfinished and cannot be merged. Also you should only use it while using multisession download!\r\nCurrent Status: {this.DownloadState}");
            }

            this.DownloadState = DownloadState.Merging;
            this.SessionsStopwatch = Stopwatch.StartNew();
            this.SizeAttribute.SizeDownloaded = 0;
            this.SizeAttribute.SizeDownloadedLast = 0;

            DownloadEvent Event = new DownloadEvent();

            using (FileStream fs = new FileStream(this.PathOutput, FileMode.Create, FileAccess.Write))
            {
                for (int t = 0; t < this.ConnectionSessions; t++)
                {
                    string chunkPath = this.PathOutput + string.Format(PathSessionPrefix, GetHashNumber(this.ConnectionSessions, t));
                    using (FileStream os = new FileStream(chunkPath, FileMode.Open, FileAccess.Read, FileShare.None, 4 << 10, FileOptions.DeleteOnClose))
                    {
                        await IOReadWrite(os, fs, token);
                    }
                }
            }

            // Update state
            Event.UpdateDownloadEvent(
                    this.SizeAttribute.SizeDownloadedLast,
                    this.SizeAttribute.SizeDownloaded,
                    this.SizeAttribute.SizeTotalToDownload,
                    0,
                    this.SessionsStopwatch.Elapsed.Milliseconds,
                    this.DownloadState = DownloadState.Finished
                    );

            this.UpdateProgress(Event);
        }
    }
}

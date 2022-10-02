using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class HttpNew
    {
        public async Task Merge()
        {
            if (this.DownloadState != MultisessionState.FinishedNeedMerge)
            {
                throw new InvalidOperationException($"The download status is unfinished and cannot be merged. Also you should only use it while using multisession download!\r\nCurrent Status: {this.DownloadState}");
            }

            this.DownloadState = MultisessionState.Merging;
            this.SessionsStopwatch = Stopwatch.StartNew();
            this.SizeAttribute.SizeDownloaded = 0;
            this.SizeAttribute.SizeDownloadedLast = 0;

            DownloadEvent Event = new DownloadEvent();

            try
            {
                using (FileStream fs = new FileStream(this.PathOutput, FileMode.Create, FileAccess.Write))
                {
                    for (int t = 0; t < this.ConnectionSessions; t++)
                    {
                        string chunkPath = this.PathOutput + string.Format(PathSessionPrefix, (69420 * this.ConnectionSessions) ^ (87654 * t));
                        using (FileStream os = new FileStream(chunkPath, FileMode.Open, FileAccess.Read, FileShare.None, 4 << 10, FileOptions.DeleteOnClose))
                        {
                            await Task.Run(() => IOReadWrite(os, fs, this._bufferMergeSize, this.ConnectionToken));
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
                        this.DownloadState = MultisessionState.Finished
                        );

                this.UpdateProgress(Event);
            }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                throw;
            }
        }

        public async void MergeAsync() => await Merge();

        public async Task WaitUntilAllMerged()
        {
            if (this.ConnectionToken.IsCancellationRequested) return;

            while (this.DownloadState == MultisessionState.Merging)
                await Task.Delay(33, this.ConnectionToken);
        }
    }
}

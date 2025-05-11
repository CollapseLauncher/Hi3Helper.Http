using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http.Legacy
{
    public sealed partial class Http
    {
        public async Task Merge(CancellationToken token)
        {
            if (DownloadState != DownloadState.FinishedNeedMerge)
            {
                throw new InvalidOperationException($"The download status is unfinished and cannot be merged. Also you should only use it while using multisession download!\r\nCurrent Status: {DownloadState}");
            }

            DownloadState = DownloadState.Merging;
            _sessionsStopwatch = Stopwatch.StartNew();
            _sizeAttribute.SizeDownloaded = 0;
            _sizeAttribute.SizeDownloadedLast = 0;

            DownloadEvent @event = new();

            using (FileStream fs = new(_pathOutput, FileMode.Create, FileAccess.Write))
            {
                for (int t = 0; t < _connectionSessions; t++)
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    string chunkPath = _pathOutput + string.Format(PathSessionPrefix, GetHashNumber(_connectionSessions, t));
#pragma warning restore CS0618 // Type or member is obsolete
                    string chunkPathNew = _pathOutput + $".{t + 1:000}";
                    using (FileStream os = new(File.Exists(chunkPath) ? chunkPath : chunkPathNew, FileMode.Open, FileAccess.Read, FileShare.None, 4 << 10, FileOptions.DeleteOnClose))
                    {
                        await IoReadWrite(os, fs, token);
                    }
                }
            }

            // Update state
            @event.UpdateDownloadEvent(
                    _sizeAttribute.SizeDownloadedLast,
                    _sizeAttribute.SizeDownloaded,
                    _sizeAttribute.SizeTotalToDownload,
                    0,
                    _sessionsStopwatch.Elapsed.Milliseconds,
                    DownloadState = DownloadState.Finished
                    );

            UpdateProgress(@event);
        }
    }
}

﻿namespace Hi3Helper.Http.Legacy
{
    public enum DownloadState
    {
        Idle, WaitingOnSession,
        Downloading, Merging,
        Finished, FinishedNeedMerge,
        FailedMerging, FailedDownloading,
        CancelledMerging, CancelledDownloading
    }

    public enum DownloadLogSeverity
    {
        Info = 0,
        Error = 1,
        Warning = 2
    }
}

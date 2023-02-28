namespace Hi3Helper.Http
{
    public enum DownloadState
    {
        Idle, WaitingOnSession,
        Downloading, Merging,
        Finished, FinishedNeedMerge,
        FailedMerging, FailedDownloading,
        CancelledMerging, CancelledDownloading
    }

    public enum DownloadLogSeverity : int
    {
        Info = 0,
        Error = 1,
        Warning = 2
    }
}

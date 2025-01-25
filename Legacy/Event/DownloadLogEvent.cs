namespace Hi3Helper.Http.Legacy
{
    public sealed class DownloadLogEvent
    {
        public DownloadLogEvent(string message, DownloadLogSeverity severity)
        {
            Message = message;
            Severity = severity;
        }

        public string Message { get; private set; }
        public DownloadLogSeverity Severity { get; private set; }
    }
}

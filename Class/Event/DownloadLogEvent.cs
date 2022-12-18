namespace Hi3Helper.Http
{
    public class DownloadLogEvent
    {
        public DownloadLogEvent(string message, DownloadLogSeverity severity)
        {
            this.Message = message;
            this.Severity = severity;
        }

        public string Message { get; private set; }
        public DownloadLogSeverity Severity { get; private set; }
    }
}

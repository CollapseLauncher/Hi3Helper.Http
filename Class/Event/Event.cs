using System;

namespace Hi3Helper.Http
{
    public sealed partial class Http
    {
        // Download Progress Event Handler
        public event EventHandler<DownloadEvent> DownloadProgress;
        // Log for external listener
        public static HttpLogInvoker LogInvoker = new HttpLogInvoker();

        // Update Progress of the Download
        private void UpdateProgress(DownloadEvent Event) => DownloadProgress?.Invoke(this, Event);

        // Push log to listener
        public static void PushLog(string message, DownloadLogSeverity severity) => LogInvoker.PushLog(message, severity);
    }

    public class HttpLogInvoker
    {
        // Log for external listener
        public static event EventHandler<DownloadLogEvent> DownloadLog;
        // Push log to listener
        public void PushLog(string message, DownloadLogSeverity severity) => DownloadLog?.Invoke(this, new DownloadLogEvent(message, severity));
    }
}

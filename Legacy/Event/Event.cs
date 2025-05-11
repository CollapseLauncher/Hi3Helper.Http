using System;

namespace Hi3Helper.Http.Legacy
{
    public sealed partial class Http
    {
        // Download Progress Event Handler
        public event EventHandler<DownloadEvent> DownloadProgress = null!;
        // Log for external listener
        private static readonly HttpLogInvoker LogInvoker = new();

        // Update Progress of the Download
        private void UpdateProgress(DownloadEvent @event) => DownloadProgress(this, @event);

        // Push log to listener
        public static void PushLog(string message, DownloadLogSeverity severity) => LogInvoker.PushLog(message, severity);
    }

    public class HttpLogInvoker
    {
        // Log for external listener
        public static event EventHandler<DownloadLogEvent>? DownloadLog;
        // Push log to listener
        public void PushLog(string message, DownloadLogSeverity severity) => DownloadLog?.Invoke(this, new DownloadLogEvent(message, severity));
    }
}

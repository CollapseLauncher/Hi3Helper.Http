using System;
// ReSharper disable UnusedAutoPropertyAccessor.Global

// ReSharper disable UnusedMember.Global

namespace Hi3Helper.Http
{
    public enum DownloadLogSeverity
    {
        Info,
        Error,
        Warning
    }

    public class LogEvent
    {
        public string Message { get; private set; }
        public DownloadLogSeverity Severity { get; private set; }

        private LogEvent(string message, DownloadLogSeverity severity)
        {
            Message = message;
            Severity = severity;
        }

        // Download Progress Event Handler
        public static event EventHandler<LogEvent>? DownloadEvent;

        // Push log to listener
        public static void PushLog(string message, DownloadLogSeverity severity)
        {
            DownloadEvent?.Invoke(null, new LogEvent(message, severity));
        }
    }
}
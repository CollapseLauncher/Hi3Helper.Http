using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class Http
    {
        // Download Progress Event Handler
        public event EventHandler<DownloadEvent> DownloadProgress;
        // Log for external listener
        public event EventHandler<DownloadLogEvent> DownloadLog;

        // Update Progress of the Download
        private void UpdateProgress(DownloadEvent Event) => DownloadProgress?.Invoke(this, Event);

        // Push log to listener
        private void PushLog(string message, LogSeverity severity) => DownloadLog?.Invoke(this, new DownloadLogEvent(message, severity));
    }
}

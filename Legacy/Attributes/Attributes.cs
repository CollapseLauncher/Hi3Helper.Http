using System;
using System.Diagnostics;
using System.Net.Http;

namespace Hi3Helper.Http.Legacy
{
    public sealed partial class Http
    {
        // Inner HttpClient instance
        private HttpClient _client;
        // Inner HttpClient UserAgent string
        private string _clientUserAgent;
        // Inner HttpClient instance
        private bool _ignoreHttpCompression;
        // Inner HttpClient handler
        private HttpClientHandler _handler;
        // Inner Buffer size
        private const int _bufferSize = 64 << 10;

        // Max allowed Connections for HttpClient instance
        private const byte ConnectionMax = 64;
        // Max allowed Sessions for HttpClient instance (in Multi-session mode)
        private const byte ConnectionSessionsMax = 16;
        // Sessions count
        private byte ConnectionSessions;

        // Max Retry Count
        private int RetryMax = TaskExtensions.DefaultRetryAttempt;
        // Retry Interval (in milliseconds)
        private int RetryInterval = TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec).Milliseconds;
        private Stopwatch SessionsStopwatch;

        // Path of the Download
        private string PathURL;
        private string PathOutput;
        private bool PathOverwrite;
        private const string PathSessionPrefix = ".{0}";

        // Download Statistics
        private AttributesSize SizeAttribute;

        // This is for Multisession mode only
        public DownloadState DownloadState { get; private set; }

        // Check for status of Dispose
        #pragma warning disable CS0414 // Field is assigned but its value is never used
        private bool IsDisposed = false;
#pragma warning restore CS0414 // Field is assigned but its value is never used

        [Obsolete("The file order should've now been in-order rather than randomized with this GetHashNumber method. Use .00x extenstion instead!")]
        public static long GetHashNumber(long num1, long num2, long s1 = 69420, long s2 = 87654) => (s1 * num1) ^ (s2 * num2);

        private void ResetState() => this.SessionsStopwatch.Restart();
    }
}

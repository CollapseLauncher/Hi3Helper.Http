using System;
using System.Diagnostics;
using System.Net.Http;
// ReSharper disable CommentTypo

namespace Hi3Helper.Http.Legacy
{
    public sealed partial class Http
    {
        // Inner HttpClient instance
        private readonly HttpClient _client;
        // Inner HttpClient UserAgent string
        private readonly string _clientUserAgent = null!;
        // Inner HttpClient instance
        private readonly bool _ignoreHttpCompression;
        // Inner HttpClient handler
        private HttpClientHandler _handler = null!;
        // Inner Buffer size
        private const int BufferSize = 64 << 10;

        // Max allowed Connections for HttpClient instance
        private const int ConnectionMax = 64;
        // Max allowed Sessions for HttpClient instance (in Multi-session mode)
        private const int ConnectionSessionsMax = 16;
        // Sessions count
        private int _connectionSessions;

        // Max Retry Count
        private readonly int _retryMax = TaskExtensions.DefaultRetryAttempt;
        // Retry Interval (in milliseconds)
        private readonly int       _retryInterval = TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec).Milliseconds;
        private          Stopwatch _sessionsStopwatch;

        // Path of the Download
        private       string _pathURL           = null!;
        private       string _pathOutput        = null!;
        private const string PathSessionPrefix = ".{0}";

        // Download Statistics
        private AttributesSize _sizeAttribute;

        // This is for Multisession mode only
        public DownloadState DownloadState { get; private set; }

        // Check for status of Dispose

        [Obsolete("The file order should've now been in-order rather than randomized with this GetHashNumber method. Use .00x extenstion instead!")]
        public static long GetHashNumber(long num1, long num2, long s1 = 69420, long s2 = 87654) => (s1 * num1) ^ (s2 * num2);

        private void ResetState() => _sessionsStopwatch.Restart();
    }
}

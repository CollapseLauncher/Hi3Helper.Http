using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace Hi3Helper.Http
{
    public partial class Http
    {
        // Inner HttpClient instance
        private HttpClient _client;
        // Inner HttpClient UserAgent string
        private string _clientUserAgent = null;
        // Inner HttpClient instance
        private bool _ignoreHttpCompression;
        // Inner HttpClient handler
        private HttpClientHandler _handler;
        // Inner Buffer size
        private const int _bufferSize = 16 << 10;
        private readonly byte[] _buffer = new byte[_bufferSize];
        // Inner Merge Buffer size
        private const int _bufferMergeSize = 8 << 20;

        // Max allowed Connections for HttpClient instance
        private const byte ConnectionMax = 16;
        // Max allowed Sessions for HttpClient instance (in Multi-session mode)
        private const byte ConnectionSessionsMax = 8;
        // Sessions count
        private byte ConnectionSessions;
        // Connection Token for Cancellation
        private CancellationToken ConnectionToken;

        // Max Retry Count
        private byte RetryMax = 5;
        // Retry Interval (in milliseconds)
        private short RetryInterval = 1000;

        // Sessions list
        private List<Session> Sessions = new List<Session>();
        private Stopwatch SessionsStopwatch;

        // Path of the Download
        private string PathURL;
        private string PathOutput;
        private bool PathOverwrite;
        private const string PathSessionPrefix = ".{0}";

        // Download Statistics
        private AttributesSize SizeAttribute = new AttributesSize();

        // This is for Multisession mode only
        public MultisessionState DownloadState;

        private long GetHashNumber(long num1, long num2, long s1 = 69420, long s2 = 87654) => (s1 * num1) ^ (s2 * num2);

        private void ResetState()
        {
            this.SessionsStopwatch = Stopwatch.StartNew();
        }
    }
}

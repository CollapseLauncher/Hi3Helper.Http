using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Linq;
using System.Net;

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
        private readonly int _bufferSize = 4 << 20;
        // Inner Merge Buffer size
        private readonly int _bufferMergeSize = 8 << 20;

        // Max allowed Connections for HttpClient instance
        private readonly byte ConnectionMax = 16;
        // Max allowed Sessions for HttpClient instance (in Multi-session mode)
        private readonly byte ConnectionSessionsMax = 8;
        // Sessions count
        private byte ConnectionSessions;
        // Connection Token for Cancellation
        private CancellationToken ConnectionToken;
        private CancellationTokenSource InnerConnectionTokenSource;

        // Max Retry Count
        private byte RetryMax = 5;
        // Retry Interval (in milliseconds)
        private short RetryInterval = 1000;

        // Sessions list
        private List<Session> Sessions = new List<Session>();
        private Stopwatch SessionsStopwatch;
        public bool IsDownloadContinue = false;

        // Path of the Download
        private string PathURL;
        private string PathOutput;
        private bool PathOverwrite;
        private const string PathSessionPrefix = ".{0}";

        // Download Statistics
        private AttributesSize SizeAttribute;

        // This is for Multisession mode only
        public MultisessionState DownloadState;

        public bool IsDownloadFailed
        {
            get
            {
                if (this.ConnectionToken.IsCancellationRequested) return false;

                return this.DownloadState == MultisessionState.FailedDownloading;
            }
        }

        public bool IsMergeFailed
        {
            get
            {
                if (this.ConnectionToken.IsCancellationRequested) return false;

                return this.DownloadState == MultisessionState.FailedMerging;
            }
        }

        public long GetHashNumber(long num1, long num2, long s1 = 69420, long s2 = 87654) => (s1 * num1) ^ (s2 * num2);

        public void ResetState(bool IsStop)
        {
            if (IsStop)
            {
                this.InnerConnectionTokenSource.Cancel();
                this.SessionsStopwatch.Stop();
                this._client.Dispose();
            }
            else
            {
                this.InnerConnectionTokenSource = new CancellationTokenSource();
                this.SessionsStopwatch = Stopwatch.StartNew();
                this._client = new HttpClient(this._handler = new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    UseCookies = true,
                    MaxConnectionsPerServer = this.ConnectionMax,
                    AutomaticDecompression = this._ignoreHttpCompression ? DecompressionMethods.None : DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.None
                });

                if (this._clientUserAgent != null)
                    this._client.DefaultRequestHeaders.UserAgent.ParseAdd(this._clientUserAgent);
            }

            this.Sessions.Clear();
        }
    }
}

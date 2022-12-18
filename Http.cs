using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class Http : IDisposable
    {
        public Http(bool IgnoreCompress = true, byte RetryMax = 5, short RetryInterval = 1000, string UserAgent = null)
        {
            this.RetryMax = RetryMax;
            this.RetryInterval = RetryInterval;
            this.DownloadState = DownloadState.Idle;
            this._clientUserAgent = UserAgent;
            this._ignoreHttpCompression = IgnoreCompress;
            this._handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                MaxConnectionsPerServer = ConnectionMax,
                AutomaticDecompression = this._ignoreHttpCompression ? DecompressionMethods.None : DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.None
            };

            this._client = new HttpClient(this._handler);

            if (this._clientUserAgent != null)
                this._client.DefaultRequestHeaders.UserAgent.ParseAdd(this._clientUserAgent);

            this.SessionsStopwatch = Stopwatch.StartNew();
        }

        public Http()
        {
            this.DownloadState = DownloadState.Idle;
            this._ignoreHttpCompression = true;
            this._handler = new HttpClientHandler()
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                MaxConnectionsPerServer = ConnectionMax,
                AutomaticDecompression = DecompressionMethods.None
            };

            this._client = new HttpClient(this._handler);

            this.SessionsStopwatch = Stopwatch.StartNew();
        }

#if NETCOREAPP
        public void DownloadSync(string URL, string Output,
            bool Overwrite, long? OffsetStart = null, long? OffsetEnd = null,
            CancellationToken ThreadToken = new CancellationToken())
        {
            ResetState();

            this.PathURL = URL;
            this.PathOutput = Output;
            this.PathOverwrite = Overwrite;
            this.ConnectionToken = ThreadToken;

            RetryableContainer(InitializeSingleSession(OffsetStart, OffsetEnd, true, null));

            this.DownloadState = DownloadState.Finished;
        }

        public void DownloadSync(string URL, Stream Outstream,
            long? OffsetStart = null, long? OffsetEnd = null,
            CancellationToken ThreadToken = new CancellationToken())
        {
            ResetState();

            this.PathURL = URL;
            this.ConnectionToken = ThreadToken;

            RetryableContainer(InitializeSingleSession(OffsetStart, OffsetEnd, false, Outstream));

            this.DownloadState = DownloadState.Finished;
        }
#endif

        public async Task Download(string URL, string Output,
            bool Overwrite, long? OffsetStart = null, long? OffsetEnd = null,
            CancellationToken ThreadToken = new CancellationToken())
        {
            ResetState();

            this.PathURL = URL;
            this.PathOutput = Output;
            this.PathOverwrite = Overwrite;
            this.ConnectionToken = ThreadToken;

#if NETCOREAPP           
            await Task.Run(() =>
                RetryableContainer(InitializeSingleSession(OffsetStart, OffsetEnd, true, null)));
#else
            await RetryableContainer(await InitializeSingleSession(OffsetStart, OffsetEnd, true, null));
#endif
            this.DownloadState = DownloadState.Finished;
        }

        public async Task Download(string URL, Stream Outstream,
            long? OffsetStart = null, long? OffsetEnd = null,
            CancellationToken ThreadToken = new CancellationToken())
        {
            ResetState();

            this.PathURL = URL;
            this.ConnectionToken = ThreadToken;

#if NETCOREAPP           
            await Task.Run(() =>
                RetryableContainer(InitializeSingleSession(OffsetStart, OffsetEnd, false, Outstream)));
#else
            await RetryableContainer(await InitializeSingleSession(OffsetStart, OffsetEnd, false, Outstream));
#endif
            this.DownloadState = DownloadState.Finished;
        }

        public void Dispose()
        {
            this._handler = null;
            this._client.Dispose();

            if (this.Sessions != null && this.Sessions.Count > 0)
            {
                this.Sessions.Clear();
            }

            this.Sessions = null;
            this.IsDisposed = true;
        }

        ~Http() => Dispose();
    }
}

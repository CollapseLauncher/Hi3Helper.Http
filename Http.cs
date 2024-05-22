using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public sealed partial class Http : IDisposable
    {
        public Http(bool IgnoreCompress = true, byte RetryMax = 5, short RetryInterval = 1000, string UserAgent = null)
        {
            this.RetryMax = RetryMax;
            this.RetryInterval = RetryInterval;
            this.DownloadState = DownloadState.Idle;
            this.SessionsStopwatch = Stopwatch.StartNew();
            this.SizeAttribute = new AttributesSize();
            this._clientUserAgent = UserAgent;
            this._ignoreHttpCompression = IgnoreCompress;
            this._handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                MaxConnectionsPerServer = ConnectionMax,
                AutomaticDecompression = this._ignoreHttpCompression ? DecompressionMethods.None : DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.None,
            };

            this._client = new HttpClient(this._handler)
            {
                Timeout = TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec)
#if NET6_0_OR_GREATER
                ,
                DefaultRequestVersion = HttpVersion.Version30,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
#endif
            };

            if (this._clientUserAgent != null)
                this._client.DefaultRequestHeaders.UserAgent.ParseAdd(this._clientUserAgent);
        }

        public Http()
        {
            this.DownloadState = DownloadState.Idle;
            this.SessionsStopwatch = Stopwatch.StartNew();
            this.SizeAttribute = new AttributesSize();
            this._ignoreHttpCompression = true;
            this._handler = new HttpClientHandler()
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                MaxConnectionsPerServer = ConnectionMax,
                AutomaticDecompression = DecompressionMethods.None
            };

            this._client = new HttpClient(this._handler)
            {
                Timeout = TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec)
#if NET6_0_OR_GREATER
                ,
                DefaultRequestVersion = HttpVersion.Version30,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
#endif
            };
        }

        public HttpClient GetHttpClient() => this._client;

        public async Task Download(string URL, string Output,
            bool Overwrite, long? OffsetStart = null, long? OffsetEnd = null,
            CancellationToken ThreadToken = new CancellationToken())
        {
            ResetState();

            this.PathURL = URL;
            this.PathOutput = Output;
            this.PathOverwrite = Overwrite;

            await SessionTaskRunnerContainer(await InitializeSingleSession(OffsetStart, OffsetEnd, true, null, false, ThreadToken), ThreadToken);

            this.DownloadState = DownloadState.Finished;
        }

        public async Task Download(string URL, Stream Outstream,
            long? OffsetStart = null, long? OffsetEnd = null,
            CancellationToken ThreadToken = new CancellationToken(), bool IgnoreOutStreamLength = false)
        {
            ResetState();

            this.PathURL = URL;

            await SessionTaskRunnerContainer(await InitializeSingleSession(OffsetStart, OffsetEnd, false, Outstream, IgnoreOutStreamLength, ThreadToken), ThreadToken);
            this.DownloadState = DownloadState.Finished;
        }

        public void Dispose()
        {
            FinalizeDispose();
        }

        private void FinalizeDispose()
        {
            this._handler = null;

            this._client.Dispose();
            this.IsDisposed = true;

            GC.SuppressFinalize(this);
        }

        ~Http() => Dispose();
    }
}

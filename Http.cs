using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class HttpNew : IDisposable
    {
        public HttpNew(bool IgnoreCompress = false, byte RetryMax = 5, short RetryInterval = 1000)
        {
            this.RetryMax = RetryMax;
            this.RetryInterval = RetryInterval;
            this.DownloadState = MultisessionState.Idle;
            this._ignoreHttpCompression = IgnoreCompress;
            this._handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                MaxConnectionsPerServer = this.ConnectionMax,
                AutomaticDecompression = this._ignoreHttpCompression ? DecompressionMethods.None : DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.None
            };

            ResetState(false);
        }

        public HttpNew()
        {
            this.DownloadState = MultisessionState.Idle;
            this._ignoreHttpCompression = false;
            this._handler = new HttpClientHandler()
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                MaxConnectionsPerServer = this.ConnectionMax
            };

            ResetState(false);
        }

        public async Task Download(string URL, string Output,
            bool Overwrite, long? OffsetStart = null, long? OffsetEnd = null,
            CancellationToken ThreadToken = new CancellationToken())
        {
            ResetState(false);

            this.PathURL = URL;
            this.PathOutput = Output;
            this.PathOverwrite = Overwrite;
            this.ConnectionToken = ThreadToken;

            Session session = await InitializeSingleSession(OffsetStart, OffsetEnd, true, null);
            await RetryableContainer(session);

            this.DownloadState = MultisessionState.Finished;

            ResetState(true);
        }

        public async Task Download(string URL, Stream Outstream,
            long? OffsetStart = null, long? OffsetEnd = null,
            CancellationToken ThreadToken = new CancellationToken())
        {
            ResetState(false);

            this.PathURL = URL;
            this.ConnectionToken = ThreadToken;

            Session session = await InitializeSingleSession(OffsetStart, OffsetEnd, false, Outstream);
            await RetryableContainer(session);

            this.DownloadState = MultisessionState.Finished;

            ResetState(true);
        }

        public void Dispose()
        {
            this._handler = null;
            this._client.Dispose();

            this.Sessions = null;
        }

        ~HttpNew() => Dispose();
    }
}

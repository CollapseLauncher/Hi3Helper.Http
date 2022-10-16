using System;
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
            this.DownloadState = MultisessionState.Idle;
            this._clientUserAgent = UserAgent;
            this._ignoreHttpCompression = IgnoreCompress;
            this._handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                MaxConnectionsPerServer = ConnectionMax,
                AutomaticDecompression = this._ignoreHttpCompression ? DecompressionMethods.None : DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.None
            };

            ResetState(false);
        }

        public Http()
        {
            this.DownloadState = MultisessionState.Idle;
            this._ignoreHttpCompression = true;
            this._handler = new HttpClientHandler()
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                MaxConnectionsPerServer = ConnectionMax,
                AutomaticDecompression = DecompressionMethods.None
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

#if NETSTANDARD
            Session session = await InitializeSingleSession(OffsetStart, OffsetEnd, true, null);
            await RetryableContainer(session);
#elif NETCOREAPP
            await Task.Run(() =>
            {
                Session session = InitializeSingleSession(OffsetStart, OffsetEnd, true, null);
                RetryableContainer(session);
            });
#endif

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

#if NETSTANDARD
            Session session = await InitializeSingleSession(OffsetStart, OffsetEnd, false, Outstream);
            await RetryableContainer(session);
#elif NETCOREAPP
            await Task.Run(() =>
            {

                Session session = InitializeSingleSession(OffsetStart, OffsetEnd, false, Outstream);
                RetryableContainer(session);
            });
#endif

            this.DownloadState = MultisessionState.Finished;

            ResetState(true);
        }

        public void Dispose()
        {
            this._handler = null;
            this._client.Dispose();

            this.Sessions = null;
        }

        ~Http() => Dispose();
    }
}

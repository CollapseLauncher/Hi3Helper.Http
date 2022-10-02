using System;
using System.Net;
using System.Net.Http;

namespace Hi3Helper.Http
{
    public partial class HttpNew : IDisposable
    {
        public HttpNew(bool IgnoreCompress = false, byte RetryMax = 5, short RetryInterval = 1000)
        {
            this.RetryMax = RetryMax;
            this.RetryInterval = RetryInterval;
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
            this._ignoreHttpCompression = false;
            this._handler = new HttpClientHandler()
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                MaxConnectionsPerServer = this.ConnectionMax
            };

            ResetState(false);
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

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
            this._handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                MaxConnectionsPerServer = this.ConnectionMax,
                AutomaticDecompression = IgnoreCompress ? DecompressionMethods.None : DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.None
            };

            ResetState();
        }

        public HttpNew()
        {
            this._handler = new HttpClientHandler()
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                MaxConnectionsPerServer = this.ConnectionMax
            };

            ResetState();
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

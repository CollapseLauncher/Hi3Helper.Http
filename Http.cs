using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class Http : HttpClient
    {
        public Http(bool IgnoreCompress = false, uint MaxRetry = 5, uint RetryInterval = 1000) : base(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseCookies = true,
            MaxConnectionsPerServer = 8,
            AutomaticDecompression = IgnoreCompress ? DecompressionMethods.None : DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.None
        })
        {
            this.MaxRetry = MaxRetry;
            this.RetryInterval = RetryInterval;
        }

        public Http() : base(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseCookies = true,
            MaxConnectionsPerServer = 8,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.None
        })
        { }

        public async Task Download(string URL, string OutPath,
            CancellationToken Token = new CancellationToken(), long Start = 0, long? End = null)
        {
            ResetAttributes();
            SessionAttribute Session = new SessionAttribute(URL, OutPath, null, Token, Start, End);
            await TryAwaitOrDisposeStreamWhileFail(StartRetryableTask(Session), Session);
        }

        public async void DownloadNoTask(string URL, string OutPath,
            CancellationToken Token = new CancellationToken(), long Start = 0, long? End = null)
            => await Download(URL, OutPath, Token, Start, End);

        public async Task DownloadStream(string URL, Stream OutStream, CancellationToken Token = new CancellationToken(),
            long Start = 0, long? End = null)
        {
            ResetAttributes();
            SessionAttribute Session = new SessionAttribute(URL, null, OutStream, Token, Start, End);
            await TryAwaitOrDisposeStreamWhileFail(StartRetryableTask(Session), Session);
        }

        public async void DownloadStreamNoTask(string URL, Stream OutStream, CancellationToken Token = new CancellationToken(),
            long Start = 0, long? End = null)
            => await DownloadStream(URL, OutStream, Token, Start, End);
    }
}

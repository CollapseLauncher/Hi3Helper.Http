using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http.Legacy
{
    public sealed partial class Http : IDisposable
    {
        public Http(bool ignoreCompress = true, byte retryMax = 5, short retryInterval = 1000, string? userAgent = null,
                    HttpClient? customHttpClient = null)
        {
            _retryMax = retryMax;
            _retryInterval = retryInterval;
            DownloadState = DownloadState.Idle;
            _sessionsStopwatch = Stopwatch.StartNew();
            _sizeAttribute = new AttributesSize();
            _clientUserAgent = userAgent!;
            _ignoreHttpCompression = ignoreCompress;

            if (customHttpClient != null)
            {
                _client = customHttpClient;
                return;
            }

            _handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                MaxConnectionsPerServer = ConnectionMax,
                AutomaticDecompression = _ignoreHttpCompression ? DecompressionMethods.None : DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.None,
            };

            _client = new HttpClient(_handler)
            {
                Timeout = TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec)
#if NET6_0_OR_GREATER
                ,
                DefaultRequestVersion = HttpVersion.Version30,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
#endif
            };

            if (!string.IsNullOrEmpty(_clientUserAgent))
                _client.DefaultRequestHeaders.UserAgent.ParseAdd(_clientUserAgent);
        }
#nullable restore


        public Http()
        {
            DownloadState = DownloadState.Idle;
            _sessionsStopwatch = Stopwatch.StartNew();
            _sizeAttribute = new AttributesSize();

            _ignoreHttpCompression = true;
            _handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                MaxConnectionsPerServer = ConnectionMax,
                AutomaticDecompression = DecompressionMethods.None
            };

            _client = new HttpClient(_handler)
            {
                Timeout = TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec)
#if NET6_0_OR_GREATER
                ,
                DefaultRequestVersion = HttpVersion.Version30,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
#endif
            };
        }

#nullable enable
        public Http(HttpClient? customHttpClient = null)
        {
            DownloadState = DownloadState.Idle;
            _sessionsStopwatch = Stopwatch.StartNew();
            _sizeAttribute = new AttributesSize();

            if (customHttpClient != null)
            {
                _client = customHttpClient;
                return;
            }

            _ignoreHttpCompression = true;
            _handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                MaxConnectionsPerServer = ConnectionMax,
                AutomaticDecompression = DecompressionMethods.None
            };

            _client = new HttpClient(_handler)
            {
                Timeout = TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec)
#if NET6_0_OR_GREATER
                ,
                DefaultRequestVersion = HttpVersion.Version30,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
#endif
            };
        }
#nullable restore

        public HttpClient GetHttpClient() => _client;

        public async Task Download(string            url,
                                   string            output,
                                   bool              overwrite,
                                   long?             offsetStart = null,
                                   long?             offsetEnd   = null,
                                   CancellationToken threadToken = default)
        {
            ResetState();

            _pathURL = url;
            _pathOutput = output;

            await SessionTaskRunnerContainer(await InitializeSingleSession(offsetStart, offsetEnd, output, overwrite, null, false, threadToken), threadToken);

            DownloadState = DownloadState.Finished;
        }

        public async Task Download(string            url,
                                   Stream            outStream,
                                   long?             offsetStart           = null,
                                   long?             offsetEnd             = null,
                                   bool              ignoreOutStreamLength = false,
                                   CancellationToken threadToken           = default)
        {
            ResetState();

            _pathURL = url;

            await SessionTaskRunnerContainer(await InitializeSingleSession(offsetStart, offsetEnd, null, false, outStream, ignoreOutStreamLength, threadToken), threadToken);
            DownloadState = DownloadState.Finished;
        }

        public void Dispose()
        {
            FinalizeDispose();
        }

        private void FinalizeDispose()
        {
            _handler = null!;

            _client.Dispose();

            GC.SuppressFinalize(this);
        }

        ~Http() => Dispose();
    }
}
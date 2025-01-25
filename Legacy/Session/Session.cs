using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http.Legacy
{
    public class HttpResponseInputStream : Stream
    {
        private protected HttpRequestMessage  NetworkRequest  = null!;
        private protected HttpResponseMessage NetworkResponse = null!;
        private protected Stream              NetworkStream   = null!;
        private protected long                NetworkLength;
        private protected long                CurrentPosition;
        public            HttpStatusCode      StatusCode;
        public            bool                IsSuccessStatusCode;

        public static async Task<HttpResponseInputStream> CreateStreamAsync(HttpClient client, string url, long? startOffset, long? endOffset, CancellationToken token)
        {
            startOffset ??= 0;

            HttpResponseInputStream httpResponseInputStream = new HttpResponseInputStream();
            httpResponseInputStream.NetworkRequest = new HttpRequestMessage
            {
                RequestUri = new Uri(url),
                Method = HttpMethod.Get
            };

            token.ThrowIfCancellationRequested();

            httpResponseInputStream.NetworkRequest.Headers.Range = new RangeHeaderValue(startOffset, endOffset);
            httpResponseInputStream.NetworkResponse = await client
                .SendAsync(httpResponseInputStream.NetworkRequest, HttpCompletionOption.ResponseHeadersRead, token);

            httpResponseInputStream.StatusCode = httpResponseInputStream.NetworkResponse.StatusCode;
            httpResponseInputStream.IsSuccessStatusCode = httpResponseInputStream.NetworkResponse.IsSuccessStatusCode;
            if (httpResponseInputStream.IsSuccessStatusCode)
            {
                httpResponseInputStream.NetworkLength = httpResponseInputStream.NetworkResponse.Content.Headers.ContentLength ?? 0;
                httpResponseInputStream.NetworkStream = await httpResponseInputStream.NetworkResponse.Content
#if NET6_0_OR_GREATER
                    .ReadAsStreamAsync(token);
#else
                    .ReadAsStreamAsync();
#endif
                return httpResponseInputStream;
            }

            if ((int)httpResponseInputStream.StatusCode != 416)
            {
                throw new
                    HttpRequestException(string.Format("HttpResponse for URL: \"{1}\" has returned unsuccessful code: {0}",
                                                       httpResponseInputStream.NetworkResponse.StatusCode, url));
            }

        #if NET6_0_OR_GREATER
            await httpResponseInputStream.DisposeAsync();
        #else
            httpResponseInputStream.Dispose();
        #endif
            throw new HttpRequestException("Http request returned 416!");
        }

        ~HttpResponseInputStream() => Dispose();


#if NET6_0_OR_GREATER
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await NetworkStream.ReadAsync(buffer, cancellationToken);
            CurrentPosition += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            int read = NetworkStream.Read(buffer);
            CurrentPosition += read;
            return read;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();
#endif

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int read = await NetworkStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            CurrentPosition += read;
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = NetworkStream.Read(buffer, offset, count);
            CurrentPosition += read;
            return read;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void Flush()
        {
            if (IsSuccessStatusCode)
            {
                NetworkStream.Flush();
            }
        }

        public override long Length
        {
            get { return NetworkLength; }
        }

        public override long Position
        {
            get { return CurrentPosition; }
            set { throw new NotSupportedException(); }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
            {
                return;
            }

            NetworkRequest.Dispose();
            NetworkResponse.Dispose();

            if (IsSuccessStatusCode)
            {
                NetworkStream.Dispose();
            }
        }

#if NET6_0_OR_GREATER
        public override async ValueTask DisposeAsync()
        {
            NetworkRequest.Dispose();
            NetworkResponse.Dispose();
            if (NetworkStream != null)
                await NetworkStream.DisposeAsync();

            await base.DisposeAsync();
            GC.SuppressFinalize(this);
        }
#endif
    }

    internal sealed class Session : IDisposable
#if NET6_0_OR_GREATER
        , IAsyncDisposable
#endif
    {
        #nullable enable
        internal Session(string pathURL,
                         HttpClientHandler clientHandler,
                         long? offsetStart = null,
                         long? offsetEnd = null, 
                         string? userAgent = null, 
                         bool useExternalSessionClient = false,
                         HttpClient? externalSessionClient = null)
        {
            // Initialize Properties
            PathURL = pathURL;
            IsDisposed = false;
            IsUseExternalSession = useExternalSessionClient;
            SessionState = DownloadState.Idle;
            
            if (useExternalSessionClient && externalSessionClient == null)
                throw new HttpHelperSessionNotReady("External session is null!");
            
            SessionClient = useExternalSessionClient ? externalSessionClient! : 
                new HttpClient(clientHandler)
                {
                    Timeout = TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec)
        #if NET6_0_OR_GREATER
                    ,
                    DefaultRequestVersion = HttpVersion.Version30,
                    DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        #endif
                };
            SessionID = 0;

            if (!useExternalSessionClient && userAgent != null)
            {
                SessionClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            }

            OffsetStart = offsetStart;
            OffsetEnd = offsetEnd;
        }
        #nullable restore

        // Seek the StreamOutput to the end of file
        // ReSharper disable once MemberCanBePrivate.Global
        internal void SeekStreamOutputToEnd() => StreamOutput.Seek(0, SeekOrigin.End);

        internal async Task AssignOutputStreamFromFile(bool isOverwrite, string filePath, bool ignoreOutStreamLength)
        {
            IsFileMode = true;
            FileInfo fileInfo = new FileInfo(filePath);
            if (isOverwrite)
                StreamOutput = await Http.NaivelyOpenFileStreamAsync(fileInfo, FileMode.Create, FileAccess.Write, FileShare.Write);
            else
            {
                StreamOutput = await Http.NaivelyOpenFileStreamAsync(fileInfo, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write);
            }

            SeekStreamOutputToEnd();
            AdjustOffsets(OffsetStart, OffsetEnd, ignoreOutStreamLength);
        }

        internal void AssignOutputStreamFromStream(Stream stream, bool ignoreOutStreamLength)
        {
            ArgumentNullException.ThrowIfNull(stream);
            StreamOutput = stream;
            IsFileMode = false;

            AdjustOffsets(OffsetStart, OffsetEnd, ignoreOutStreamLength);
        }

        private void AdjustOffsets(long? start, long? end, bool ignoreOutStreamLength = false)
        {
            OffsetStart = (start ?? 0) + (ignoreOutStreamLength ? 0 : StreamOutputSize);
            OffsetEnd = end;
        }

#if NET6_0_OR_GREATER
        internal async ValueTask<Tuple<bool, Exception>> TryReinitializeRequest(CancellationToken token)
#else
        internal async Task<Tuple<bool, Exception>> TryReinitializeRequest(CancellationToken token)
#endif
        {
            try
            {
                if (StreamInput != null)
#if NET6_0_OR_GREATER
                    await StreamInput.DisposeAsync();
#else
                    this.StreamInput.Dispose();
#endif

                return new Tuple<bool, Exception>(await TryGetHttpRequest(token), null!);
            }
            catch (Exception ex)
            {
                Http.PushLog($"Failed while reinitialize session ID: {SessionID}\r\n{ex}", DownloadLogSeverity.Error);
                return new Tuple<bool, Exception>(false, ex);
            }
        }

        internal async
#if NETCOREAPP
        ValueTask<bool>
#else
        Task<bool>
#endif
        TryGetHttpRequest(CancellationToken token)
        {
            if (IsExistingFileSizeValid())
            {
                ActionTimeoutValueTaskCallback<HttpResponseInputStream> createStreamCallback =
                    async (innerToken) =>
                        await HttpResponseInputStream.CreateStreamAsync(SessionClient, PathURL,
                                                                        OffsetStart, OffsetEnd, innerToken);

                StreamInput = await TaskExtensions.WaitForRetryAsync(() => createStreamCallback, fromToken: token);
                return StreamInput != null;
            }

            return false;
        }

        internal bool IsExistingFileOversize(long offsetStart, long offsetEnd) => StreamOutputSize > offsetEnd + 1 - offsetStart;

        private bool IsExistingFileSizeValid() =>
            !((IsLastSession ? OffsetEnd - 1 : OffsetEnd) - OffsetStart < 0
           && (IsLastSession ? OffsetEnd - 1 : OffsetEnd) - OffsetStart == -1);

        // Implement Disposable for IDisposable
        ~Session()
        {
            if (IsDisposed) return;

            Dispose();
        }

#if NET6_0_OR_GREATER
        public async ValueTask DisposeAsync()
        {
            if (IsDisposed) return;

            try
            {
                if (IsFileMode && StreamOutput != null) await StreamOutput.DisposeAsync();
                if (StreamInput != null) await StreamInput.DisposeAsync();
            }
            catch (Exception ex)
            {
                Http.PushLog($"Exception while disposing session: {ex}", DownloadLogSeverity.Warning);
            }

            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
#endif

        public void Dispose()
        {
            if (IsDisposed) return;

            try
            {
                if (IsFileMode)
                    StreamOutput.Dispose();

                StreamInput.Dispose();
                if (IsUseExternalSession) SessionClient.Dispose();
            }
            catch (Exception ex)
            {
                Http.PushLog($"Exception while disposing session: {ex}", DownloadLogSeverity.Warning);
            }

            IsDisposed = true;
            GC.SuppressFinalize(this);
        }

        // Session Offset Properties
        internal long? OffsetStart;
        internal long? OffsetEnd;

        // Path Properties
        internal string PathURL;

        // Boolean Properties
        internal bool IsUseExternalSession;
        internal bool IsLastSession;
        internal bool IsFileMode;
        internal bool IsDisposed;

        // Session Properties
        internal HttpClient    SessionClient;
        internal DownloadState SessionState;
        internal int           SessionRetryAttempt { get; set; }
        internal long          SessionID;

        // Stream Properties
        internal HttpResponseInputStream StreamInput  = null!;
        internal Stream                  StreamOutput = null!;
        internal long                    StreamOutputSize => StreamOutput.CanWrite || StreamOutput.CanRead ? StreamOutput.Length : 0;
    }
}

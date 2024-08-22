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
        private protected HttpRequestMessage _networkRequest;
        private protected HttpResponseMessage _networkResponse;
        private protected Stream _networkStream;
        private protected long _networkLength;
        private protected long _currentPosition;
        public HttpStatusCode _statusCode;
        public bool _isSuccessStatusCode;

        public static async Task<HttpResponseInputStream> CreateStreamAsync(HttpClient client, string url, long? startOffset, long? endOffset, CancellationToken token)
        {
            if (startOffset == null)
                startOffset = 0;

            HttpResponseInputStream httpResponseInputStream = new HttpResponseInputStream();
            httpResponseInputStream._networkRequest = new HttpRequestMessage()
            {
                RequestUri = new Uri(url),
                Method = HttpMethod.Get
            };

            token.ThrowIfCancellationRequested();

            httpResponseInputStream._networkRequest.Headers.Range = new RangeHeaderValue(startOffset, endOffset);
            httpResponseInputStream._networkResponse = await client
                .SendAsync(httpResponseInputStream._networkRequest, HttpCompletionOption.ResponseHeadersRead, token);

            httpResponseInputStream._statusCode = httpResponseInputStream._networkResponse.StatusCode;
            httpResponseInputStream._isSuccessStatusCode = httpResponseInputStream._networkResponse.IsSuccessStatusCode;
            if (httpResponseInputStream._isSuccessStatusCode)
            {
                httpResponseInputStream._networkLength = httpResponseInputStream._networkResponse.Content.Headers.ContentLength ?? 0;
                httpResponseInputStream._networkStream = await httpResponseInputStream._networkResponse.Content
#if NET6_0_OR_GREATER
                    .ReadAsStreamAsync(token);
#else
                    .ReadAsStreamAsync();
#endif
                return httpResponseInputStream;
            }

            if ((int)httpResponseInputStream._statusCode == 416)
            {
#if NET6_0_OR_GREATER
                await httpResponseInputStream.DisposeAsync();
#else
                httpResponseInputStream.Dispose();
#endif
                return null;
            }

            throw new HttpRequestException(string.Format("HttpResponse for URL: \"{1}\" has returned unsuccessful code: {0}", httpResponseInputStream._networkResponse.StatusCode, url));
        }

        ~HttpResponseInputStream() => Dispose();


#if NET6_0_OR_GREATER
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await _networkStream.ReadAsync(buffer, cancellationToken);
            _currentPosition += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            int read = _networkStream.Read(buffer);
            _currentPosition += read;
            return read;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();
#endif

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            int read = await _networkStream.ReadAsync(buffer, offset, count, cancellationToken);
            _currentPosition += read;
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _networkStream.Read(buffer, offset, count);
            _currentPosition += read;
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
            _networkStream.Flush();
        }

        public override long Length
        {
            get { return _networkLength; }
        }

        public override long Position
        {
            get { return _currentPosition; }
            set { throw new NotSupportedException(); }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _networkRequest?.Dispose();
                _networkResponse?.Dispose();
                _networkStream?.Dispose();
            }

            GC.SuppressFinalize(this);
        }

#if NET6_0_OR_GREATER
        public override async ValueTask DisposeAsync()
        {
            _networkRequest?.Dispose();
            _networkResponse?.Dispose();
            if (_networkStream != null)
                await _networkStream.DisposeAsync();

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
        internal Session(string PathURL,                          HttpClientHandler ClientHandler,
            long?               OffsetStart              = null,  long?             OffsetEnd             = null,
            string?             UserAgent                = null,
            bool                UseExternalSessionClient = false, HttpClient?       ExternalSessionClient = null,
            bool                IgnoreOutStreamLength    = false)
        {
            // Initialize Properties
            this.PathURL = PathURL;
            this.IsDisposed = false;
            this.IsUseExternalSession = UseExternalSessionClient;
            this.SessionState = DownloadState.Idle;
            this.SessionClient = UseExternalSessionClient ? ExternalSessionClient : new HttpClient(ClientHandler)
            {
                Timeout = TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec)
#if NET6_0_OR_GREATER
                ,
                DefaultRequestVersion = HttpVersion.Version30,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
#endif
            };
            this.SessionID = 0;

            if (!UseExternalSessionClient && UserAgent != null)
            {
                this.SessionClient?.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            }

            this.OffsetStart = OffsetStart;
            this.OffsetEnd = OffsetEnd;
        }
        #nullable restore

        // Seek the StreamOutput to the end of file
        internal void SeekStreamOutputToEnd() => this.StreamOutput?.Seek(0, SeekOrigin.End);

        internal async Task AssignOutputStreamFromFile(bool isOverwrite, string filePath, bool IgnoreOutStreamLength)
        {
            this.IsFileMode = true;
            FileInfo fileInfo = new FileInfo(filePath);
            if (isOverwrite)
                this.StreamOutput = await Http.NaivelyOpenFileStreamAsync(fileInfo, FileMode.Create, FileAccess.Write, FileShare.Write);
            else
            {
                this.StreamOutput = await Http.NaivelyOpenFileStreamAsync(fileInfo, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write);
            }

            SeekStreamOutputToEnd();
            AdjustOffsets(OffsetStart, OffsetEnd, IgnoreOutStreamLength);
        }

        internal void AssignOutputStreamFromStream(Stream stream, bool IgnoreOutStreamLength)
        {
            ArgumentNullException.ThrowIfNull(stream);
            this.StreamOutput = stream;
            this.IsFileMode = false;

            AdjustOffsets(OffsetStart, OffsetEnd, IgnoreOutStreamLength);
        }

        private void AdjustOffsets(long? Start, long? End, bool IgnoreOutStreamLength = false)
        {
            this.OffsetStart = (Start ?? 0) + (IgnoreOutStreamLength ? 0 : this.StreamOutputSize);
            this.OffsetEnd = End;
        }

#if NET6_0_OR_GREATER
        internal async ValueTask<Tuple<bool, Exception>> TryReinitializeRequest(CancellationToken token)
#else
        internal async Task<Tuple<bool, Exception>> TryReinitializeRequest(CancellationToken token)
#endif
        {
            try
            {
                if (this.StreamInput != null)
#if NET6_0_OR_GREATER
                    await this.StreamInput.DisposeAsync();
#else
                    this.StreamInput.Dispose();
#endif

                return new Tuple<bool, Exception>(await TryGetHttpRequest(token), null);
            }
            catch (Exception ex)
            {
                Http.PushLog($"Failed while reinitialize session ID: {this.SessionID}\r\n{ex}", DownloadLogSeverity.Error);
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
                        await HttpResponseInputStream.CreateStreamAsync(this.SessionClient, this.PathURL,
                                                                        this.OffsetStart, this.OffsetEnd, innerToken);

                this.StreamInput = await TaskExtensions.WaitForRetryAsync(() => createStreamCallback, fromToken: token);
                return this.StreamInput != null;
            }

            return false;
        }

        internal bool IsExistingFileOversized(long offsetStart, long offsetEnd) => this.StreamOutputSize > offsetEnd + 1 - offsetStart;

        private bool IsExistingFileSizeValid() =>
            !((this.IsLastSession ? this.OffsetEnd - 1 : this.OffsetEnd) - this.OffsetStart < 0
           && (this.IsLastSession ? this.OffsetEnd - 1 : this.OffsetEnd) - this.OffsetStart == -1);

        // Implement Disposable for IDisposable
        ~Session()
        {
            if (this.IsDisposed) return;

            Dispose();
        }

#if NET6_0_OR_GREATER
        public async ValueTask DisposeAsync()
        {
            if (this.IsDisposed) return;

            try
            {
                if (this.IsFileMode && this.StreamOutput != null) await this.StreamOutput.DisposeAsync();
                if (this.StreamInput != null) await this.StreamInput.DisposeAsync();
            }
            catch (Exception ex)
            {
                Http.PushLog($"Exception while disposing session: {ex}", DownloadLogSeverity.Warning);
            }

            this.IsDisposed = true;
            GC.SuppressFinalize(this);
        }
#endif

        public void Dispose()
        {
            if (this.IsDisposed) return;

            try
            {
                if (this.IsFileMode)
                    this.StreamOutput?.Dispose();

                this.StreamInput?.Dispose();
                if (this.IsUseExternalSession) this.SessionClient?.Dispose();
            }
            catch (Exception ex)
            {
                Http.PushLog($"Exception while disposing session: {ex}", DownloadLogSeverity.Warning);
            }

            this.IsDisposed = true;
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
        internal HttpClient SessionClient;
        internal DownloadState SessionState;
        internal int SessionRetryAttempt { get; set; }
        internal long SessionID;

        // Stream Properties
        internal HttpResponseInputStream StreamInput;
        internal Stream StreamOutput;
        internal long StreamOutputSize => (this.StreamOutput?.CanWrite ?? false) || (this.StreamOutput?.CanRead ?? false) ? this.StreamOutput.Length : 0;
    }
}

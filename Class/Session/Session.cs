using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    internal class HttpResponseInputStream : Stream
    {
        private protected HttpRequestMessage _networkRequest;
        private protected HttpResponseMessage _networkResponse;
        private protected Stream _networkStream;
        private protected long _networkLength;
        private protected long _currentPosition = 0;
        internal protected HttpStatusCode _statusCode;
        internal protected bool _isSuccessStatusCode;

        internal static async Task<HttpResponseInputStream> CreateStreamAsync(HttpClient client, string url, long? startOffset, long? endOffset, CancellationToken token)
        {
            HttpResponseInputStream httpResponseInputStream = new HttpResponseInputStream();
            httpResponseInputStream._networkRequest = new HttpRequestMessage()
            {
                RequestUri = new Uri(url),
                Method = HttpMethod.Get
            };
            httpResponseInputStream._networkRequest.Headers.Range = new RangeHeaderValue(startOffset, endOffset);
            httpResponseInputStream._networkResponse = await client
                .SendAsync(httpResponseInputStream._networkRequest, HttpCompletionOption.ResponseHeadersRead, token);

            httpResponseInputStream._statusCode = httpResponseInputStream._networkResponse.StatusCode;
            httpResponseInputStream._isSuccessStatusCode = httpResponseInputStream._networkResponse.IsSuccessStatusCode;
            if (httpResponseInputStream._isSuccessStatusCode)
            {
                httpResponseInputStream._networkLength = httpResponseInputStream._networkResponse.Content.Headers.ContentLength ?? 0;
                httpResponseInputStream._networkStream = await httpResponseInputStream._networkResponse.Content
#if NETCOREAPP
                    .ReadAsStreamAsync(token);
#else
                    .ReadAsStreamAsync();
#endif
                return httpResponseInputStream;
            }

            if ((int)httpResponseInputStream._statusCode == 416)
            {
#if NETCOREAPP
                await httpResponseInputStream.DisposeAsync();
#else
                httpResponseInputStream.Dispose();
#endif
                return null;
            }

            throw new HttpRequestException(string.Format("HttpResponse for URL: \"{1}\" has returned unsuccessful code: {0}", httpResponseInputStream._networkResponse.StatusCode, url));
        }

        ~HttpResponseInputStream() => Dispose();


#if NETCOREAPP
        public int ReadUntilFull(Span<byte> buffer)
        {
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = _networkStream.Read(buffer.Slice(totalRead));
                if (read == 0) return totalRead;

                totalRead += read;
                _currentPosition += read;
            }
            return totalRead;
        }

        public async ValueTask<int> ReadUntilFullAsync(Memory<byte> buffer, CancellationToken token)
        {
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = await _networkStream.ReadAsync(buffer.Slice(totalRead), token);
                if (read == 0) return totalRead;

                totalRead += read;
                _currentPosition += read;
            }
            return totalRead;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => await ReadUntilFullAsync(buffer, cancellationToken);
        public override int Read(Span<byte> buffer) => ReadUntilFull(buffer);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();
#endif

        private async
#if NETCOREAPP
            ValueTask<int>
#else
            Task<int>
#endif
            ReadUntilFullAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            int totalRead = 0;
            while (offset < count)
            {
                int read = await _networkStream
#if NETCOREAPP
                    .ReadAsync(buffer.AsMemory(offset), token);
#else
                    .ReadAsync(buffer, offset, count - offset, token);
#endif
                if (read == 0) return totalRead;

                totalRead += read;
                offset += read;
                _currentPosition += read;
            }
            return totalRead;
        }

        private int ReadUntilFull(byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (offset < count)
            {
#if NETCOREAPP
                int read = _networkStream.Read(buffer.AsSpan(offset));
#else
                int read = _networkStream.Read(buffer, offset, count);
#endif
                if (read == 0) return totalRead;

                totalRead += read;
                offset += read;
                _currentPosition += read;
            }
            return totalRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default) => await ReadUntilFullAsync(buffer, offset, count, cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => ReadUntilFull(buffer, offset, count);
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

#if NETCOREAPP
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
#if NETCOREAPP
        , IAsyncDisposable
#endif
    {
        internal Session(string PathURL, string PathOutput, Stream SOutput,
            CancellationToken SToken, bool IsFileMode, HttpClientHandler ClientHandler,
            long? OffsetStart = null, long? OffsetEnd = null,
            bool Overwrite = false, string UserAgent = null,
            bool UseExternalSessionClient = false, bool IgnoreOutStreamLength = false)
        {
            // Initialize Properties
            this.PathURL = PathURL;
            this.PathOutput = PathOutput;
            this.StreamOutput = SOutput;
            this.SessionToken = SToken;
            this.IsFileMode = IsFileMode;
            this.IsDisposed = false;
            this.IsUseExternalSession = UseExternalSessionClient;
            this.SessionState = DownloadState.Idle;
            this.SessionClient = UseExternalSessionClient ? null : new HttpClient(ClientHandler)
            {
                Timeout = TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec)
#if NET7_0_OR_GREATER
                ,
                DefaultRequestVersion = HttpVersion.Version30,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
#endif
            };
            this.SessionID = 0;

            if (!UseExternalSessionClient && UserAgent != null)
            {
                this.SessionClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            }

            // If the OutStream is explicitly defined, use OutStream instead and set to IsFileMode == false.
            if (!(this.IsFileMode = SOutput == null))
            {
                this.StreamOutput = SOutput;
            }
            // Else, use file and set IsFileMode == true.
            else
            {
                this.StreamOutput = Overwrite ?
                      new FileStream(this.PathOutput, FileMode.Create, FileAccess.Write)
                    : new FileStream(this.PathOutput, FileMode.OpenOrCreate, FileAccess.Write);
            }

            AdjustOffsets(OffsetStart, OffsetEnd, IgnoreOutStreamLength);
        }

        // Seek the StreamOutput to the end of file
        internal void SeekStreamOutputToEnd() => this.StreamOutput.Seek(0, SeekOrigin.End);

        private void AdjustOffsets(long? Start, long? End, bool IgnoreOutStreamLength = false)
        {
            this.OffsetStart = (Start ?? 0) + (IgnoreOutStreamLength ? 0 : this.StreamOutputSize);
            this.OffsetEnd = End;
        }

#if NETCOREAPP
        internal async ValueTask<Tuple<bool, Exception>> TryReinitializeRequest()
#else
        internal async Task<Tuple<bool, Exception>> TryReinitializeRequest()
#endif
        {
            try
            {
                if (this.StreamInput != null)
#if NETCOREAPP
                    await this.StreamInput.DisposeAsync();
#else
                    this.StreamInput.Dispose();
#endif

                return new Tuple<bool, Exception>(await TryGetHttpRequest(), null);
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
        TryGetHttpRequest()
        {
            if (IsExistingFileSizeValid())
            {
                this.StreamInput = await TaskExtensions.RetryTimeoutAfter(
                    async () => await HttpResponseInputStream.CreateStreamAsync(this.SessionClient, this.PathURL, this.OffsetStart, this.OffsetEnd, this.SessionToken),
                    this.SessionToken
                    );
                return this.StreamInput != null;
            }

            return false;
        }

        internal bool IsExistingFileOversized(long OffsetStart, long OffsetEnd) => this.StreamOutputSize > OffsetEnd + 1 - OffsetStart;

        private bool IsExistingFileSizeValid() =>
            !((this.IsLastSession ? this.OffsetEnd - 1 : this.OffsetEnd) - this.OffsetStart < 0
           && (this.IsLastSession ? this.OffsetEnd - 1 : this.OffsetEnd) - this.OffsetStart == -1);

        // Implement Disposable for IDisposable
        ~Session()
        {
            if (this.IsDisposed) return;

            Dispose();
        }

#if NETCOREAPP
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
                if (this.IsFileMode && this.StreamOutput != null) this.StreamOutput.Dispose();
                if (this.StreamInput != null) this.StreamInput.Dispose();
                if (this.IsUseExternalSession) this.SessionClient.Dispose();
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
        internal string PathOutput;

        // Boolean Properties
        internal bool IsUseExternalSession;
        internal bool IsLastSession;
        internal bool IsFileMode;
        internal bool IsDisposed;

        // Session Properties
        internal HttpClient SessionClient;
        internal CancellationToken SessionToken;
        internal DownloadState SessionState;
        internal int SessionRetryAttempt { get; set; }
        internal long SessionID;

        // Stream Properties
        internal HttpResponseInputStream StreamInput;
        internal Stream StreamOutput;
        internal long StreamOutputSize => (this.StreamOutput?.CanWrite ?? false) || (this.StreamOutput?.CanRead ?? false) ? this.StreamOutput.Length : 0;
    }
}

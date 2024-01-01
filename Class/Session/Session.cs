using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public class HttpResponseInputStream : Stream
    {
        private protected HttpRequestMessage _networkRequest;
        private protected HttpResponseMessage _networkResponse;
        private protected Stream _networkStream;
        private protected long _networkLength;
        private protected long _currentPosition = 0;
        internal protected HttpStatusCode _statusCode;
        internal protected bool _isSuccessStatusCode;

        internal static async ValueTask<HttpResponseInputStream> CreateStreamAsync(HttpClient client, string url, long? startOffset, long? endOffset, CancellationToken token)
        {
            HttpResponseInputStream httpResponseInputStream = new HttpResponseInputStream();
            httpResponseInputStream._networkRequest = new HttpRequestMessage()
            {
                RequestUri = new Uri(url),
                Method = HttpMethod.Get
            };
            httpResponseInputStream._networkRequest.Headers.Range = new RangeHeaderValue(startOffset, endOffset);
            httpResponseInputStream._networkResponse = await client.SendAsync(httpResponseInputStream._networkRequest, HttpCompletionOption.ResponseHeadersRead, token);

            httpResponseInputStream._statusCode = httpResponseInputStream._networkResponse.StatusCode;
            httpResponseInputStream._isSuccessStatusCode = httpResponseInputStream._networkResponse.IsSuccessStatusCode;
            if (httpResponseInputStream._isSuccessStatusCode)
            {
                httpResponseInputStream._networkLength = httpResponseInputStream._networkResponse.Content.Headers.ContentLength ?? 0;
                httpResponseInputStream._networkStream = await httpResponseInputStream._networkResponse.Content.ReadAsStreamAsync(token);
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

        private int ReadUntilFull(byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (offset < count)
            {
                int read = _networkStream.Read(buffer.AsSpan(offset));
                if (read == 0) return totalRead;

                totalRead += read;
                offset += read;
                _currentPosition += read;
            }
            return totalRead;
        }
#endif

        public override int Read(byte[] buffer, int offset, int count) => ReadUntilFull(buffer, offset, count);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();


#if NETCOREAPP
        public override int Read(Span<byte> buffer) => ReadUntilFull(buffer);
        public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();
#endif

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

        public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();

        public override void SetLength(long value) => throw new NotImplementedException();

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

    public sealed class Session : IDisposable
#if NETCOREAPP
        , IAsyncDisposable
#endif
    {
        public Session(string PathURL, string PathOutput, Stream SOutput,
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
            this.SessionState = DownloadState.Idle;
            this.SessionClient = UseExternalSessionClient ? null : new HttpClient(ClientHandler);
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
        public void SeekStreamOutputToEnd() => this.StreamOutput.Seek(0, SeekOrigin.End);

        private void AdjustOffsets(long? Start, long? End, bool IgnoreOutStreamLength = false)
        {
            this.OffsetStart = (Start ?? 0) + (IgnoreOutStreamLength ? 0 : this.StreamOutputSize);
            this.OffsetEnd = End;
        }

#if NETCOREAPP
        public async ValueTask<bool> TryReinitializeRequest()
#else
        public async Task<bool> TryReinitializeRequest()
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

                return await TryGetHttpRequest();
            }
            catch (Exception ex)
            {
                Http.PushLog($"Failed while reinitialize session ID: {this.SessionID}\r\n{ex}", DownloadLogSeverity.Error);
                throw;
            }
        }

        public async ValueTask<bool> TryGetHttpRequest()
        {
            if (IsExistingFileSizeValid())
            {
                this.StreamInput = await HttpResponseInputStream.CreateStreamAsync(this.SessionClient, this.PathURL, this.OffsetStart, this.OffsetEnd, this.SessionToken);
                return this.StreamInput != null;
            }

            return false;
        }

        public bool IsExistingFileOversized(long OffsetStart, long OffsetEnd) => this.StreamOutputSize > OffsetEnd + 1 - OffsetStart;

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
            }
            catch (Exception ex)
            {
                Http.PushLog($"Exception while disposing session: {ex}", DownloadLogSeverity.Warning);
            }

            this.IsDisposed = true;
            GC.SuppressFinalize(this);
        }

        // Session Offset Properties
        public long? OffsetStart;
        public long? OffsetEnd;

        // Path Properties
        public string PathURL;
        public string PathOutput;

        // Boolean Properties
        public bool IsLastSession;
        public bool IsFileMode;
        public bool IsDisposed;

        // Session Properties
        public HttpClient SessionClient;
        public CancellationToken SessionToken;
        public DownloadState SessionState;
        public int SessionRetryAttempt;
        public long SessionID;

        // Stream Properties
        public HttpResponseInputStream StreamInput;
        public Stream StreamOutput;
        public long StreamOutputSize => (this.StreamOutput?.CanWrite ?? false) || (this.StreamOutput?.CanRead ?? false) ? this.StreamOutput.Length : 0;
    }
}

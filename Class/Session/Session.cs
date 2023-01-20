﻿using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
#if !NETCOREAPP
using System.Threading.Tasks;
#endif

namespace Hi3Helper.Http
{
    public sealed class Session : IDisposable
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
            // this.Checksum = new SimpleChecksum();
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
        public void TryReinitializeRequest()
        {
            try
            {
                this.StreamInput?.Dispose();
                this.SessionRequest?.Dispose();
                this.SessionResponse?.Dispose();

                TrySetHttpRequest();
                TrySetHttpRequestOffset();
                TrySetHttpResponse();
            }
            catch (Exception) { throw; }
        }

        public bool TrySetHttpResponse()
        {
            HttpResponseMessage Input = this.SessionClient.Send(this.SessionRequest, HttpCompletionOption.ResponseHeadersRead, this.SessionToken);
            if (Input.IsSuccessStatusCode)
            {
                this.SessionResponse = Input;
                return true;
            }

            if ((int)Input.StatusCode == 416) return false;

            throw new HttpRequestException(string.Format("HttpResponse has returned unsuccessful code: {0}", Input.StatusCode));
        }
#else
        public async Task TryReinitializeRequest()
        {
            try
            {
                this.IsDisposed = false;
                this.StreamInput?.Dispose();
                this.SessionRequest?.Dispose();
                this.SessionResponse?.Dispose();

                TrySetHttpRequest();
                TrySetHttpRequestOffset();
                await TrySetHttpResponse();
            }
            catch (Exception ex) { throw ex; }
        }

        public async Task<bool> TrySetHttpResponse()
        {
            HttpResponseMessage Input = await this.SessionClient.SendAsync(this.SessionRequest, HttpCompletionOption.ResponseHeadersRead, this.SessionToken);
            if (Input.IsSuccessStatusCode)
            {
                this.SessionResponse = Input;
                return true;
            }

            if ((int)Input.StatusCode == 416) return false;

            throw new HttpRequestException(string.Format("HttpResponse has returned unsuccessful code: {0}", Input.StatusCode));
        }
#endif

        public bool TrySetHttpRequest()
        {
            this.SessionRequest = new HttpRequestMessage()
            {
                RequestUri = new Uri(this.PathURL),
                Method = HttpMethod.Get
            };

            return IsExistingFileSizeValid();
        }

        public bool IsExistingFileOversized(long OffsetStart, long OffsetEnd) => this.StreamOutputSize > OffsetEnd + 1 - OffsetStart;

        private bool IsExistingFileSizeValid() =>
            !((this.IsLastSession ? this.OffsetEnd - 1 : this.OffsetEnd) - this.OffsetStart < 0
           && (this.IsLastSession ? this.OffsetEnd - 1 : this.OffsetEnd) - this.OffsetStart == -1);

        public bool TrySetHttpRequestOffset()
        {
            try
            {
                this.SessionRequest.Headers.Range = new RangeHeaderValue(this.OffsetStart, this.OffsetEnd);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
            catch (Exception)
            {
                throw;
            }
        }

        // public void InjectLastHash(int Hash) => this.Checksum.InjectHash(Hash);
        // public void InjectLastHashPos(long Pos) => this.Checksum.InjectPos(Pos);

        // Implement Disposable for IDisposable
        ~Session()
        {
            if (this.IsDisposed) return;

            Dispose();
        }

        public void Dispose()
        {
            // this.Checksum = null;

            if (this.IsDisposed) return;

            if (this.IsFileMode) this.StreamOutput?.Dispose();

            this.StreamInput?.Dispose();
            this.SessionRequest?.Dispose();
            this.SessionResponse?.Dispose();

            this.IsDisposed = true;
        }

        // Checksum Properties
        // public SimpleChecksum Checksum { get; set; }
        // public int LastChecksumHash { get => this.Checksum.Hash32; }
        // public long LastChecksumPos { get => this.Checksum.LastChecksumPos; }

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
        public HttpRequestMessage SessionRequest;
        public HttpResponseMessage SessionResponse;
        public DownloadState SessionState;
        public int SessionRetryAttempt;
        public long SessionID;

        // Stream Properties
#if NETCOREAPP
        public Stream StreamInput => this.SessionResponse?.Content.ReadAsStream();
#else
        public Stream StreamInput => this.SessionResponse?.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
#endif
        public Stream StreamOutput;
        public long StreamOutputSize => (this.StreamOutput?.CanWrite ?? false) || (this.StreamOutput?.CanRead ?? false) ? this.StreamOutput.Length : 0;
    }
}

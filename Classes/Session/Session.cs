using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public class Session : IDisposable
    {
        public Session(string PathURL, string PathOutput, Stream SOutput,
            CancellationToken SToken, bool IsFileMode, bool IsMultiSession,
            long? OffsetStart = null, long? OffsetEnd = null, bool Overwrite = false)
        {
            // Initialize Properties
            this.PathURL = PathURL;
            this.PathOutput = PathOutput;
            this.StreamOutput = SOutput;
            this.SessionToken = SToken;
            this.IsFileMode = IsFileMode;
            this.IsMultiSession = IsMultiSession;
            this.SessionState = MultisessionState.Idle;
            this.Checksum = new SimpleChecksum();

            // If the OutStream is explicitly defined, use OutStream instead and set to IsFileMode == false.
            if (SOutput != null)
            {
                this.StreamOutput = SOutput;
                this.IsFileMode = false;
                AdjustOffsets(OffsetStart, OffsetEnd);
                return;
            }

            // Else, use file and set IsFileMode == true.
            this.FileOutput = new FileInfo(this.PathOutput);
            if (Overwrite)
                this.StreamOutput = this.FileOutput.Create();
            else
                this.StreamOutput = this.FileOutput.OpenWrite();
            this.IsFileMode = true;
            AdjustOffsets(OffsetStart, OffsetEnd);
        }

        // Deconstructor
        ~Session() => Dispose();

        // Seek the StreamOutput to the end of file
        public void SeekStreamOutputToEnd() => this.StreamOutput.Seek(0, SeekOrigin.End);

        private void AdjustOffsets(long? Start, long? End)
        {
            this.OffsetStart = (Start ?? 0) + this.StreamOutputSize;
            this.OffsetEnd = End;
        }

        public bool TrySetHttpRequest()
        {
            this.SessionRequest = new HttpRequestMessage()
            {
                RequestUri = new Uri(this.PathURL),
                Method = HttpMethod.Get
            };

            return IsExistingFileSizeValid();
        }

        public bool IsExistingFileOversized(long OffsetStart, long OffsetEnd)
        {
            return this.StreamOutputSize > (this.IsLastSession ? OffsetEnd : OffsetEnd + 1) - OffsetStart;
        }

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
            catch (Exception) { throw; }
        }

        public async Task<bool> TrySetHttpResponse(HttpClient client)
        {
            try
            {
                HttpResponseMessage Input = await client.SendAsync(this.SessionRequest, HttpCompletionOption.ResponseHeadersRead, this.SessionToken);
                if (Input.IsSuccessStatusCode)
                {
                    this.SessionResponse = Input;
                    return true;
                }

                if ((int)Input.StatusCode == 416) return false;

                throw new HttpRequestException(string.Format("HttpResponse has returned unsuccessful code: {0}", Input.StatusCode));
            }
            catch (HttpRequestException) { throw; }
            catch (Exception) { return false; }
        }

        public void InjectLastHash(int Hash) => this.Checksum.InjectHash(Hash);
        public void InjectLastHashPos(long Pos) => this.Checksum.InjectPos(Pos);

        // Implement Disposable for IDisposable
        public void Dispose()
        {
            this.Checksum = null;
            this.StreamInput?.Dispose();
            this.SessionRequest?.Dispose();
            this.SessionResponse?.Dispose();

            if (this.IsFileMode)
                this.StreamOutput?.Dispose();
        }

        // Checksum Properties
        public SimpleChecksum Checksum { get; set; }
        public int LastChecksumHash { get => this.Checksum.Hash32; }
        public long LastChecksumPos { get => this.Checksum.LastChecksumPos; }

        // Session Offset Properties
        public long? OffsetStart { get; private set; }
        public long? OffsetEnd { get; private set; }

        // Path Properties
        public string PathURL { get; private set; }
        public string PathOutput { get; private set; }

        // Boolean Properties
        public bool IsLastSession { get; set; }
        public bool IsMultiSession { get; set; }
        public bool IsFileMode { get; private set; }
        public bool IsCompleted { get { return StreamOutputSize >= SessionSize; } }

        // Session Properties
        public CancellationToken SessionToken { get; private set; }
        public HttpRequestMessage SessionRequest { get; set; }
        public HttpResponseMessage SessionResponse { get; set; }
        public MultisessionState SessionState { get; set; }
        public long SessionSize { get; set; }
        public int SessionRetryAttempt { get; set; }

        // Stream Properties
#if !NETSTANDARD
        public Stream StreamInput { get => this.SessionResponse?.Content.ReadAsStream(); }
#else
        public Stream StreamInput { get => this.SessionResponse.Content.ReadAsStreamAsync()
                    .GetAwaiter()
                    .GetResult(); }
#endif
        public FileInfo FileOutput { get; private set; }
        public Stream StreamOutput { get; private set; }
        public long StreamOutputSize => this.StreamOutput.CanWrite || this.StreamOutput.CanRead ? this.StreamOutput.Length : 0;
    }
}

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace Hi3Helper.Http
{
    public class MetadataProp
    {
        public long RemoteFileSize;
        public byte Sessions;
        public bool CanOverwrite;
    }

    public partial class Http
    {
        // Retry Attributes
        private uint MaxRetry = 5; // Default: 5 times
        private uint RetryInterval = 3500; // Default: 1000 ms

        // Session Attributes
        private long SizeDownloaded;
        private long SizeLastDownloaded;
        private long SizeToBeDownloaded;
        private Stopwatch SessionStopwatch;

        // Multisession Attributes
        private byte MaxAllowedSessions = 8; // Default: 8 Max sessions
        private byte Sessions;
        private ICollection<SessionAttribute> SessionAttributes;
        public MultisessionState SessionState;

        private void ResetAttributes()
        {
            ResetSessionStopwatch();
            SizeDownloaded = 0;
            SizeLastDownloaded = 0;
            SizeToBeDownloaded = 0;
            SessionState = MultisessionState.Idle;
            SessionAttributes = null;
        }

        public partial class SessionAttribute
        {
            public SessionAttribute(string InURL, string OutPath, Stream OutStream = null,
                CancellationToken SessionToken = new CancellationToken(),
                long? Start = null, long? End = null, bool Overwrite = false)
            {
                this.InURL = InURL;
                this.SessionToken = SessionToken;

                // If the OutStream is explicitly defined, use OutStream instead and set to IsOutDisposable == true.
                if (OutStream != null)
                {
                    this.OutStream = OutStream;
                    this.IsOutDisposable = false;
                    AdjustOffsets(Start, End);
                    return;
                }

                this.OutFile = new FileInfo(OutPath);
                if (Overwrite)
                    this.OutStream = this.OutFile.Create();
                else
                    this.OutStream = this.OutFile.OpenWrite();
                this.IsOutDisposable = true;
                AdjustOffsets(Start, End);
            }

            private void AdjustOffsets(long? Start, long? End)
            {
                this.StartOffset = (Start ?? 0) + this.OutStream.Length;
                this.EndOffset = End;
            }

            public HttpResponseMessage CheckHttpResponseCode(HttpResponseMessage Input)

            {
                if (Input.IsSuccessStatusCode)
                    return Input;

                throw new HttpRequestException(string.Format("HttpResponse has returned unsuccessful code: {0}", Input.StatusCode));
            }

            public CancellationToken SessionToken { get; private set; }
            public bool IsOutDisposable { get; private set; }
            public string InURL { get; private set; }
            public FileInfo OutFile { get; private set; }
            public Stream InStream
            {
                get => this.RemoteResponse.Content.ReadAsStreamAsync()
                    .GetAwaiter()
                    .GetResult();

            }

            public bool IsNeedRecalculateSize = true;
            public bool IsMultisession = false;
            public Stream OutStream { get; private set; }
            public HttpResponseMessage RemoteResponse { get; set; }
            public HttpRequestMessage RemoteRequest { get; set; }
            // Get InSize directly from InStream
            public long InSize { get => this.InStream.Length; }
            // Get OutSize directly from OutStream
            public long OutSize { get => this.OutStream.Length; }
            public long? StartOffset { get; set; }
            public long? EndOffset { get; set; }
            public byte SessionRetry = 1;

            // For Multisession mode only
            public bool IsLastSession { get; set; }
            public MultisessionState SessionState { get; set; } = MultisessionState.Idle;
        }
    }
}

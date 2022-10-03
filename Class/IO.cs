using System;
using System.IO;
using System.Threading;

namespace Hi3Helper.Http
{
    public partial class Http
    {
        public bool IOReadVerifyMulti(Session Input)
        {
            // Initialize local checksum
            SimpleChecksum Checksum = new SimpleChecksum();
            byte[] Buffer = new byte[_bufferSize];
            int Read;
            int NextRead = Buffer.Length;

            // Set Session State to verification check
            Input.SessionState = MultisessionState.CheckingLastSessionIntegrity;

            // Set Output Stream position to beginning
            Input.StreamOutput.Position = 0;
            // Read Stream into Buffer
            while ((Read = Input.StreamOutput.Read(Buffer, 0, NextRead)) > 0)
            {
                // Calculate Next Read Jump
                NextRead = (int)Math.Min(Buffer.Length, Input.LastChecksumPos - Input.StreamOutput.Position);
                // Compute checksum from Buffer
                Checksum.ComputeHash32(Buffer, Read);
                // Throw if Token Cancellation is requested
                Input.SessionToken.ThrowIfCancellationRequested();
            }

            Input.SessionState = MultisessionState.CompleteLastSessionIntegrity;

            return Checksum.Hash32 == Input.LastChecksumHash;
        }

        public void IOReadWrite(Stream Input, Stream Output, int BufferSize, CancellationToken Token)
        {
            DownloadEvent Event = new DownloadEvent();
            byte[] Buffer = new byte[BufferSize];
            int Read;

            // Read Stream into Buffer
            while ((Read = Input.Read(Buffer, 0, Buffer.Length)) > 0)
            {
                // Write Buffer to the output Stream
                Output.Write(Buffer, 0, Read);
                // Throw if Token Cancellation is requested
                Token.ThrowIfCancellationRequested();

                // Increment SizeDownloaded attribute
                this.SizeAttribute.SizeDownloaded += Read;
                this.SizeAttribute.SizeDownloadedLast += Read;

                // Update state
                Event.UpdateDownloadEvent(
                        this.SizeAttribute.SizeDownloadedLast,
                        this.SizeAttribute.SizeDownloaded,
                        this.SizeAttribute.SizeTotalToDownload,
                        Read,
                        this.SessionsStopwatch.Elapsed.TotalSeconds,
                        this.DownloadState
                        );
                this.UpdateProgress(Event);
            }
        }

        public void IOReadWriteSession(Session Input, CancellationToken InnerToken)
        {
            DownloadEvent Event = new DownloadEvent();
            byte[] Buffer = new byte[_bufferSize];
            int Read;

            // Read Stream into Buffer
            while ((Read = Input.StreamInput.Read(Buffer, 0, Buffer.Length)) > 0)
            {
                // Write Buffer to the output Stream
                Input.StreamOutput.Write(Buffer, 0, Read);
                // Compute checksum from Buffer
                // Input.Checksum.ComputeHash32(Buffer, Read);
                // Set Inner Session Status
                Input.SessionState = MultisessionState.Downloading;
                // Throw if Token Cancellation is requested
                Input.SessionToken.ThrowIfCancellationRequested();
                // Throw if Inner Token Cancellation is requested
                InnerToken.ThrowIfCancellationRequested();
                // Reset session retry attempt
                Input.SessionRetryAttempt = 1;

                // Lock SizeAttribute to avoid race condition while updating status
                lock (this.SizeAttribute)
                {
                    // Increment SizeDownloaded attribute
                    this.SizeAttribute.SizeDownloaded += Read;
                    this.SizeAttribute.SizeDownloadedLast += Read;

                    // Update download state
                    Event.UpdateDownloadEvent(
                            this.SizeAttribute.SizeDownloadedLast,
                            this.SizeAttribute.SizeDownloaded,
                            this.SizeAttribute.SizeTotalToDownload,
                            Read,
                            this.SessionsStopwatch.Elapsed.TotalSeconds,
                            this.DownloadState
                            );
                    this.UpdateProgress(Event);
                }
            }
        }
    }
}

using System;
using System.IO;
using System.Threading;

namespace Hi3Helper.Http
{
    public partial class Http
    {
        /*
        public bool IOReadVerifyMulti(Session Input)
        {
            // Initialize local checksum
            SimpleChecksum Checksum = new SimpleChecksum();
            int Read;
            int NextRead = _buffer.Length;

            // Set Session State to verification check
            Input.SessionState = MultisessionState.CheckingLastSessionIntegrity;

            // Set Output Stream position to beginning
            Input.StreamOutput.Position = 0;
            // Read Stream into Buffer
            while ((Read = Input.StreamOutput.Read(_buffer, 0, NextRead)) > 0)
            {
                // Calculate Next Read Jump
                NextRead = (int)Math.Min(_buffer.Length, Input.LastChecksumPos - Input.StreamOutput.Position);
                // Compute checksum from Buffer
                Checksum.ComputeHash32(_buffer, Read);
                // Throw if Token Cancellation is requested
                Input.SessionToken.ThrowIfCancellationRequested();
            }

            Input.SessionState = MultisessionState.CompleteLastSessionIntegrity;

            return Checksum.Hash32 == Input.LastChecksumHash;
        }
        */

        public void IOReadWrite(Stream Input, Stream Output, CancellationToken Token)
        {
            DownloadEvent Event = new DownloadEvent();
            int Read;

            // Read Stream into Buffer
            while ((Read = Input.Read(_buffer, 0, _buffer.Length)) > 0)
            {
                // Write Buffer to the output Stream
                Output.Write(_buffer, 0, Read);
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
            int Read;

            // Read Stream into Buffer
            while ((Read = Input.StreamInput.Read(_buffer, 0, _buffer.Length)) > 0)
            {
                // Write Buffer to the output Stream
                Input.StreamOutput.Write(_buffer, 0, Read);
                // Increment as last OffsetStart adjusted
                Input.OffsetStart += Read;
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

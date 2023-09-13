using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public sealed partial class Http
    {
        private async Task IOReadWrite(Stream Input, Stream Output, CancellationToken Token)
        {
            DownloadEvent Event = new DownloadEvent();
            int Read;
            byte[] Buffer = new byte[_bufferSize];

            // Read Stream into Buffer
            while ((Read = await Input.ReadAsync(Buffer, 0, _bufferSize, Token)) > 0)
            {
                // Write Buffer to the output Stream
                await Output.WriteAsync(Buffer, 0, Read, Token);

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

        private async Task IOReadWriteSession(Session Input)
        {
            DownloadEvent Event = new DownloadEvent();
            int Read;
            byte[] Buffer = new byte[_bufferSize];

            // Read Stream into Buffer
#if NET7_0_OR_GREATER
            while ((Read = await Input.StreamInput.ReadAtLeastAsync(Buffer, _bufferSize, false, Input.SessionToken)) > 0)
#else
            while ((Read = await Input.StreamInput.ReadAsync(Buffer, 0, _bufferSize, Input.SessionToken)) > 0)
#endif
            {
                // Write Buffer to the output Stream
                await Input.StreamOutput.WriteAsync(Buffer, 0, Read, Input.SessionToken);
                // Increment as last OffsetStart adjusted
                Input.OffsetStart += Read;
                // Set Inner Session Status
                Input.SessionState = DownloadState.Downloading;
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

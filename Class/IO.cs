using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public sealed partial class Http
    {
#if NETCOREAPP
        private async ValueTask IOReadWriteAsync(Stream Input, Stream Output, CancellationToken Token)
        {
            DownloadEvent Event = new DownloadEvent();
            int Read;
            Memory<byte> Buffer = new byte[_bufferSizeMerge];

            // Read Stream into Buffer
            while ((Read = await Input.ReadAsync(Buffer, Token)) > 0)
            {
                // Write Buffer to the output Stream
                await Output.WriteAsync(Buffer.Slice(0, Read), Token);

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
#endif

        private void IOReadWrite(Stream Input, Stream Output, CancellationToken Token)
        {
            DownloadEvent Event = new DownloadEvent();
            int Read;
            byte[] Buffer = new byte[_bufferSizeMerge];

#if NETCOREAPP
            // Read Stream into Buffer
            while ((Read = Input.Read(Buffer)) > 0)
            {
#else
            // Read Stream into Buffer
            while ((Read = Input.Read(Buffer, 0, _bufferSizeMerge)) > 0)
            {
#endif
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

        private void IOReadWriteSession(Session Input)
        {
            DownloadEvent Event = new DownloadEvent();
            int Read;
#if NETCOREAPP
            Span<byte> Buffer = stackalloc byte[_bufferSize];

            // Read Stream into Buffer
            while ((Read = Input.StreamInput.Read(Buffer)) > 0)
            {
                // Write Buffer to the output Stream
                Input.StreamOutput.Write(Buffer.Slice(0, Read));
#else
            byte[] Buffer = new byte[_bufferSize];

            // Read Stream into Buffer
            while ((Read = Input.StreamInput.Read(Buffer, 0, _bufferSize)) > 0)
            {
                // Write Buffer to the output Stream
                Input.StreamOutput.Write(Buffer, 0, Read);
#endif
                // Increment as last OffsetStart adjusted
                Input.OffsetStart += Read;
                // Set Inner Session Status
                Input.SessionState = DownloadState.Downloading;
                // Throw if Token Cancellation is requested
                Input.SessionToken.ThrowIfCancellationRequested();
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

#if NETCOREAPP
        private async ValueTask IOReadWriteSessionAsync(Session Input)
#else
        private async Task IOReadWriteSessionAsync(Session Input)
#endif
        {
            DownloadEvent Event = new DownloadEvent();
            int Read;
#if NETCOREAPP
            Memory<byte> Buffer = new byte[_bufferSize];

            // Read Stream into Buffer
            while ((Read = await Input.StreamInput.ReadAsync(Buffer, Input.SessionToken)) > 0)
            {
                // Write Buffer to the output Stream
                await Input.StreamOutput.WriteAsync(Buffer.Slice(0, Read), Input.SessionToken);
#else
            byte[] Buffer = new byte[_bufferSize];

            // Read Stream into Buffer
            while ((Read = await Input.StreamInput.ReadAsync(Buffer, 0, _bufferSize, Input.SessionToken)) > 0)
            {
                // Write Buffer to the output Stream
                await Input.StreamOutput.WriteAsync(Buffer, 0, Read, Input.SessionToken);
#endif
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

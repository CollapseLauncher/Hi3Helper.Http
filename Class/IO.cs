using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public sealed partial class Http
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async
#if NETCOREAPP
            ValueTask
#else
            Task
#endif
            IOReadWrite(Stream Input, Stream Output, CancellationToken Token)
        {
            DownloadEvent Event = new DownloadEvent();
            int Read;
            byte[] Buffer = new byte[_bufferSize];

            // Read Stream into Buffer
            while ((Read = await Input
#if NETCOREAPP
                .ReadAsync(Buffer, Token)
#else
                .ReadAsync(Buffer, 0, _bufferSize, Token)
#endif
                ) > 0)
            {
                // Write Buffer to the output Stream
#if NETCOREAPP
                Token.ThrowIfCancellationRequested();
                Output.Write(Buffer, 0, Read);
#else
                await Output
                    .WriteAsync(Buffer, 0, Read, Token);
#endif

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async
#if NETCOREAPP
            ValueTask
#else
            Task
#endif
            IOReadWriteSession(Session Input, CancellationToken Token)
        {
            DownloadEvent Event = new DownloadEvent();
            int Read;
            byte[] Buffer = new byte[_bufferSize];

            // Read Stream into Buffer
            while ((Read = await Input.StreamInput
                .ReadAsync(Buffer, 0, _bufferSize, Token)
                .TimeoutAfter(Token)
                ) > 0)
            {
                // Write Buffer to the output Stream
#if NETCOREAPP
                Token.ThrowIfCancellationRequested();
                Input.StreamOutput.Write(Buffer, 0, Read);
#else
                await Input.StreamOutput
                    .WriteAsync(Buffer, 0, Read, Token);
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

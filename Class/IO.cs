using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public sealed partial class Http
    {
        private async
#if NET6_0_OR_GREATER
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
#if NET6_0_OR_GREATER
                .ReadAsync(Buffer, Token)
#else
                .ReadAsync(Buffer, 0, _bufferSize, Token)
#endif
                ) > 0)
            {
                // Write Buffer to the output Stream
#if NET6_0_OR_GREATER
                Token.ThrowIfCancellationRequested();
                Output.Write(Buffer, 0, Read);
#else
                await Output
                    .WriteAsync(Buffer, 0, Read, Token);
#endif

                // Increment SizeDownloaded attribute
                Interlocked.Add(ref this.SizeAttribute.SizeDownloaded, Read);
                Interlocked.Add(ref this.SizeAttribute.SizeDownloadedLast, Read);

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
    }
}

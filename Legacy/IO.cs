using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http.Legacy
{
    public sealed partial class Http
    {
        private async
#if NET6_0_OR_GREATER
            ValueTask
#else
            Task
#endif
            IoReadWrite(Stream input, Stream output, CancellationToken token)
        {
            DownloadEvent @event = new();
            int           read;
            byte[]        buffer = new byte[BufferSize];

            // Read Stream into Buffer
            while ((read = await input
#if NET6_0_OR_GREATER
                .ReadAsync(buffer, token)
#else
                .ReadAsync(Buffer, 0, _bufferSize, Token)
#endif
                ) > 0)
            {
                // Write Buffer to the output Stream
#if NET6_0_OR_GREATER
                token.ThrowIfCancellationRequested();
                await output.WriteAsync(buffer.AsMemory(0, read), token);
#else
                await Output
                    .WriteAsync(Buffer, 0, Read, Token);
#endif

                // Increment SizeDownloaded attribute
                Interlocked.Add(ref _sizeAttribute.SizeDownloaded, read);
                Interlocked.Add(ref _sizeAttribute.SizeDownloadedLast, read);

                // Update state
                @event.UpdateDownloadEvent(
                        _sizeAttribute.SizeDownloadedLast,
                        _sizeAttribute.SizeDownloaded,
                        _sizeAttribute.SizeTotalToDownload,
                        read,
                        _sessionsStopwatch.Elapsed.TotalSeconds,
                        DownloadState
                        );
                UpdateProgress(@event);
            }
        }

        internal static async ValueTask<FileStream> NaivelyOpenFileStreamAsync(FileInfo info, FileMode fileMode, FileAccess fileAccess, FileShare fileShare)
        {
            const int maxTry = 10;
            int currentTry = 1;
            while (true)
            {
                try
                {
                    return info.Open(fileMode, fileAccess, fileShare);
                }
                catch
                {
                    if (currentTry > maxTry)
                    {
                        throw; // Throw this MFs
                    }

                    PushLog($"Failed while trying to open: {info.FullName}. Retry attempt: {++currentTry} / {maxTry}", DownloadLogSeverity.Warning);
                    await Task.Delay(50); // Adding 50ms delay
                }
            }
        }
    }
}

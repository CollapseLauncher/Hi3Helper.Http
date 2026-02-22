using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable InconsistentNaming
// ReSharper disable AccessToModifiedClosure

namespace Hi3Helper.Http
{
    internal static class IO
    {
        internal const int StreamRWBufferSize = 64 << 10;

        internal static async Task WriteStreamToFileChunkSessionAsync(
            ChunkSession              session,
            DownloadSpeedLimiter?     downloadSpeedLimiter,
            int                       threadSize,
            HttpResponseInputStream?  networkStream,
            bool                      isNetworkStreamFromExternal,
            Stream                    fileStream,
            DownloadProgress          downloadProgress,
            DownloadProgressDelegate? progressDelegateAsync,
            CancellationToken         token)
        {
            int currentRetry = 0;

        StartWrite:
            byte[] buffer = ArrayPool<byte>.Shared.Rent(16 << 10);
            CancellationTokenSource? timeoutToken = null;
            CancellationTokenSource? coopToken = null;

            try
            {
                if (session.CurrentPositions.End != 0 && session.CurrentPositions.Start >= session.CurrentPositions.End)
                {
                    return;
                }

                if (!isNetworkStreamFromExternal || (isNetworkStreamFromExternal && currentRetry > 0))
                {
                    networkStream = await CreateStreamFromSessionAsync(session, token);
                }

                if (networkStream == null)
                {
                    throw new NullReferenceException("networkStream argument returns null!");
                }

                if (fileStream.CanSeek && session.CurrentPositions.End + 1 > fileStream.Length)
                {
                    fileStream.SetLength(session.CurrentPositions.Start);
                }

                if (fileStream.CanSeek)
                {
                    fileStream.Seek(session.CurrentPositions.Start, SeekOrigin.Begin);
                }

                timeoutToken = new CancellationTokenSource(session.TimeoutAfterInterval);
                coopToken = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken.Token, token);

                int read;
                while ((read = await networkStream.ReadAsync(buffer, coopToken.Token)) > 0)
                {
                    await (downloadSpeedLimiter?.AddBytesOrWaitAsync(read, token) ?? ValueTask.CompletedTask);
                    await fileStream.WriteAsync(buffer, 0, read, coopToken.Token);

                    session.CurrentPositions.AdvanceStartOffset(read);
                    session.CurrentMetadata?.UpdateLastEndOffset(session.CurrentPositions);
                    downloadProgress.AdvanceBytesDownloaded(read);
                    progressDelegateAsync?.Invoke(read, downloadProgress);

                    timeoutToken.Dispose();
                    coopToken.Dispose();

                    timeoutToken = new CancellationTokenSource(session.TimeoutAfterInterval);
                    coopToken = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken.Token, token);

                    currentRetry = 0;
                }
            }
            catch (TaskCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (NullReferenceException)
            {
                throw;
            }
            catch (Exception)
            {
                currentRetry++;
                if (currentRetry > session.RetryMaxAttempt)
                {
                    throw;
                }

                await Task.Delay(session.RetryAttemptInterval, token);
                goto StartWrite;
            }
            finally
            {
                if (networkStream != null)
                {
                    await networkStream.DisposeAsync();
                }

                ArrayPool<byte>.Shared.Return(buffer);

                timeoutToken?.Dispose();
                coopToken?.Dispose();
            }
        }

        private static async ValueTask<HttpResponseInputStream?> CreateStreamFromSessionAsync(ChunkSession session,
            CancellationToken token)
        {
            // Assign the url and throw if null
            Uri? fileUri = session.CurrentMetadata?.Url;
            if (fileUri == null)
            {
                throw new NullReferenceException("Metadata was found to be null and it shouldn't happen!");
            }

            // Create the network stream instance
            HttpResponseInputStream? stream = await HttpResponseInputStream
                .CreateStreamAsync(
                    session.CurrentHttpClient,
                    fileUri,
                    session.CurrentPositions.Start,
                    session.CurrentPositions.End,
                    session.TimeoutAfterInterval,
                    session.RetryAttemptInterval,
                    session.RetryMaxAttempt,
                    token);

            // Return the stream
            return stream;
        }
    }
}
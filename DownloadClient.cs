using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Local
// ReSharper disable UnusedMember.Global
// ReSharper disable ConvertToUsingDeclaration
// ReSharper disable IdentifierTypo
// ReSharper disable ArrangeObjectCreationWhenTypeEvident

namespace Hi3Helper.Http
{
    /// <summary>
    ///     An instance of the download client. This instance can be re-used. But besides it can be re-used, make sure to set
    ///     the sufficient <c>MaxConnection</c> of the <seealso cref="HttpClient" />
    ///     assigned into the current <seealso cref="DownloadClient" /> instance.
    /// </summary>
    public sealed class DownloadClient
    {
        private const int DefaultConnectionSessions = 4;
        private const int DefaultRetryCountMax = 5;
        private const int MinimumDownloadSpeedLimit = 256 << 10; // 262144 bytes/s (256 KiB/s)

        private TimeSpan RetryAttemptInterval { get; init; }
        private int RetryCountMax { get; init; }
        private TimeSpan TimeoutAfterInterval { get; init; }
        private HttpClient CurrentHttpClientInstance { get; init; }


        internal static event EventHandler<int>? DownloadSpeedLimitChanged;

        private static int _downloadSpeedLimitBase = -1;

        /// <summary>
        ///     Set the limit of the download speed shared across all instances.
        /// </summary>
        internal static int DownloadSpeedLimitBase
        {
            get => _downloadSpeedLimitBase;
            set
            {
                _downloadSpeedLimitBase = Math.Max(MinimumDownloadSpeedLimit, value);
                DownloadSpeedLimitChanged?.Invoke(null, _downloadSpeedLimitBase);
            }
        }


        private const int DefaultSessionChunkSize = 4 << 20; // 4 MiB for each chunk size

        private DownloadClient(HttpClient httpClient, int retryCountMax = DefaultRetryCountMax,
            TimeSpan? retryAttemptInterval = null, TimeSpan? timeoutAfterInterval = null)
        {
            retryAttemptInterval ??= TimeSpan.FromSeconds(1);
            RetryAttemptInterval = retryAttemptInterval.Value;

            timeoutAfterInterval ??= TimeSpan.FromSeconds(10);
            TimeoutAfterInterval = timeoutAfterInterval.Value;

            RetryCountMax = retryCountMax;
            CurrentHttpClientInstance = httpClient;
        }

        /// <summary>
        ///     <inheritdoc cref="DownloadSpeedLimitBase" />
        /// </summary>
        /// <param name="speedLimit">
        ///     Set the limit of the speed. The default -1 (No limit).<br />
        ///     The minimum size is: 262144 bytes (256 KiB/s). If the value is below the minimum limit, the speed will be set to
        ///     256 KiB
        /// </param>
        public static void SetSharedDownloadSpeedLimit(int speedLimit = -1)
        {
            DownloadSpeedLimitBase = speedLimit;
        }

        /// <summary>
        ///     Create an instance of a Http Download Client instance from the given <seealso cref="HttpClient" /> instance.
        /// </summary>
        /// <param name="httpClient">
        ///     Use the HttpClient from the parent caller from the given <seealso cref="HttpClient" /> instance.
        /// </param>
        /// <param name="retryCountMax">
        ///     Count of how many times the retry attempt should be accepted.
        /// </param>
        /// <param name="retryAttemptInterval">
        ///     Determine how long the pause will run before the next retry attempt is executed. The default value is 1 second.
        /// </param>
        /// <param name="timeoutAfterInterval">
        ///     Determine how long the method will time out while it's getting called.
        /// </param>
        /// <returns>An instance of a Http Download Client</returns>
        /// <exception cref="NullReferenceException">Throw if the <paramref name="httpClient" /> argument is <c>null</c>.</exception>
        public static DownloadClient CreateInstance(HttpClient httpClient, int retryCountMax = DefaultRetryCountMax,
            TimeSpan? retryAttemptInterval = null, TimeSpan? timeoutAfterInterval = null)
        {
            // Throw if HttpClient argument is null
            ArgumentNullException.ThrowIfNull(httpClient, nameof(httpClient));

            // Return the instance
            return new DownloadClient(httpClient, retryCountMax, retryAttemptInterval, timeoutAfterInterval);
        }

        /// <summary>
        ///     Start the download process of the file asynchronously. This method can be used for single-session or multi-session
        ///     download.
        /// </summary>
        /// <param name="url">The URL of the file to be downloaded.</param>
        /// <param name="fileOutputPath">
        ///     Output path of the file to download.
        /// </param>
        /// <param name="useOverwrite">
        ///     Overwrite the download even the previous session can be continued.
        ///     If set to true, the previous download will start from beginning.
        /// </param>
        /// <param name="offsetStart">
        ///     The start position of the data to be downloaded. If this argument is set with <paramref name="offsetEnd" /> to
        ///     <c>null</c>,<br />
        ///     the download will start from the beginning of the data. The <paramref name="offsetStart" /> argument cannot be set
        ///     more than or equal as <paramref name="offsetEnd" />.<br /><br />
        ///     Default: <c>null</c>
        /// </param>
        /// <param name="offsetEnd">
        ///     The end position of the data to be downloaded. If this argument is set to <c>null</c>,<br />
        ///     the download will write the data until the end of the data. The <paramref name="offsetEnd" /> argument cannot be
        ///     set less than or equal as <paramref name="offsetStart" />.<br /><br />
        ///     Default: <c>null</c>
        /// </param>
        /// <param name="progressDelegateAsync">
        ///     The delegate callback to process the download progress information.<br /><br />
        ///     Default: <c>null</c>
        /// </param>
        /// <param name="maxConnectionSessions">
        ///     How much connection session to be started for the download process. If it's being set to less than or equal as 0,
        ///     then it will fall back to the default value: 4.<br /><br />
        ///     Default: <c>4</c>
        /// </param>
        /// <param name="sessionChunkSize">
        ///     How big the size of each session chunk.<br /><br />
        ///     Default: <c>4,194,304 bytes</c> or <c>4 MiB</c>
        /// </param>
        /// <param name="cancelToken">
        ///     Cancellation token. If not assigned, a cancellation token will not be assigned and the download becomes
        ///     non-cancellable.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="url" /> or <paramref name="fileOutputPath" /> is null.</exception>
        /// <exception cref="ArgumentException">
        ///     <paramref name="url" /> or <paramref name="fileOutputPath" /> is empty or only have
        ///     whitespaces.
        /// </exception>
        public async Task DownloadAsync(string url, string fileOutputPath, bool useOverwrite = false,
            long? offsetStart = null, long? offsetEnd = null, DownloadProgressDelegate? progressDelegateAsync = null,
            int maxConnectionSessions = DefaultConnectionSessions,
            int sessionChunkSize = DefaultSessionChunkSize,
            CancellationToken cancelToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(url, nameof(url));
            ArgumentException.ThrowIfNullOrWhiteSpace(url, nameof(url));
            ArgumentException.ThrowIfNullOrEmpty(fileOutputPath, nameof(fileOutputPath));
            ArgumentException.ThrowIfNullOrWhiteSpace(fileOutputPath, nameof(fileOutputPath));

            Uri uri = url.ToUri();

            DownloadProgress downloadProgressStruct = new DownloadProgress();
            ActionBlock<ChunkSession> actionBlock = new ActionBlock<ChunkSession>(async chunk =>
                {
                    await using (FileStream stream = new FileStream(chunk.CurrentMetadata?.OutputFilePath!,
                                     FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write))
                    {
                        if (chunk.CurrentMetadata == null)
                        {
                            throw new NullReferenceException("chunk.CurrentMetadata reference is null");
                        }

                        await chunk.CurrentMetadata.SaveLastMetadataStateAsync(cancelToken);
                        await IO.WriteStreamToFileChunkSessionAsync(chunk, maxConnectionSessions, null, false, stream,
                            downloadProgressStruct, progressDelegateAsync, cancelToken);

                        chunk.CurrentMetadata.PopRange(chunk.CurrentPositions);
                        await chunk.CurrentMetadata.SaveLastMetadataStateAsync(cancelToken);
                    }
                },
                new ExecutionDataflowBlockOptions
                {
                    CancellationToken = cancelToken,
                    MaxDegreeOfParallelism = maxConnectionSessions,
                    MaxMessagesPerTask = maxConnectionSessions,
                    BoundedCapacity = maxConnectionSessions
                });

            await foreach (ChunkSession session in ChunkSession.EnumerateMultipleChunks(
                               CurrentHttpClientInstance,
                               uri,
                               fileOutputPath,
                               useOverwrite,
                               sessionChunkSize,
                               downloadProgressStruct,
                               progressDelegateAsync,
                               RetryCountMax,
                               RetryAttemptInterval,
                               TimeoutAfterInterval
                           ).WithCancellation(cancelToken))
            {
                await actionBlock.SendAsync(session, cancelToken);
            }

            actionBlock.Complete();
            await actionBlock.Completion;
            if (actionBlock.Completion.Exception != null)
            {
                throw actionBlock.Completion.Exception;
            }

            Metadata.DeleteMetadataFile(fileOutputPath);
        }

        /// <summary>
        ///     Start the download process to a <seealso cref="Stream" />.
        /// </summary>
        /// <param name="url">The URL of the file to be downloaded into stream.</param>
        /// <param name="outputStream">
        ///     The output stream where the data will be downloaded. The <paramref name="outputStream" /> must be writable.
        /// </param>
        /// <param name="allowContinue">
        ///     Allow to resume the last position of the download.
        ///     However, this argument will be ignored if the <paramref name="outputStream" /> is not seekable and
        ///     <paramref name="offsetStart" /> is set to > 0.
        /// </param>
        /// <param name="offsetStart">
        ///     The start position of the data to be downloaded. If this argument is set with <paramref name="offsetEnd" /> to
        ///     <c>null</c>,<br />
        ///     the download will start from the beginning of the data. The <paramref name="offsetStart" /> argument cannot be set
        ///     more than or equal as <paramref name="offsetEnd" />.<br /><br />
        ///     Default: <c>null</c>
        /// </param>
        /// <param name="offsetEnd">
        ///     The end position of the data to be downloaded. If this argument is set to <c>null</c>,<br />
        ///     the download will write the data until the end of the data. The <paramref name="offsetEnd" /> argument cannot be
        ///     set less than or equal as <paramref name="offsetStart" />.<br /><br />
        ///     Default: <c>null</c>
        /// </param>
        /// <param name="progressDelegateAsync">
        ///     The delegate callback to process the download progress information.<br /><br />
        ///     Default: <c>null</c>
        /// </param>
        /// <param name="cancelToken">
        ///     Cancellation token. If not assigned, a cancellation token will not be assigned and the download becomes
        ///     non-cancellable.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="url" /> or <paramref name="outputStream" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="url" /> is empty or only have whitespaces.</exception>
        public async Task DownloadAsync(string url, Stream outputStream,
            bool allowContinue, DownloadProgressDelegate? progressDelegateAsync = null,
            long? offsetStart = null, long? offsetEnd = null,
            CancellationToken cancelToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(url, nameof(url));
            ArgumentException.ThrowIfNullOrWhiteSpace(url, nameof(url));
            ArgumentNullException.ThrowIfNull(outputStream, nameof(outputStream));

            Uri uri = url.ToUri();

            offsetStart ??= 0;

            bool isSeeked = false;

            // If the download allows the download to continue and the outputStream is seekable,
            // then seek the outputStream to the very end of its position.
            if (allowContinue
                && outputStream.CanSeek
                && outputStream.IsStreamCanSeeLength()
                && offsetStart == 0)
            {
                outputStream.Seek(0, SeekOrigin.End);
                isSeeked = true;
            }

            // Create the session and stream tuple
            (ChunkSession, HttpResponseInputStream)? networkStream = await ChunkSession
                .CreateSingleSessionAsync(
                    CurrentHttpClientInstance,
                    uri,
                    offsetStart,
                    offsetEnd,
                    RetryCountMax,
                    RetryAttemptInterval,
                    TimeoutAfterInterval,
                    cancelToken
                );

            // If the network stream tuple is null, then ignore
            if (networkStream == null)
            {
                return;
            }

            // Set the download progress struct and set the bytes total to download.
            DownloadProgress downloadProgressStruct = new DownloadProgress();
            downloadProgressStruct.SetBytesTotal(networkStream.Value.Item2.Length);

            // If the outputStream is seekable, then advance the downloaded progress.
            if (isSeeked)
            {
                downloadProgressStruct.AdvanceBytesDownloaded(outputStream.Length);
            }

            // Start the download
            await IO.WriteStreamToFileChunkSessionAsync(
                networkStream.Value.Item1,
                1,
                networkStream.Value.Item2,
                true,
                outputStream,
                downloadProgressStruct,
                progressDelegateAsync,
                cancelToken);
        }

        public void Dispose()
        {
            FinalizeDispose();
        }

        private void FinalizeDispose()
        {
            GC.SuppressFinalize(this);
        }

        ~DownloadClient()
        {
            Dispose();
        }
    }
}
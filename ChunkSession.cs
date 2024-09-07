﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator

namespace Hi3Helper.Http
{
    internal class ChunkSession
    {
        internal static async IAsyncEnumerable<ChunkSession> EnumerateMultipleChunks(
            HttpClient client,
            Uri url,
            string outputFilePath,
            bool overwrite,
            int chunkSize,
            DownloadProgress downloadProgress,
            DownloadProgressDelegate? progressDelegateAsync,
            int retryMaxAttempt,
            TimeSpan retryAttemptInterval,
            TimeSpan timeoutInterval,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Get the file size from the URL
            long contentLength = await url.GetUrlContentLengthAsync(client, retryMaxAttempt, retryAttemptInterval,
                timeoutInterval, cancellationToken);

            // Set the length to the progress
            downloadProgress.SetBytesTotal(contentLength);

            // Enumerate previous chunks inside the metadata first
            FileInfo outputFileInfo = new FileInfo(outputFilePath);

            // Enumerate previous chunks inside the metadata first
            string metadataFilePath = outputFileInfo.FullName + Metadata.MetadataExtension;
            FileInfo metadataFileInfo = new FileInfo(metadataFilePath);

        StartEnumerate:
            // Get the last session metadata info
            Metadata? currentSessionMetadata =
                await Metadata.ReadLastMetadataAsync(url, outputFileInfo, metadataFileInfo,
                contentLength, cancellationToken);

            // null as per completed status
            if (currentSessionMetadata == null)
            {
                downloadProgress.AdvanceBytesDownloaded(contentLength);
                progressDelegateAsync?.Invoke(0, downloadProgress);
                yield break;
            }

            // SANITY CHECK: Metadata and file state check

            // If overwrite is toggled and the file exist, delete them.
            // Or if the file overflow, then delete the file and start from scratch
            if ((outputFileInfo.Exists && overwrite)
             || (outputFileInfo.Exists && outputFileInfo.Length > contentLength)
             || (metadataFileInfo.Exists && metadataFileInfo.Length < 64 && outputFileInfo.Exists)
             || (metadataFileInfo.Exists && !outputFileInfo.Exists)
             || ((currentSessionMetadata?.Ranges?.Count ?? 0) == 0 && (currentSessionMetadata?.IsCompleted ?? false)))
            {
                // Remove the redundant metadata file and refresh
                metadataFileInfo.Refresh();
                if (metadataFileInfo.Exists)
                {
                    metadataFileInfo.IsReadOnly = false;
                    metadataFileInfo.Delete();
                    metadataFileInfo.Refresh();
                }

                // If the current file info exist while the metadata is in invalid state,
                // then remove the file.
                outputFileInfo.Refresh();
                if (outputFileInfo.Exists)
                {
                    outputFileInfo.IsReadOnly = false;
                    outputFileInfo.Delete();
                    outputFileInfo.Refresh();
                }

                // Go start over
                goto StartEnumerate;
            }

            // If the completed flag is set, the ranges are empty, the output file exist with the length is equal,
            // then return from enumerating. Or if the ranges list is empty, return
            if ((currentSessionMetadata?.Ranges?.Count == 0
                 && outputFileInfo.Exists
                 && outputFileInfo.Length == contentLength)
                || currentSessionMetadata?.Ranges == null)
            {
                downloadProgress.AdvanceBytesDownloaded(contentLength);
                progressDelegateAsync?.Invoke(0, downloadProgress);
                yield break;
            }

            // Enumerate last ranges
            long lastEndOffset = currentSessionMetadata.Ranges.Count > 0
                ? currentSessionMetadata.Ranges.Max(x => x?.End ?? 0) + 1
                : 0;

            // If the metadata is not exist, but it has an uncompleted file with size > DefaultSessionChunkSize,
            // then try to resume the download and advance the lastEndOffset from the file last position.
            if (currentSessionMetadata.Ranges.Count == 0
                && outputFileInfo.Exists
                && outputFileInfo.Length > chunkSize
                && currentSessionMetadata.LastEndOffset <= outputFileInfo.Length)
            {
                lastEndOffset = outputFileInfo.Length;
                downloadProgress.AdvanceBytesDownloaded(outputFileInfo.Length);
                progressDelegateAsync?.Invoke(0, downloadProgress);
            }
            // Else if the file exist with size downloaded less than LastEndOffset, then continue
            // the position based on metadata.
            else if (outputFileInfo.Exists)
            {
                ChunkRange lastRange = new ChunkRange();
                List<ChunkRange?> copyOfExistingRanges = new List<ChunkRange?>(currentSessionMetadata.Ranges);
                foreach (ChunkRange? range in copyOfExistingRanges)
                {
                    // If range somehow return a null or the outputFileInfo.Length is less than range.Start and range.End,
                    // or if the file does not exist, then skip
                    if (range == null)
                    {
                        continue;
                    }

                    long toAdd = range.Start - lastRange.End;

                    downloadProgress.AdvanceBytesDownloaded(toAdd);
                    progressDelegateAsync?.Invoke(0, downloadProgress);

                    lastRange = range;

                    yield return new ChunkSession
                    {
                        CurrentHttpClient = client,
                        CurrentMetadata = currentSessionMetadata,
                        CurrentPositions = range,
                        RetryAttemptInterval = retryAttemptInterval,
                        RetryMaxAttempt = retryMaxAttempt,
                        TimeoutAfterInterval = timeoutInterval
                    };
                }
            }

            // Enumerate the chunk session information to process
            long remainedSize = contentLength - lastEndOffset;
            long lastStartOffset = lastEndOffset;
            while (remainedSize > 0)
            {
                long startOffset = lastStartOffset;
                long toAdvanceSize = Math.Min(remainedSize, chunkSize);
                long toAdvanceOffset = toAdvanceSize - 1;
                long endOffset = startOffset + toAdvanceOffset;
                lastStartOffset += toAdvanceSize;
                remainedSize -= toAdvanceSize;

                ChunkSession chunkSession = new ChunkSession
                {
                    CurrentHttpClient = client,
                    CurrentMetadata = currentSessionMetadata,
                    CurrentPositions = new ChunkRange
                    {
                        Start = startOffset,
                        End = endOffset
                    },
                    RetryAttemptInterval = retryAttemptInterval,
                    RetryMaxAttempt = retryMaxAttempt,
                    TimeoutAfterInterval = timeoutInterval
                };
                currentSessionMetadata.PushRange(chunkSession.CurrentPositions);
                yield return chunkSession;
            }
        }

        internal static async ValueTask<(ChunkSession, HttpResponseInputStream)?> CreateSingleSessionAsync(
            HttpClient client,
            Uri url,
            long? offsetStart,
            long? offsetEnd,
            int retryMaxAttempt,
            TimeSpan retryAttemptInterval,
            TimeSpan timeoutInterval,
            CancellationToken cancellationToken
        )
        {
            // Create network stream
            HttpResponseInputStream? networkStream = await HttpResponseInputStream
                .CreateStreamAsync(client, url, offsetStart, offsetEnd, timeoutInterval, retryAttemptInterval,
                    retryMaxAttempt, cancellationToken);

            // If the network stream is null (due to StatusCode 416), then return null
            if (networkStream == null)
            {
                return null;
            }

            // Create the session without metadata
            ChunkSession session = new ChunkSession
            {
                CurrentPositions = new ChunkRange
                {
                    Start = offsetStart ?? 0,
                    End = networkStream.Length
                },
                CurrentMetadata = new Metadata
                {
                    TargetToCompleteSize = networkStream.Length,
                    Url = url
                },
                CurrentHttpClient = client,
                RetryMaxAttempt = retryMaxAttempt,
                RetryAttemptInterval = retryAttemptInterval,
                TimeoutAfterInterval = timeoutInterval
            };

            // Return as tuple
            return (session, networkStream);
        }

#nullable disable
        internal ChunkRange CurrentPositions { get; private init; }
        internal Metadata CurrentMetadata { get; private set; }
        internal HttpClient CurrentHttpClient { get; private set; }
        internal int RetryMaxAttempt { get; private set; }
        internal TimeSpan RetryAttemptInterval { get; private set; }
        internal TimeSpan TimeoutAfterInterval { get; private set; }
    }
}
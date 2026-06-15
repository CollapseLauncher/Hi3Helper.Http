using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

#if NETCOREAPP
using System.Runtime.Intrinsics;
#endif

#if NETCOREAPP && !NET7_0_OR_GREATER
using System.Runtime.Intrinsics.X86;
#endif

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
            // Throw if cancellation is triggered
            cancellationToken.ThrowIfCancellationRequested();

            // Get the file size from the URL
            long contentLength = await url.GetUrlContentLengthAsync(client, retryMaxAttempt, retryAttemptInterval,
                timeoutInterval, cancellationToken);

            // Set the length to the progress
            downloadProgress.SetBytesTotal(contentLength);

            // Enumerate previous chunks inside the metadata first
            FileInfo outputFileInfo = new(outputFilePath);

            // Enumerate previous chunks inside the metadata first
            string metadataFilePath = outputFileInfo.FullName + Metadata.MetadataExtension;
            FileInfo metadataFileInfo = new(metadataFilePath);

        StartEnumerate:
            // Get the last session metadata info
            Metadata? currentSessionMetadata =
                await Metadata.ReadLastMetadataAsync(url, outputFileInfo, metadataFileInfo,
                contentLength, cancellationToken);

            // null as per completed status and if it's not in overwrite
            if (currentSessionMetadata == null && !overwrite)
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
             || (metadataFileInfo is { Exists: true, Length: < 64 } && outputFileInfo.Exists)
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
                ChunkRange        lastRange            = new();
                List<ChunkRange?> copyOfExistingRanges = new(currentSessionMetadata.Ranges);
                foreach (ChunkRange? range in copyOfExistingRanges)
                {
                    // Throw if cancellation is triggered
                    cancellationToken.ThrowIfCancellationRequested();

                    // If range somehow return a null or the outputFileInfo.Length is less than range.Start and range.End,
                    // or if the file does not exist, then skip
                    if (range == null)
                    {
                        continue;
                    }

                    // Check for invalid zero data at start
                    CheckInvalidZeroDataAtStart(range, outputFileInfo, copyOfExistingRanges);

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
                // Throw if cancellation is triggered
                cancellationToken.ThrowIfCancellationRequested();

                long startOffset = lastStartOffset;
                long toAdvanceSize = Math.Min(remainedSize, chunkSize);
                long toAdvanceOffset = toAdvanceSize - 1;
                long endOffset = startOffset + toAdvanceOffset;
                lastStartOffset += toAdvanceSize;
                remainedSize -= toAdvanceSize;

                ChunkSession chunkSession = new()
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

        private static unsafe void CheckInvalidZeroDataAtStart(ChunkRange range, FileInfo existingFileInfo, List<ChunkRange?> listOfRanges)
        {
            // If the start is 0, then return
            if (range.Start == 0)
                return;

            // Set the buffer to read to 4096 bytes
            const int bufferLen = 4 << 10;
            long nearbyEnd = -1;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferLen);
            try
            {
                // Try to find nearby start
                for (int i = 0; i < listOfRanges.Count - 2; i++)
                {
                    if (listOfRanges[i]?.Start != 0 && i == 0)
                    {
                        nearbyEnd = 0;
                        break;
                    }

                    // If the previous start range is less than current start range and
                    // the next start range is more than the current end range, then assign the nearby end.
                    if (listOfRanges[i]?.End < range.Start && listOfRanges[i + 1]?.End == range.End)
                    {
                        nearbyEnd = listOfRanges[i]?.End ?? 0;
                        break;
                    }
                }

                // If the nearby end is more than or equal to the current start - 1, then return
                if (nearbyEnd >= range.Start - 1)
                {
                    return;
                }

                // If the nearby end is equal to -1 (none) and the list of
                // ranges count is more than 2, then return
                if (nearbyEnd == -1 && listOfRanges.Count > 2)
                {
                    return;
                }

                // Start checking if start is more than nearby end,
                // then start checking for the zero data
                if (range.Start > nearbyEnd)
                {
                    // Get the data stream
#if NETCOREAPP
                    using FileStream fileStream = existingFileInfo.Open(new FileStreamOptions
                    {
                        Mode    = FileMode.OpenOrCreate,
                        Access  = FileAccess.ReadWrite,
                        Share   = FileShare.ReadWrite,
                        Options = FileOptions.WriteThrough
                    });
#else
                    using FileStream fileStream = new(existingFileInfo.FullName,
                                                      FileMode.OpenOrCreate,
                                                      FileAccess.ReadWrite,
                                                      FileShare.ReadWrite,
                                                      0,
                                                      FileOptions.WriteThrough);
#endif
                StartReadData:
                    // If the current start range is less than nearby end, then increment and return.
                    if (range.Start < nearbyEnd)
                    {
                        range.Start = nearbyEnd + 1;
                        return;
                    }

                    // Clamp the value between length of buffer and fileStream length, subtract to
                    // the current start range, and to between 0.
                    int toReadMin;
                    if (range.Start < bufferLen)
                    {
                        toReadMin = (int)range.Start;
                    }
                    else
                    {
                        toReadMin = (int)Math.Min(fileStream.Length, bufferLen);
                    }

                    fileStream.Position = Math.Max(range.Start - toReadMin, 0);

                    // Read the stream to the given buffer length
                    int read = fileStream.Read(buffer, 0, toReadMin);

                    // Assign the offset as the read pos and init offset back value.
                    int offset           = read;
                    int dataOffsetToBack = 0;

                    // If file is EOF, then return
                    if (read == 0)
                    {
                        return;
                    }

                    // UNSAFE: Assign buffer as pointer
                    fixed (byte* bufferPtr = &buffer[0])
                    {
                        // Start zero bytes check
                        StartZeroCheck:
                        // If there is no offset left, then continue read another data
                        if (offset == 0)
                        {
                            range.Start -= dataOffsetToBack;
                            goto StartReadData;
                        }

                        // Read 32 bytes from last, check if all the values are zero with SIMD
                        bool isVector256Zero = IsVector256Zero(bufferPtr, offset);
                        if (isVector256Zero)
                        {
                            offset           -= 32;
                            dataOffsetToBack += 32;
                            goto StartZeroCheck;
                        }

                        // Read 16 bytes from last, check if all the values are zero with SIMD
                        bool isVector128Zero = IsVector128Zero(bufferPtr, offset);
                        if (isVector128Zero)
                        {
                            offset           -= 16;
                            dataOffsetToBack += 16;
                            goto StartZeroCheck;
                        }

                        // Read 8 bytes from last, check if all the values are zero
                        bool isInt64Zero = *(long*)(bufferPtr + (offset - 8)) == 0;
                        if (isInt64Zero)
                        {
                            offset           -= 8;
                            dataOffsetToBack += 8;
                            goto StartZeroCheck;
                        }

                        // Read 4 bytes from last, check if all the values are zero
                        bool isInt32Zero = *(int*)(bufferPtr + (offset - 4)) == 0;
                        if (isInt32Zero)
                        {
                            offset           -= 4;
                            dataOffsetToBack += 4;
                            goto StartZeroCheck;
                        }

                        // Read one byte from last, check if all the values are zero
                        bool isInt8Zero = *(bufferPtr + (offset - 1)) == 0;
                        if (isInt8Zero)
                        {
                            --offset;
                            ++dataOffsetToBack;
                            goto StartZeroCheck;
                        }

                        // If all the bytes are non-zero (clean), then subtract the current start range and return
                        if (!isVector256Zero && !isVector128Zero && !isInt64Zero && !isInt32Zero && !isInt8Zero)
                        {
                            range.Start -= dataOffsetToBack;
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static unsafe bool IsVector128Zero(byte* bufferPtr, int offset)
        {
            if (offset < 16)
                return false;

            byte* ptr = bufferPtr + offset - 16;
#if NET7_0_OR_GREATER
            // If 128-bit Hardware-Accelerated vectorization is not supported, use scalar.
            if (!Vector128.IsHardwareAccelerated) return IsScalar128Zero(ptr);

            var data = Unsafe.ReadUnaligned<Vector128<byte>>(ptr);
            return Vector128.EqualsAll(data, Vector128<byte>.Zero);
#elif NET6_0_OR_GREATER
            // If SSE2 is not supported, use scalar.
            if (!Sse2.IsSupported) return IsScalar128Zero(ptr);

            var data = Unsafe.ReadUnaligned<Vector128<byte>>(ptr);
            Vector128<byte> result = Sse2.CompareEqual(data, Vector128<byte>.Zero);
            int mask = Sse2.MoveMask(result);

            return mask == 0xFFFF; // In SSE2, 0xFFFF == all zero
#else
            return IsScalar128Zero(ptr);
#endif
        }

        private static unsafe bool IsVector256Zero(byte* bufferPtr, int offset)
        {
            if (offset < 32)
                return false;

            byte* ptr = bufferPtr + offset - 32;
#if NET7_0_OR_GREATER
            // If 256-bit Hardware-Accelerated vectorization is not supported, use scalar.
            if (!Vector256.IsHardwareAccelerated) return IsScalar256Zero(ptr);

            var data = Unsafe.ReadUnaligned<Vector256<byte>>(ptr);
            return Vector256.EqualsAll(data, Vector256<byte>.Zero);
#elif NET6_0_OR_GREATER
            // If AVX2 is not supported, use scalar.
            if (!Avx2.IsSupported) return IsScalar256Zero(ptr);

            var data = Unsafe.ReadUnaligned<Vector256<byte>>(ptr);
            Vector256<byte> result = Avx2.CompareEqual(data, Vector256<byte>.Zero);
            int mask = Avx2.MoveMask(result);

            return mask == unchecked((int)0xFFFFFFFF); // In AVX, 0xFFFFFFFF == all zero
#else
            return IsScalar256Zero(ptr);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool IsScalar128Zero(byte* ptr)
        {
            nuint a = Unsafe.ReadUnaligned<nuint>(ptr);
            nuint b = Unsafe.ReadUnaligned<nuint>(ptr + sizeof(nuint));

            if (sizeof(nuint) == 8)
            {
                return (a | b) == 0;
            }

            nuint c = Unsafe.ReadUnaligned<nuint>(ptr + 8);
            nuint d = Unsafe.ReadUnaligned<nuint>(ptr + 12);

            return (a | b | c | d) == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool IsScalar256Zero(byte* ptr)
        {
            nuint a = Unsafe.ReadUnaligned<nuint>(ptr);
            nuint b = Unsafe.ReadUnaligned<nuint>(ptr + sizeof(nuint));
            nuint c = Unsafe.ReadUnaligned<nuint>(ptr + sizeof(nuint) * 2);
            nuint d = Unsafe.ReadUnaligned<nuint>(ptr + sizeof(nuint) * 3);

            if (sizeof(nuint) == 8)
            {
                return (a | b | c | d) == 0;
            }

            nuint e = Unsafe.ReadUnaligned<nuint>(ptr + 16);
            nuint f = Unsafe.ReadUnaligned<nuint>(ptr + 20);
            nuint g = Unsafe.ReadUnaligned<nuint>(ptr + 24);
            nuint h = Unsafe.ReadUnaligned<nuint>(ptr + 28);

            return (a | b | c | d | e | f | g | h) == 0;
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
            ChunkSession session = new()
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
        internal ChunkRange CurrentPositions { get; private set; }
        internal Metadata CurrentMetadata { get; private set; }
        internal HttpClient CurrentHttpClient { get; private set; }
        internal int RetryMaxAttempt { get; private set; }
        internal TimeSpan RetryAttemptInterval { get; private set; }
        internal TimeSpan TimeoutAfterInterval { get; private set; }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

// ReSharper disable ConvertToUsingDeclaration

namespace Hi3Helper.Http
{
    internal class Metadata
    {
        internal const string MetadataExtension = ".collapseMeta";

        public Uri?               Url                  { get; set; }
        public string?            MetadataFilePath     { get; set; }
        public string?            OutputFilePath       { get; set; }
        public List<ChunkRange?>  Ranges               { get; set; } = new List<ChunkRange?>();
        public long               TargetToCompleteSize { get; set; }
        public bool               IsCompleted          { get; set; }
        public long               LastEndOffset        { get; set; }

        [JsonIgnore]
#if NET10_0_OR_GREATER
        private readonly Lock _lockObj = new();
#else
        private readonly object _lockObj = new();
#endif

        internal event EventHandler<bool>? UpdateChunkRangesCountEvent;

        internal static Metadata? OpenLastMetadata(
            Uri? url,
            FileInfo outputFilePath,
            FileInfo metadataFilePath,
            long targetToDownloadSize)
        {
            try
            {
                if (!metadataFilePath.Exists)
                {
                    if (outputFilePath.Exists && outputFilePath.Length == targetToDownloadSize)
                    {
                        return null;
                    }

                    throw new InvalidDataException();
                }

                if (metadataFilePath.Length < 64)
                {
                    throw new InvalidDataException();
                }

                using FileStream metadataStream = metadataFilePath.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                Metadata? lastMetadata = JsonSerializer.Deserialize(metadataStream, MetadataJsonContext.Default.Metadata);

                if (lastMetadata == null
                    || lastMetadata.Url == null
                    || lastMetadata.Ranges.Count == 0
                    || string.IsNullOrEmpty(lastMetadata.OutputFilePath))
                {
                    throw new InvalidDataException();
                }

                lastMetadata.MetadataFilePath = metadataFilePath.FullName;
                lastMetadata.Ranges.Sort((x, y) =>
                {
                    // If the source is null, then return -1 (less than)
                    if (x == null || y == null)
                    {
                        return -1;
                    }

                    // Compare based on Start
                    int startComparison = x.Start.CompareTo(y.Start);
                    return startComparison != 0
                        ? startComparison
                        :
                        // If Start is equal, compare based on End
                        x.End.CompareTo(y.End);
                });
                lastMetadata.Ranges.RemoveAll(x => x == null);
                return lastMetadata;
            }
            catch (Exception)
            {
                // Invalid state, then return an empty metadata file
                return new Metadata
                {
                    Url = url,
                    OutputFilePath = outputFilePath.FullName,
                    TargetToCompleteSize = targetToDownloadSize,
                    IsCompleted = false,
                    MetadataFilePath = metadataFilePath.FullName
                };
            }
        }

        internal void SaveLastMetadataState()
        {
#if NET10_0_OR_GREATER
            using (_lockObj.EnterScope())
#else
            lock (_lockObj)
#endif
            {
                if (string.IsNullOrEmpty(MetadataFilePath) || string.IsNullOrWhiteSpace(MetadataFilePath))
                {
                    throw new NullReferenceException("MetadataFilePath property is null or empty!");
                }

                using FileStream fileStream = new(MetadataFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                JsonSerializer.Serialize(fileStream, this, MetadataJsonContext.Default.Metadata);
            }
        }

        internal static void DeleteMetadataFile(string outputFilePath)
        {
            string metadataFilePath = outputFilePath + MetadataExtension;
            FileInfo fileInfo = new(metadataFilePath);
            if (!fileInfo.Exists)
            {
                return;
            }

            fileInfo.IsReadOnly = false;
            fileInfo.Delete();
        }

        internal bool PopRange(ChunkRange? range)
        {
#if NET10_0_OR_GREATER
            using (_lockObj.EnterScope())
#else
            lock (_lockObj)
#endif
            {
                if (range == null)
                {
                    return false;
                }

                bool isRemoved = Ranges.Remove(range);
                UpdateChunkRangesCountEvent?.Invoke(null, isRemoved);
                return isRemoved;
            }
        }

        internal bool PushRange(ChunkRange? range)
        {
#if NET10_0_OR_GREATER
            using (_lockObj.EnterScope())
#else
            lock (_lockObj)
#endif
            {
                if (range == null)
                {
                    return false;
                }

                if (Ranges.Contains(range))
                {
                    return false;
                }

                Ranges.Add(range);
                UpdateChunkRangesCountEvent?.Invoke(null, true);
                return true;
            }
        }

        internal void UpdateLastEndOffset(ChunkRange range)
        {
#if NET10_0_OR_GREATER
            using (_lockObj.EnterScope())
#else
            lock (_lockObj)
#endif
            {
                LastEndOffset = Math.Max(range.End, LastEndOffset);
            }
        }
    }
}
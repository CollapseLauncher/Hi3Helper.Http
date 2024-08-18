using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    internal class Metadata
    {
        private const string MetadataExtension = ".collapseMeta";

        public Uri? Url { get; set; }
        public string? MetadataFilePath { get; set; }
        public string? OutputFilePath { get; set; }
        public List<ChunkRange>? Ranges { get; set; }
        public long TargetToCompleteSize { get; set; }
        public bool IsCompleted { get; set; }
        public long LastEndOffset { get; set; }

        [JsonIgnore]
        private bool IsOnWrite { get; set; }

        internal event EventHandler<bool>? UpdateChunkRangesCountEvent;

        internal static async ValueTask<Metadata> ReadLastMetadataAsync(Uri url, string outputFilePath, long targetToDownloadSize, CancellationToken token)
        {
            string metadataFilePath = outputFilePath + MetadataExtension;
            if (!File.Exists(metadataFilePath))
            {
                FileInfo existingOutputFile = new FileInfo(outputFilePath);
                if (existingOutputFile.Exists && existingOutputFile.Length == targetToDownloadSize)
                {
                    return new Metadata
                    {
                        Url = url,
                        OutputFilePath = outputFilePath,
                        TargetToCompleteSize = targetToDownloadSize,
                        IsCompleted = true
                    };
                }
                return new Metadata
                {
                    Url = url,
                    OutputFilePath = outputFilePath,
                    TargetToCompleteSize = targetToDownloadSize,
                    IsCompleted = false,
                    Ranges = new List<ChunkRange>(),
                    MetadataFilePath = metadataFilePath
                };
            }

            using (FileStream metadataStream = new FileStream(metadataFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                Metadata? lastMetadata = await JsonSerializer.DeserializeAsync(metadataStream, MetadataJsonContext.Default.Metadata, token);
                if (lastMetadata != null
                && lastMetadata.Url != null
                && lastMetadata.Ranges?.Count != 0
                && !string.IsNullOrEmpty(lastMetadata.OutputFilePath))
                {
                    lastMetadata.MetadataFilePath = metadataFilePath;
                    lastMetadata.Ranges?.Sort((x, y) =>
                    {
                        // Compare based on Start
                        int startComparison = x.Start.CompareTo(y.Start);
                        if (startComparison != 0)
                        {
                            return startComparison;
                        }

                        // If Start is equal, compare based on End
                        return x.End.CompareTo(y.End);
                    });
                    lastMetadata.Ranges?.RemoveAll(x => x == null);
                    return lastMetadata;
                }

                return new Metadata
                {
                    Url = url,
                    OutputFilePath = outputFilePath,
                    TargetToCompleteSize = targetToDownloadSize,
                    IsCompleted = false,
                    Ranges = new List<ChunkRange>(),
                    MetadataFilePath = metadataFilePath
                };
            }
        }

        internal async Task SaveLastMetadataStateAsync(CancellationToken token)
        {
            if (string.IsNullOrEmpty(MetadataFilePath) || string.IsNullOrWhiteSpace(MetadataFilePath))
                throw new NullReferenceException("MetadataFilePath property is null or empty!");

            IsOnWrite = true;
            FileStream? fileStream = null;
            try
            {
                fileStream = new FileStream(MetadataFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                await JsonSerializer.SerializeAsync(fileStream, this, MetadataJsonContext.Default.Metadata, token);
            }
            finally
            {
                IsOnWrite = false;
                if (fileStream != null)
                    await fileStream.DisposeAsync();
            }
        }

        internal static void DeleteMetadataFile(string outputFilePath)
        {
            string metadataFilePath = outputFilePath + MetadataExtension;
            FileInfo fileInfo = new FileInfo(metadataFilePath);
            if (fileInfo.Exists)
            {
                fileInfo.IsReadOnly = false;
                fileInfo.Delete();
            }
        }

        internal bool PopRange(ChunkRange range)
        {
            if (range == null)
                return false;

            bool isRemoved = Ranges?.Remove(range) ?? false;
            UpdateChunkRangesCountEvent?.Invoke(null, isRemoved);
            return isRemoved;
        }

        internal bool PushRange(ChunkRange range)
        {
            if (range == null)
                return false;
            if (Ranges?.Contains(range) ?? false)
                return false;

            Ranges?.Add(range);
            UpdateChunkRangesCountEvent?.Invoke(null, true);
            return true;
        }

        internal void UpdateLastEndOffset(ChunkRange range)
        {
            LastEndOffset = Math.Max(range.End, LastEndOffset);
        }
    }
}

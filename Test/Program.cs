using Hi3Helper.Http;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Test
{
    [JsonSerializable(typeof(PkgVersion))]
    public partial class JsonContext : JsonSerializerContext { }

    public class PkgVersion
    {
        public required string remoteName { get; set; }
        public required string md5 { get; set; }
        public long fileSize { get; set; }
    }

    internal class Program
    {
        static async Task Main()
        {
            const string ExtractUrl = @"https://autopatchcn.bh3.com/ptpublic/rel/20240805104059_mJaMrTOz6gCYOVBW/PC/extract/";
            const string PkgVersionUrl = ExtractUrl + "pkg_version";
            const string OutputPath = @"E:\myGit\CollapseLauncher-ReleaseRepo\diff\test";

            {
                try
                {
                    using (HttpClientHandler handler = new HttpClientHandler() { MaxConnectionsPerServer = 2048 })
                    using (HttpClient client = new HttpClient(handler, false))
                    using (HttpRequestMessage pkgVersionRequestMessage = new HttpRequestMessage()
                    {
                        RequestUri = new Uri(PkgVersionUrl)
                    })
                    using (HttpResponseMessage pkgVersionResponseMessage = await client.SendAsync(pkgVersionRequestMessage))
                    using (Stream pkgVersionStream = await pkgVersionResponseMessage.Content.ReadAsStreamAsync())
                    {
                        DownloadClient downloader = DownloadClient.CreateInstance(client);
                        await Parallel.ForEachAsync(
                            DeserializePkgVersion(pkgVersionStream),
                            new ParallelOptions
                            {
                                MaxDegreeOfParallelism = Environment.ProcessorCount
                            },
                            async (pkgVersion, token) =>
                        {
                            string outputPath = Path.Combine(OutputPath, pkgVersion.remoteName);
                            string outputDir = Path.GetDirectoryName(outputPath)!;
                            string fileUrl = ExtractUrl + pkgVersion.remoteName;

                            if (!Directory.Exists(outputDir))
                                Directory.CreateDirectory(outputDir);

                            sw = Stopwatch.StartNew();
                            lastDownloaded = 0;

                            // DownloadClient.SetSharedDownloadSpeedLimit(1 << 20);
                            Console.WriteLine("Downloading: {0}", pkgVersion.remoteName);
                            await downloader.DownloadAsync(fileUrl, outputPath, true, null, null, DownloadProgressDelegateAsync, Environment.ProcessorCount);

                            byte[] remoteHash = Convert.FromHexString(pkgVersion.md5);

                            using FileStream fileStream = File.OpenRead(outputPath);
                            byte[] currentHash = await MD5.HashDataAsync(fileStream);

                            if (!currentHash.AsSpan().SequenceEqual(remoteHash))
                                throw new Exception();
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine($"Cancelled!");
                }
            }
        }

        private static async IAsyncEnumerable<PkgVersion> DeserializePkgVersion(Stream stream)
        {
            StreamReader pkgVersionTextReader = new StreamReader(stream);
            while (!pkgVersionTextReader.EndOfStream)
            {
                yield return JsonSerializer.Deserialize(await pkgVersionTextReader.ReadLineAsync() ?? "", JsonContext.Default.PkgVersion)!;
            }
        }

        private static Stopwatch sw = Stopwatch.StartNew();
        private static long lastDownloaded = 0;

        private static void DownloadProgressDelegateAsync(int size, DownloadProgress bytesProgress)
        {
            lastDownloaded += size;
            double speed = lastDownloaded / sw.Elapsed.TotalSeconds;
            double unNan = (bytesProgress.BytesTotal - bytesProgress.BytesDownloaded) / speed;
            unNan = double.IsNaN(unNan) || double.IsInfinity(unNan) ? 0 : unNan;
            TimeSpan timeLeft = checked(TimeSpan.FromSeconds(unNan));
            const string format = "Read: {0:0.00} -> {1}% ({6}/s) {2} / {3} (in bytes: {4} / {5}) ({7})";
            string toWrite = string.Format(format,
                size,
                Math.Round((double)bytesProgress.BytesDownloaded / bytesProgress.BytesTotal * 100, 2),
                SummarizeSizeSimple(bytesProgress.BytesDownloaded),
                SummarizeSizeSimple(bytesProgress.BytesTotal),
                bytesProgress.BytesDownloaded,
                bytesProgress.BytesTotal,
                SummarizeSizeSimple(speed),
                string.Format("{0:%h}h{0:%m}m{0:%s}s left", timeLeft)
                );

            int spaceToWrite = Math.Max(0, Console.BufferWidth - 1 - toWrite.Length);
            toWrite += new string(' ', spaceToWrite) + '\r';
            Console.Write(toWrite);
        }

        public static string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

        public static string SummarizeSizeSimple(double value, int decimalPlaces = 2)
        {
            byte mag = (byte)Math.Log(value, 1000);

            return string.Format("{0} {1}", Math.Round(value / (1L << (mag * 10)), decimalPlaces), SizeSuffixes[mag]);
        }
    }
}
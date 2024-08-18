using Hi3Helper.Http;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Test
{
    internal class Program
    {
        static string URL = "https://autopatchos.zenlesszonezero.com/pclauncher/nap_global/audio_ja-jp_1.0.1_1.1.0_hdiff_icFimTTzuCqPhGgs.zip";
        static string Output = @".\Testing.diff";
        static string Output2 = @"C:\Users\neon-nyan\AppData\LocalLow\CollapseLauncher\GameFolder\Hi3TW\Games\BH3_v5.9.0_cba771e4ca76.7z.001";
        static string Output3 = @"C:\Users\neon-nyan\AppData\LocalLow\CollapseLauncher\GameFolder\Hi3TW\Games\BH3_v5.9.0_cba771e4ca76.7z.dummy";
        static string Output4 = @"C:\Users\neon-nyan\Downloads\bin\YuanShen_2.8.54_beta.zip";

        static async Task Main()
        {
            #region testing
            // Console.WriteLine("Downloading sample...");
            // if (File.Exists(Output))
            //     File.Delete(Output);

            // Output = Output4;
            // await Client.Download(URL, Output);
            /*

            Stopwatch sw = Stopwatch.StartNew();

            int buffLength = 4 << 20;
            using (FileStream fs = new FileStream(Output, FileMode.Open, FileAccess.Read))
            {
                int read;
                byte[] buf = new byte[buffLength];
                while ((read = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    sh64.ComputeHash32(buf, read);
                }
            }

            Console.WriteLine("SimpleChecksum (Int32/int mode): {0:00}:{1:00}:{2:00}.{3:00}", sw.Elapsed.Hours, sw.Elapsed.Minutes, sw.Elapsed.Seconds, sw.Elapsed.Milliseconds / 10);
            Console.WriteLine("Hash: {0}", sh64.HashString32);

            sw = Stopwatch.StartNew();

            sh64.Flush();
            using (FileStream fs = new FileStream(Output, FileMode.Open, FileAccess.Read))
            {
                int read;
                byte[] buf = new byte[buffLength];
                while ((read = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    sh64.ComputeHash64(buf, read);
                }
            }

            Console.WriteLine("SimpleChecksum (Int64/long mode): {0:00}:{1:00}:{2:00}.{3:00}", sw.Elapsed.Hours, sw.Elapsed.Minutes, sw.Elapsed.Seconds, sw.Elapsed.Milliseconds / 10);
            Console.WriteLine("Hash: {0}", sh64.HashString64);

            sw = Stopwatch.StartNew();

            crc = new();
            using (FileStream fs = new FileStream(Output, FileMode.Open, FileAccess.Read))
            {
                int read;
                byte[] buf = new byte[buffLength];
                while ((read = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    crc.TransformBlock(buf, 0, read, buf, 0);
                }
                crc.TransformFinalBlock(buf, 0, 0);
            }

            Console.WriteLine("Force's CRC32: {0:00}:{1:00}:{2:00}.{3:00}", sw.Elapsed.Hours, sw.Elapsed.Minutes, sw.Elapsed.Seconds, sw.Elapsed.Milliseconds / 10);
            Console.WriteLine("Hash: {0}", BytesToHex(crc.Hash));
            */
            #endregion
            /*
            long limit = 1200000000;
            byte[] buffer = new byte[4096];
            SimpleChecksum partialHash = new();

            using (Stream fs = new FileStream(Output4, FileMode.Open, FileAccess.Read))
            {
                int Read;
                int NextRead = buffer.Length;
                while ((Read = fs.Read(buffer, 0, NextRead)) > 0)
                {
                    NextRead = (int)Math.Min(buffer.Length, limit - fs.Position);
                    partialHash.ComputeHash32(buffer, Read);
                }
            }
            */
            while (true)
            {
                try
                {
                    using (HttpClientHandler handler = new HttpClientHandler())
                    using (HttpClient client = new HttpClient(handler, false))
                    using (MemoryStream stream = new MemoryStream())
                    using (BrotliStream encStream = new BrotliStream(stream, CompressionLevel.Fastest))
                    {
                        sw = Stopwatch.StartNew();
                        lastDownloaded = 0;
                        DownloadClient downloader = DownloadClient.CreateInstance(client);
                        DownloadClient.SetSharedDownloadSpeedLimit(1 << 20);
                        // await downloader.DownloadAsync(URL, Output, false, null, null, DownloadProgressDelegateAsync, 4, 8 << 20);

                        await downloader.DownloadAsync(URL, encStream, false, DownloadProgressDelegateAsync, null, null);
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine($"Cancelled!");
                }
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

            int spaceToWrite = Math.Max(0, toWrite.Length - Console.CursorLeft - 1);
            toWrite += new string(' ', spaceToWrite) + '\r';
            Console.Write(toWrite);
        }

        public static string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

        public static string SummarizeSizeSimple(double value, int decimalPlaces = 2)
        {
            byte mag = (byte)Math.Log(value, 1000);

            return string.Format("{0} {1}", Math.Round(value / (1L << (mag * 10)), decimalPlaces), SizeSuffixes[mag]);
        }

        private static async void WaitAndCancel()
        {
            await Task.Delay(250);
        }

        public static string BytesToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty);
    }
}
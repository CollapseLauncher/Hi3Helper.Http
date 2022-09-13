using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Hi3Helper.Http;
using Force.Crc32;

namespace Test
{
    internal class Program
    {
        static string URL = "https://github.com/neon-nyan/CollapseLauncher/releases/download/CL-v1.0.36.1-stable/CL-1.0.36.1_Portable.7z";
        static string Output = @".\Testing.7z";
        static string Output2 = @"C:\Users\neon-nyan\AppData\LocalLow\CollapseLauncher\GameFolder\Hi3TW\Games\BH3_v5.9.0_cba771e4ca76.7z.001";
        static string Output3 = @"C:\Users\neon-nyan\AppData\LocalLow\CollapseLauncher\GameFolder\Hi3TW\Games\BH3_v5.9.0_cba771e4ca76.7z.dummy";
        static string Output4 = @"C:\Users\neon-nyan\Downloads\bin\YuanShen_2.8.54_beta.zip";
        static Http Client = new();
        static Crc32Algorithm crc = new();
        static SimpleChecksum sh64 = new();
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
        }

        public static string BytesToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty);
    }
}
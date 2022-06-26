using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class Http
    {
        public async Task MergeMultithread(string OutPath, int Threads, CancellationToken Token)
        {
            int Read;
            byte[] Buffer = new byte[4 << 17];
            ResetSessionStopwatch();

            MetadataProp meta = ReadMetadataFile(OutPath);

            if (!meta.CanOverwrite && File.Exists(OutPath))
            {
                this.ThreadState = MultithreadState.FailedMerging;
                throw new HttpHelperThreadFileExist(
                    $"File is already exist and doesn't have attribute to be overwrite-able.");
            }

            if (meta.Threads != Threads)
            {
                this.ThreadState = MultithreadState.CancelledMerging;
                throw new HttpHelperThreadMetadataInvalid(
                    "Existing metadata for merging doesn't match"
                    + $"(Using {Threads} instead of {meta.Threads} from metadata)!");
            }

            this.SizeLastDownloaded = 0;
            this.SizeDownloaded = 0;

            try
            {
                using (Stream OutStream = new FileStream(OutPath, FileMode.Create, FileAccess.Write))
                {
                    for (int i = 0; i < Threads; i++)
                    {
                        string InPath = string.Format(OutPath + ".{0:000}", i + 1);
                        using (Stream InStream = new FileStream(InPath, FileMode.Open, FileAccess.Read, FileShare.None, 4 << 15, FileOptions.DeleteOnClose))
                        {
                            while ((Read = InStream.Read(Buffer)) > 0)
                            {
                                ThreadState = MultithreadState.Merging;
                                Token.ThrowIfCancellationRequested();
                                await OutStream.WriteAsync(Buffer, 0, Read, Token);
                                this.SizeLastDownloaded += Read;
                                this.SizeDownloaded += Read;

                                UpdateProgress(new DownloadEvent(this.SizeLastDownloaded, this.SizeDownloaded, this.SizeToBeDownloaded, Read, this.SessionStopwatch.Elapsed.TotalSeconds));
                            }
                        }
                    }
                }

                File.Delete(OutPath + ".h3mtd");
                Console.WriteLine();
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine($"Merging has been cancelled!");
                this.ThreadState = MultithreadState.CancelledMerging;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"Merging has been cancelled!");
                this.ThreadState = MultithreadState.CancelledMerging;
            }
            catch (Exception ex)
            {
                this.ThreadState = MultithreadState.FailedMerging;
                throw new Exception($"Unhandled exception while merging!\r\n{ex}", ex);
            }
        }

        public async void MergeMultithreadNoTask(string OutPath, int Threads, CancellationToken Token = new CancellationToken()) => await MergeMultithread(OutPath, Threads, Token);
    }
}

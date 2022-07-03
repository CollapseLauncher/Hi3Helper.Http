using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class Http
    {
        public async Task MergeMultisession(string OutPath, byte Sessions = 4, CancellationToken Token = new CancellationToken())
        {
            int Read;
            byte[] Buffer = new byte[4 << 17];
            ResetSessionStopwatch();

            MetadataProp meta = ReadMetadataFile(OutPath);

            if (!meta.CanOverwrite && File.Exists(OutPath))
            {
                this.SessionState = MultisessionState.FailedMerging;
                throw new HttpHelperSessionFileExist(
                    $"File is already exist and doesn't have attribute to be overwrite-able.");
            }

            if (meta.Sessions != Sessions)
            {
                this.SessionState = MultisessionState.CancelledMerging;
                throw new HttpHelperSessionMetadataInvalid(
                    "Existing metadata for merging doesn't match"
                    + $"(Using {Sessions} instead of {meta.Sessions} from metadata)!");
            }

            this.SizeLastDownloaded = 0;
            this.SizeDownloaded = 0;

            try
            {
                using (Stream OutStream = new FileStream(OutPath, FileMode.Create, FileAccess.Write))
                {
                    for (int i = 0; i < Sessions; i++)
                    {
                        string InPath = string.Format(OutPath + ".{0:000}", i + 1);
                        using (Stream InStream = new FileStream(InPath, FileMode.Open, FileAccess.Read, FileShare.None, 4 << 15, FileOptions.DeleteOnClose))
                        {
                            while ((Read = InStream.Read(Buffer, 0, Buffer.Length)) > 0)
                            {
                                SessionState = MultisessionState.Merging;
                                Token.ThrowIfCancellationRequested();
                                await OutStream.WriteAsync(Buffer, 0, Read, Token);
                                this.SizeLastDownloaded += Read;
                                this.SizeDownloaded += Read;

                                UpdateProgress(new DownloadEvent(this.SizeLastDownloaded, this.SizeDownloaded, this.SizeToBeDownloaded,
                                    Read, this.SessionStopwatch.Elapsed.TotalSeconds, this.SessionState));
                            }
                        }
                    }
                }

                File.Delete(OutPath + ".h3mtd");
            }
            catch (TaskCanceledException)
            {
                this.SessionState = MultisessionState.CancelledMerging;
                PushLog("Merging has been cancelled!", LogSeverity.Info);
            }
            catch (OperationCanceledException)
            {
                this.SessionState = MultisessionState.CancelledMerging;
                PushLog("Merging has been cancelled!", LogSeverity.Info);
            }
            catch (Exception ex)
            {
                this.SessionState = MultisessionState.FailedMerging;
                PushLog("Unhandled exception while merging has occured!\r\n{ex}", LogSeverity.Error);
                throw new HttpHelperUnhandledError($"Unhandled exception while merging has occured!\r\n{ex}", ex);
            }
        }

        public async void MergeMultisessionNoTask(string OutPath, byte Sessions, CancellationToken Token = new CancellationToken()) => await MergeMultisession(OutPath, Sessions, Token);
    }
}

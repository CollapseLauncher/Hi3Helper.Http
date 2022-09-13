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
            FileInfo OutFile = new FileInfo(OutPath);
            SessionState = MultisessionState.Merging;
            ResetSessionStopwatch();

            EnsureDisposeAllSessions();
            
            MetadataProp meta = ReadMetadataFile(OutPath);

            if (!meta.CanOverwrite && OutFile.Exists)
            {
                if (OutFile.Length == this.SizeToBeDownloaded)
                {
                    this.SessionState = MultisessionState.FailedMerging;
                    throw new HttpHelperSessionFileExist(
                        $"File is already exist and doesn't have attribute to be overwrite-able.");
                }
            }

            if (meta.ChunkSize != Sessions)
            {
                this.SessionState = MultisessionState.CancelledMerging;
                throw new HttpHelperSessionMetadataInvalid(
                    "Existing metadata for merging doesn't match"
                    + $"(Using {Sessions} instead of {meta.ChunkSize} from metadata)!");
            }

            this.SizeLastDownloaded = 0;
            this.SizeDownloaded = 0;

            try
            {
                using (Stream OutStream = OutFile.Create())
                {
                    for (int i = 0; i < Sessions; i++)
                    {
                        string InPath = string.Format(OutPath + ".{0:000}", i + 1);
                        using (Stream InStream = new FileStream(InPath, FileMode.Open, FileAccess.Read, FileShare.None, 4 << 10, FileOptions.DeleteOnClose))
                        {
                            while ((Read = await InStream.ReadAsync(Buffer, 0, Buffer.Length, Token)) > 0)
                            {
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

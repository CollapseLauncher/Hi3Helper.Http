using System.IO;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class Http
    {
        // Use ReadWriteStreamDisposable if OutStream from session is Disposable
        private async Task ReadWriteStreamDisposable(SessionAttribute Session)
        {
            await ReadWriteStream(Session);
        }

        // Use ReadWriteStreamDisposable if OutStream from session is not disposable
        private async Task ReadWriteStream(SessionAttribute Session) => await Task.Run(() => ReadWrite(Session));

        private void ReadWrite(SessionAttribute Session)
        {
            int Read;
            byte[] Buffer = new byte[4 << 20];

            // Reset SessionRetry counter while successfully get the Input Stream.
            Session.SessionRetry = 1;

            // Seek to the end of the OutStream
            SeekStreamToEnd(Session.OutStream);

            // Read and send it to buffer
            // Throw if the cancel has been sent from Token
            while ((Read = Session.InStream.Read(Buffer, 0, Buffer.Length)) > 0)
            {
                // Throw if Token has been called
                Session.SessionToken.ThrowIfCancellationRequested();
                // Set downloading state to Downloading
                Session.SessionState = MultisessionState.Downloading;
                // Write the buffer into OutStream
                Session.OutStream.Write(Buffer, 0, Read);
                // Update DownloadProgress
                this.SizeLastDownloaded += Read;
                // Increment the StartOffset
                Session.StartOffset += Read;

                this.L += Read;
                if (this.L >= 0x3200000)
                {
                    this.L = 0;
                    PushLog(string.Format("{0} reached -> Continuous: {1}", 0x3200000, Session.OutStream.Length), LogSeverity.Warning);
                    throw new IOException("test");
                }

                // Use UpdateProgress in ReadWrite() for SingleSession download only
                if (!Session.IsMultisession)
                    UpdateProgress(new DownloadEvent(this.SizeLastDownloaded, Session.OutSize, this.SizeToBeDownloaded,
                        Read, this.SessionStopwatch.Elapsed.TotalSeconds, this.SessionState));
            }
        }

        private long L;

        // Seek the Stream to the end of it.
        private Stream SeekStreamToEnd(Stream S)
        {
            S.Seek(0, SeekOrigin.End);

            return S;
        }

        // Dispose all session streams for Multisession download.
        // This will be called if the session has finished or even failed
        public void DisposeAllMultisessionStream()
        {
            foreach (SessionAttribute Session in SessionAttributes) Session.DisposeOutStream();
        }

        public partial class SessionAttribute
        {
            public void DisposeOutStream()
            {
                if (this.IsOutDisposable) OutStream?.Dispose();
            }
        }
    }
}

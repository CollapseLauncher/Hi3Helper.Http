using System.IO;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class Http
    {
        private async Task StartWriteSession(SessionAttribute Session)
        {
            int Read;
            byte[] Buffer = new byte[4 << 20];

            // Reset SessionRetry counter while successfully get the Input Stream.
            Session.SessionRetry = 1;

            // Seek to the end of the OutStream
            SeekStreamToEnd(Session.OutStream);

            // Read and send it to buffer
            // Throw if the cancel has been sent from Token
            while ((Read = await Session.InStream.ReadAsync(Buffer, 0, Buffer.Length, Session.SessionToken)) > 0)
            {
                // Set downloading state to Downloading
                Session.SessionState = MultisessionState.Downloading;
                // Write the buffer into OutStream
                Session.OutStream.Write(Buffer, 0, Read);
                // Update DownloadProgress
                this.SizeLastDownloaded += Read;
                // Increment the StartOffset
                Session.StartOffset += Read;

                // Use UpdateProgress in ReadWrite() for SingleSession download only
                if (!Session.IsMultisession)
                    UpdateProgress(new DownloadEvent(this.SizeLastDownloaded, Session.OutSize, this.SizeToBeDownloaded,
                        Read, this.SessionStopwatch.Elapsed.TotalSeconds, this.SessionState));
            }
        }

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
            // Dispose InHttp Response and Request to prevent request flooding.
            public void DisposeInHttp()
            {
                RemoteRequest?.Dispose();
                RemoteResponse?.Dispose();
            }

            // Dispose OutStream
            public void DisposeOutStream()
            {
                if (this.IsOutDisposable) OutStream?.Dispose();
            }
        }
    }
}

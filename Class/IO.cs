using System.IO;

namespace Hi3Helper.Http
{
    public partial class Http
    {
        // Use ReadWriteStreamDisposable if OutStream from session is Disposable
        private void ReadWriteStreamDisposable(SessionAttribute Session)
        {
            using (Session.InStream)
            using (Session.OutStream)
                ReadWriteStream(Session);
        }

        // Use ReadWriteStreamDisposable if OutStream from session is not disposable
        private void ReadWriteStream(SessionAttribute Session) => ReadWrite(Session);

        private void ReadWrite(SessionAttribute Session)
        {
            int Read;
            byte[] Buffer = new byte[4 << 20];

            // Reset CurrentRetry counter while successfully get the Input Stream.
            CurrentRetry = 1;

            // Seek to the end of the OutStream
            SeekStreamToEnd(Session.OutStream);

            // Read and send it to buffer
            while ((Read = Session.InStream.Read(Buffer)) > 0)
            {
                // Set downloading state to Downloading
                Session.SessionState = MultisessionState.Downloading;
                // Throw if the cancel has been sent from Token
                Session.SessionToken.ThrowIfCancellationRequested();
                // Write the buffer into OutStream
                Session.OutStream.Write(Buffer, 0, Read);
                // Update DownloadProgress
                this.SizeLastDownloaded += Read;
                this.SizeDownloaded += Read;

                UpdateProgress(new DownloadEvent(this.SizeLastDownloaded, this.SizeDownloaded, this.SizeToBeDownloaded,
                    Read, this.SessionStopwatch.Elapsed.TotalSeconds, this.SessionState));
            }
        }

        // Seek the Stream to the end of it.
        private Stream SeekStreamToEnd(Stream S)
        {
            S.Seek(0, SeekOrigin.End);

            return S;
        }

        public partial class SessionAttribute
        {
            public void DisposeOutStream()
            {
                if (OutStream != null) OutStream?.Dispose();
            }
        }
    }
}

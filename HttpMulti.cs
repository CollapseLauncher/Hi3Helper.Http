using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class HttpNew
    {
        public async Task Download(string URL, string Output, bool Overwrite = false,
            byte ConnectionSessions = 4, CancellationToken ThreadToken = new CancellationToken())
        {
            ResetState(false);

            this.PathURL = URL;
            this.PathOutput = Output;
            this.PathOverwrite = Overwrite;
            this.ConnectionToken = ThreadToken;
            this.ConnectionSessions = ConnectionSessions;

            if (ConnectionSessions > this.ConnectionSessionsMax)
                throw new HttpHelperAllowedSessionsMaxed($"You've maxed allowed Connection Sessions ({ConnectionSessions} sessions have been set and only <= {this.ConnectionSessionsMax} sessions allowed)");

            CheckForOldDifferentCSessions(this.PathOutput);

            await InitializeMultiSession();
            // await TryRunSessionVerification();
            await RunSessionTasks();

            ResetState(true);
        }

        private void CheckForOldDifferentCSessions(string OutPath)
        {
            string FolderPath = Path.GetDirectoryName(OutPath);
            string FileName = Path.GetFileName(OutPath);
            string[] Files = Directory.GetFiles(FolderPath, $"{FileName}.{PathExtPrefix}.*", SearchOption.TopDirectoryOnly);

            bool IsDelete = Files.Length > 0 && Files.Length != this.ConnectionSessions;

            if (IsDelete)
            {
                Array.ForEach<string>(Files, f =>
                {
                    FileInfo file = new FileInfo(f);
                    file.IsReadOnly = false;
                    file.Delete();
                });
                this.PathOverwrite = true;
            }
        }

        public async void DownloadAsync(string URL, string Output, bool Overwrite = false,
            byte ConnectionSessions = 4, CancellationToken ThreadToken = new CancellationToken()) =>
            await Download(URL, Output, Overwrite, ConnectionSessions, ThreadToken);
    }
}

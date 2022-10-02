using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class HttpNew
    {
        public async Task Download(string URL, string Output, bool Overwrite = false,
            byte ConnectionSessions = 4, CancellationToken ThreadToken = new CancellationToken())
        {
            this.PathURL = URL;
            this.PathOutput = Output;
            this.PathOverwrite = Overwrite;
            this.ConnectionToken = ThreadToken;
            this.ConnectionSessions = ConnectionSessions;

            if (ConnectionSessions > this.ConnectionSessionsMax)
                throw new HttpHelperAllowedSessionsMaxed($"You've maxed allowed Connection Sessions ({ConnectionSessions} sessions have been set and only <= {this.ConnectionSessionsMax} sessions allowed)");

            await InitializeMultiSession();
            // await TryRunSessionVerification();
            await RunSessionTasks();

            ResetState();
        }

        public async void DownloadAsync(string URL, string Output, bool Overwrite = false,
            byte ConnectionSessions = 4, CancellationToken ThreadToken = new CancellationToken()) =>
            await Download(URL, Output, Overwrite, ConnectionSessions, ThreadToken);

        public async Task WaitUntilAllSessionReady()
        {
            if (this.Sessions.Count == 0) return;
            if (this.ConnectionToken.IsCancellationRequested) return;

            while (this.Sessions.All(x => x.SessionState != MultisessionState.Downloading))
                await Task.Delay(33, this.ConnectionToken);
        }
    }
}

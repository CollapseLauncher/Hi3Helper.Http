using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class Http
    {
#if NETCOREAPP
        public void DownloadSync(string URL, string Output, byte ConnectionSessions = 4,
            bool Overwrite = false, CancellationToken ThreadToken = new CancellationToken())
        {
            ResetState();

            this.Sessions.Clear();
            this.PathURL = URL;
            this.PathOutput = Output;
            this.PathOverwrite = Overwrite;
            this.ConnectionToken = ThreadToken;
            this.ConnectionSessions = ConnectionSessions;

            if (ConnectionSessions > ConnectionSessionsMax)
                throw new HttpHelperAllowedSessionsMaxed($"You've maxed allowed Connection Sessions ({ConnectionSessions} sessions have been set and only <= {ConnectionSessionsMax} sessions allowed)");

            InitializeMultiSession();
            Task.WhenAll(RunMultiSessionTasks()).GetAwaiter().GetResult();

            this.DownloadState = DownloadState.FinishedNeedMerge;
        }
#endif

        public async Task Download(string URL, string Output, byte ConnectionSessions = 4,
            bool Overwrite = false, CancellationToken ThreadToken = new CancellationToken())
        {
            ResetState();

            this.Sessions.Clear();
            this.PathURL = URL;
            this.PathOutput = Output;
            this.PathOverwrite = Overwrite;
            this.ConnectionToken = ThreadToken;
            this.ConnectionSessions = ConnectionSessions;

            if (ConnectionSessions > ConnectionSessionsMax)
                throw new HttpHelperAllowedSessionsMaxed($"You've maxed allowed Connection Sessions ({ConnectionSessions} sessions have been set and only <= {ConnectionSessionsMax} sessions allowed)");

#if NETCOREAPP
            await Task.Run(InitializeMultiSession);
#else
            await InitializeMultiSession();
#endif
            await Task.WhenAll(RunMultiSessionTasks());

            this.DownloadState = DownloadState.FinishedNeedMerge;
        }

        public async void DownloadAsync(string URL, string Output, bool Overwrite = false,
            byte ConnectionSessions = 4, CancellationToken ThreadToken = new CancellationToken()) =>
            await Download(URL, Output, ConnectionSessions, Overwrite, ThreadToken);
    }
}

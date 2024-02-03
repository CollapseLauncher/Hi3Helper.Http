using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public sealed partial class Http
    {
        public async Task Download(string URL, string Output, byte ConnectionSessions = 4,
            bool Overwrite = false, CancellationToken ThreadToken = new CancellationToken())
        {
            ResetState();
            this.PathURL = URL;
            this.PathOutput = Output;
            this.PathOverwrite = Overwrite;
            this.ConnectionToken = ThreadToken;
            this.ConnectionSessions = ConnectionSessions;

            if (ConnectionSessions > ConnectionSessionsMax)
                throw new HttpHelperAllowedSessionsMaxed($"You've maxed allowed Connection Sessions ({ConnectionSessions} sessions have been set and only <= {ConnectionSessionsMax} sessions allowed)");

#if NETCOREAPP
            await GetMultisessionTasks(URL, Output, ConnectionSessions, ThreadToken).TaskWhenAll(ThreadToken, ConnectionSessions);
#else
            Task[] parallelTasks = await GetMultisessionTasks(URL, Output, ConnectionSessions, ThreadToken);
            await Task.WhenAll(parallelTasks);
#endif

            this.DownloadState = DownloadState.FinishedNeedMerge;
        }
    }
}

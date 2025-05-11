using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http.Legacy
{
    public sealed partial class Http
    {
        public async Task Download(string url, string output, int connectionSessions = 4,
            bool overwrite = false, CancellationToken threadToken = default)
        {
            ResetState();
            _pathURL            = url;
            _pathOutput         = output;
            _connectionSessions = connectionSessions;

            if (connectionSessions > ConnectionSessionsMax)
                throw new HttpHelperAllowedSessionsMaxed($"You've maxed allowed Connection Sessions ({connectionSessions} sessions have been set and only <= {ConnectionSessionsMax} sessions allowed)");

            await TaskWhenAllSession(GetMultisessionTasks(url, output, connectionSessions, overwrite, threadToken), threadToken, connectionSessions);

            DownloadState = DownloadState.FinishedNeedMerge;
        }
    }
}
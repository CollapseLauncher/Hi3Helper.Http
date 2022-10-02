using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;

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
    }
}

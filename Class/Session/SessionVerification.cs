using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;

namespace Hi3Helper.Http
{
    public partial class Http
    {
        /*
        public async Task TryRunSessionVerification()
        {
            List<Task> VerifyTask = new List<Task>();

            foreach (Session session in this.Sessions)
            {
                VerifyTask.Add(Task.Run(async () =>
                {
                    if (!await RunSessionVerification(session))
                    {
                        PushLog($"Session: {PathOutput} seems to be corrupted. Redownloading it!", LogSeverity.Warning);
                        ReinitializeSession(session);
                    }
                }));
            }

            MonitorSessionVerifyProgress();
            await Task.WhenAll(VerifyTask);
        }

        public async Task<bool> RunSessionVerification(Session Input)
        {
            if (!Input.FileOutput.Exists) return false;
            return await Task.Run(() => IOMultiVerify(Input));
        }

        public async void MonitorSessionVerifyProgress()
        {
            
        }
        */
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class HttpNew
    {
        public async Task RunSessionTasks()
        {
            List<Task> tasks = new List<Task>();

            foreach (Session session in this.Sessions)
            {
                tasks.Add(RetryableContainer(session));
            }

            await Task.WhenAll(tasks);
        }

        private async Task RetryableContainer(Session session)
        {
            using (session)
            {
                bool StillRetry = true;
                while (StillRetry)
                {
                    session.SessionRetryAttempt++;
                    try
                    {
                        await Task.Run(() => IOReadWriteMulti(session));
                        StillRetry = false;
                    }
                    catch (Exception)
                    {
                        if (session.SessionRetryAttempt > this.RetryMax)
                        {
                            throw;
                        }
                    }
                }
            }
        }
    }
}

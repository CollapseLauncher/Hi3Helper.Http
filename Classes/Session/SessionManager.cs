using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.Net.Http;
using static Hi3Helper.Http.Http;

namespace Hi3Helper.Http
{
    public partial class HttpNew
    {
        private async Task InitializeMultiSession()
        {
            this.DownloadState = MultisessionState.WaitingOnSession;

            if (this.IsSessionContinue = LoadMetadata()) return;

            this.SizeTotal = await TryGetContentLength(this.PathURL, this.ConnectionToken);
            long SliceSize = (long)Math.Ceiling((double)this.SizeTotal / this.ConnectionSessions);
            long EndOffset;
            
            for (long StartOffset = 0, t = 0; t < this.ConnectionSessions; t++)
            {
                EndOffset = t + 1 == this.ConnectionSessions ? this.SizeTotal : (StartOffset + SliceSize - 1);
                this.Sessions.Add(new Session(
                    this.PathURL, this.PathOutput + string.Format(".{0:000}", t + 1), null,
                    this.ConnectionToken, true, true,
                    StartOffset, EndOffset, this.PathOverwrite)
                {
                    IsLastSession = t + 1 == this.ConnectionSessions,
                    IsCompleted = false,
                    SessionSize = EndOffset - StartOffset
                });

                StartOffset += SliceSize;
            }

            CreateOrUpdateMetadata();
        }

        public void ReinitializeSession(Session Input) => 
            Input = new Session(
                this.PathURL, this.PathOutput, null,
                this.ConnectionToken, true, true,
                Input.OffsetStart, Input.OffsetEnd, this.PathOverwrite
                )
            {
                IsLastSession = Input.IsLastSession,
                IsCompleted = false,
                SessionSize = (Input.OffsetEnd - Input.OffsetStart) ?? 0
            };

        private async Task<long> TryGetContentLength(string URL, CancellationToken Token)
        {
            byte CurrentRetry = 0;
            while (true)
            {
                try
                {
                    return await GetContentLength(URL, Token) ?? 0;
                }
                catch (HttpRequestException)
                {
                    CurrentRetry++;
                    if (CurrentRetry > this.RetryMax)
                        throw;

                    PushLog($"Error while fetching File Size (Retry Attempt: {CurrentRetry})...", LogSeverity.Warning);
                    await Task.Delay(this.RetryInterval, Token);
                }
            }
        }

        public async Task<long?> GetContentLength(string Input, CancellationToken token = new CancellationToken())
        {
            HttpResponseMessage response = await _client.SendAsync(new HttpRequestMessage() { RequestUri = new Uri(Input) }, HttpCompletionOption.ResponseHeadersRead, token);

            long? Length = response.Content.Headers.ContentLength;

            response.Dispose();

            return Length;
        }
    }
}

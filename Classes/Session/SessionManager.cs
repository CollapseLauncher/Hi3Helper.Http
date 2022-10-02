using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class HttpNew
    {
        private async Task InitializeMultiSession()
        {
            this.SizeAttribute = new AttributesSize();
            this.DownloadState = MultisessionState.WaitingOnSession;
            string PathOut;

            // if (this.IsSessionContinue = LoadMetadata()) return;

            long? RemoteLength = await TryGetContentLength(this.PathURL, this.ConnectionToken);

            if (RemoteLength is null)
                throw new NullReferenceException($"File can't be downloaded because the content-length is undefined!");

            this.SizeAttribute.SizeTotalToDownload = (long)RemoteLength;

            long SliceSize = (long)Math.Ceiling((double)this.SizeAttribute.SizeTotalToDownload / this.ConnectionSessions);
            long EndOffset;

            for (long StartOffset = 0, t = 0; t < this.ConnectionSessions; t++)
            {
                EndOffset = t + 1 == this.ConnectionSessions ? this.SizeAttribute.SizeTotalToDownload : (StartOffset + SliceSize - 1);
                PathOut = this.PathOutput + string.Format(".{0:000}", t + 1);
                Session session = new Session(
                    this.PathURL, PathOut, null,
                    this.ConnectionToken, true, true,
                    StartOffset, EndOffset, this.PathOverwrite)
                {
                    IsLastSession = t + 1 == this.ConnectionSessions,
                    SessionSize = EndOffset - StartOffset
                };

                bool IsSetRequestSuccess = session.TrySetHttpRequest(),
                     IsSetRequestOffsetSuccess = false,
                     IsSetResponseSuccess = false;

                if (IsSetRequestSuccess)
                {
                    IsSetRequestOffsetSuccess = session.TrySetHttpRequestOffset();
                }

                if (IsSetRequestOffsetSuccess)
                {
                    IsSetResponseSuccess = await session.TrySetHttpResponse(this._client);
                }

                if (IsSetResponseSuccess)
                {
                    this.Sessions.Add(session);
                }

                IncrementDownloadedSize(session);

                StartOffset += SliceSize;
            }

            this.IsDownloadContinue = this.SizeAttribute.SizeDownloaded > 0;

            // CreateOrUpdateMetadata();
        }

        private void IncrementDownloadedSize(Session session) => this.SizeAttribute.SizeDownloaded += session.StreamOutputSize;

        public void ReinitializeSession(Session Input) =>
            Input = new Session(
                this.PathURL, this.PathOutput, null,
                this.ConnectionToken, true, true,
                Input.OffsetStart, Input.OffsetEnd, this.PathOverwrite
                )
            {
                IsLastSession = Input.IsLastSession,
                SessionSize = (Input.OffsetEnd - Input.OffsetStart) ?? 0
            };

        private async Task<long?> TryGetContentLength(string URL, CancellationToken Token)
        {
            byte CurrentRetry = 0;
            while (true)
            {
                try
                {
                    return await GetContentLength(URL, Token);
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

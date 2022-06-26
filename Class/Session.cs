using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class Http
    {
        private async Task<bool> GetSession(SessionAttribute Session)
        {
            Session.RemoteRequest = new HttpRequestMessage()
            {
                RequestUri = new Uri(Session.InURL),
                Method = HttpMethod.Get,
            };

            Session.RemoteRequest.Headers.Range = new RangeHeaderValue(Session.StartOffset, Session.EndOffset);

            HttpResponseMessage Response = await SendAsync(Session.RemoteRequest, HttpCompletionOption.ResponseHeadersRead, Session.SessionToken);

            if ((int)Response.StatusCode == 416)
            {
                this.SizeDownloaded += Session.OutSize;
                this.SizeToBeDownloaded += Session.OutSize;
                UpdateProgress(new DownloadEvent(0, this.SizeDownloaded, this.SizeToBeDownloaded, this.SizeDownloaded, this.SessionStopwatch.Elapsed.TotalSeconds));
                return false;
            }

            Session.RemoteResponse = Session.CheckHttpResponseCode(Response);

            this.SizeDownloaded += Session.OutSize;
            this.SizeToBeDownloaded += (Session.RemoteResponse.Content.Headers.ContentLength ?? 0) + Session.OutSize;

            return true;
        }

        private async Task<bool> GetSessionMultisession(SessionAttribute Session)
        {
            Session.RemoteRequest = new HttpRequestMessage()
            {
                RequestUri = new Uri(Session.InURL),
                Method = HttpMethod.Get,
            };

            if ((Session.IsLastSession ? Session.EndOffset - 1 : Session.EndOffset) - Session.StartOffset < 0
                && (Session.IsLastSession ? Session.EndOffset - 1 : Session.EndOffset) - Session.StartOffset == -1)
            {
                UpdateProgress(new DownloadEvent(0, this.SizeDownloaded, this.SizeToBeDownloaded, Session.OutSize, SessionStopwatch.Elapsed.TotalSeconds));
                return false;
            }

            Session.RemoteRequest.Headers.Range = new RangeHeaderValue(Session.StartOffset, Session.IsLastSession ? Session.EndOffset - 1 : Session.EndOffset);

            HttpResponseMessage Response = await SendAsync(Session.RemoteRequest, HttpCompletionOption.ResponseHeadersRead, Session.SessionToken);
            
            if ((int)Response.StatusCode == 416)
            {
                UpdateProgress(new DownloadEvent(0, Session.OutSize, this.SizeToBeDownloaded, Session.OutSize, SessionStopwatch.Elapsed.TotalSeconds));
                return false;
            }

            Session.RemoteResponse = Session.CheckHttpResponseCode(Response);

            return true;
        }

        private async Task StartSession(SessionAttribute Session)
        {
            // Run Synchronous code as Asynchronous
            await Task.Run(() =>
            {
                if (Session.IsOutDisposable)
                    ReadWriteStreamDisposable(Session);
                else
                    ReadWriteStream(Session);
            }).ConfigureAwait(false);
        }

        private void ResetSessionStopwatch() => this.SessionStopwatch = Stopwatch.StartNew();

        public async Task<long?> GetContentLength(string Input, CancellationToken token = new CancellationToken())
        {
            HttpResponseMessage response = await SendAsync(new HttpRequestMessage() { RequestUri = new Uri(Input) }, HttpCompletionOption.ResponseHeadersRead, token);

            return response.Content.Headers.ContentLength;
        }
    }
}

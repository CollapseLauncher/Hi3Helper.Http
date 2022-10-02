﻿using System;
using System.Linq;
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
                PathOut = this.PathOutput + string.Format(this.PathExtSessionPrefix, t + 1);
                Session session = new Session(
                    this.PathURL, PathOut, null,
                    this.ConnectionToken, true, true,
                    StartOffset, EndOffset, this.PathOverwrite)
                {
                    IsLastSession = t + 1 == this.ConnectionSessions,
                    SessionSize = EndOffset - StartOffset
                };

                if (session.IsExistingFileOversized(StartOffset, EndOffset))
                    session = ReinitializeSession(session, true, true, StartOffset, EndOffset);

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

                IncrementDownloadedSize(session);

                if (IsSetResponseSuccess)
                {
                    session.SeekStreamOutputToEnd();
                    this.Sessions.Add(session);
                }
                else session.Dispose();

                StartOffset += SliceSize;
            }

            if (this.Sessions.Count == 0)
                this.DownloadState = MultisessionState.Idle;

            if (this.SizeAttribute.SizeDownloaded == this.SizeAttribute.SizeTotalToDownload)
                this.DownloadState = MultisessionState.FinishedNeedMerge;
            else
                this.DownloadState = MultisessionState.Downloading;

            this.IsDownloadContinue = this.SizeAttribute.SizeDownloaded > 0;
            // CreateOrUpdateMetadata();
        }

        public async Task WaitUntilAllSessionReady()
        {
            if (this.ConnectionToken.IsCancellationRequested) return;

            while (this.Sessions.All(x => x.SessionState != MultisessionState.Downloading)
               && !this.ConnectionToken.IsCancellationRequested
               && !this.InnerConnectionTokenSource.Token.IsCancellationRequested)
            {
                if (this.DownloadState == MultisessionState.Idle) return;
                await Task.Delay(33, this.ConnectionToken);
            }
        }

        public async Task WaitUntilAllSessionDownloaded()
        {
            if (this.Sessions.Count == 0) return;
            if (this.ConnectionToken.IsCancellationRequested) return;
            if (this.DownloadState == MultisessionState.Idle
             || this.DownloadState == MultisessionState.WaitingOnSession)
                throw new InvalidOperationException("You couldn't be able to use this method before all sessions are ready!");

            while (this.Sessions.Any(x => x.SessionState == MultisessionState.Downloading)
               && !this.ConnectionToken.IsCancellationRequested
               && !this.InnerConnectionTokenSource.Token.IsCancellationRequested
               && this.Sessions.Count != 0)
                await Task.Delay(33, this.ConnectionToken);
        }

        private void IncrementDownloadedSize(Session session) => this.SizeAttribute.SizeDownloaded += session.StreamOutputSize;

        public Session ReinitializeSession(Session Input, bool IsMultiSession, bool ForceOverwrite = false,
            long? GivenOffsetStart = null, long? GivenOffsetEnd = null)
        {
            Input.Dispose();
            return new Session(
                this.PathURL, Input.PathOutput, null,
                this.ConnectionToken, true, IsMultiSession,
                ForceOverwrite ? GivenOffsetStart : Input.OffsetStart,
                ForceOverwrite ? GivenOffsetEnd : Input.OffsetStart,
                ForceOverwrite || this.PathOverwrite
                )
            {
                IsLastSession = Input.IsLastSession,
                SessionSize = (Input.OffsetEnd - Input.OffsetStart) ?? 0
            };
        }

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

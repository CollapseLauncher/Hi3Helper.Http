using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public sealed partial class Http
    {
#if NETCOREAPP
        private async ValueTask<Session> InitializeSingleSession(long? OffsetStart, long? OffsetEnd, bool IsFileMode = true, Stream _Stream = null, bool IgnoreOutStreamLength = false)
#else
        private async Task<Session> InitializeSingleSession(long? OffsetStart, long? OffsetEnd, bool IsFileMode = true, Stream _Stream = null, bool IgnoreOutStreamLength = false)
#endif
        {
            this.SizeAttribute.SizeTotalToDownload = 0;
            this.SizeAttribute.SizeDownloaded = 0;
            this.SizeAttribute.SizeDownloadedLast = 0;

            this.DownloadState = DownloadState.WaitingOnSession;

            Session session = new Session(this.PathURL, this.PathOutput, _Stream,
                this.ConnectionToken, IsFileMode, this._handler,
                OffsetStart, OffsetEnd, this.PathOverwrite, this._clientUserAgent, true, IgnoreOutStreamLength);

            session.SessionRequest = new HttpRequestMessage()
            {
                RequestUri = new Uri(this.PathURL),
                Method = HttpMethod.Get
            };

            if (!!(session.OffsetEnd - session.OffsetStart < 0
                && session.OffsetEnd - session.OffsetStart == -1))
                return null;

            session.SessionRequest.Headers.Range = new RangeHeaderValue(session.OffsetStart, session.OffsetEnd);

            HttpResponseMessage Input = await this._client.SendAsync(session.SessionRequest, HttpCompletionOption.ResponseHeadersRead, session.SessionToken);
            if (!Input.IsSuccessStatusCode)
            {
                session.Dispose();
                throw new HttpHelperUnhandledError(string.Format("HttpResponse for URL: \"{1}\" has returned unsuccessful code: {0}", Input.StatusCode, this.PathURL));
            }

            session.SessionResponse = Input;

            if ((int)Input.StatusCode == 416) return null;

            this.SizeAttribute.SizeDownloaded = session.StreamOutputSize;

            if (session.SessionResponse.Content.Headers.ContentLength == null)
            {
                this.SizeAttribute.SizeTotalToDownload = 0;
            }
            else
            {
                this.SizeAttribute.SizeTotalToDownload = (session.SessionResponse.Content.Headers.ContentLength ?? 0) + session.StreamOutputSize;
            }

            session.SessionClient = this._client;

            return session;
        }

        private async Task InitializeMultiSession()
        {
            bool IsInitSucceeded = true;

            this.SizeAttribute.SizeTotalToDownload = 0;
            this.SizeAttribute.SizeDownloaded = 0;
            this.SizeAttribute.SizeDownloadedLast = 0;

            this.DownloadState = DownloadState.WaitingOnSession;
            string PathOut;

            Session session = null;
            try
            {
                long? RemoteLength = await TryGetContentLength(this.PathURL, this.ConnectionToken);

                if (RemoteLength == null)
                {
                    throw new NullReferenceException($"File can't be downloaded because the content-length is undefined!");
                }

                this.SizeAttribute.SizeTotalToDownload = (long)RemoteLength;

                long SliceSize = (long)Math.Ceiling((double)this.SizeAttribute.SizeTotalToDownload / this.ConnectionSessions);
                long EndOffset;

                for (long StartOffset = 0, t = 0; t < this.ConnectionSessions; t++)
                {
                    long ID = GetHashNumber(this.ConnectionSessions, t);
                    EndOffset = t + 1 == this.ConnectionSessions ? this.SizeAttribute.SizeTotalToDownload - 1 : (StartOffset + SliceSize - 1);
                    PathOut = this.PathOutput + string.Format(PathSessionPrefix, ID);

                    session = new Session(
                        this.PathURL, PathOut, null,
                        this.ConnectionToken, true, this._handler,
                        StartOffset, EndOffset, this.PathOverwrite,
                        this._clientUserAgent, false)
                    {
                        IsLastSession = t + 1 == this.ConnectionSessions,
                        SessionID = ID
                    };

                    long _Start = StartOffset;
                    StartOffset += SliceSize;

                    if (session.IsExistingFileOversized(_Start, EndOffset))
                    {
                        session = ReinitializeSession(session, true, _Start, EndOffset);
                    }

                    bool IsSetRequestSuccess = session.TrySetHttpRequest(),
                         IsSetRequestOffsetSuccess = false,
                         IsSetResponseSuccess = false;

                    if (IsSetRequestSuccess)
                    {
                        IsSetRequestOffsetSuccess = session.TrySetHttpRequestOffset();
                    }

                    if (IsSetRequestOffsetSuccess)
                    {
                        IsSetResponseSuccess = await session.TrySetHttpResponse();
                    }

                    this.SizeAttribute.SizeDownloaded += session.StreamOutputSize;

                    if (session.StreamOutputSize == (EndOffset - _Start) + 1)
                    {
                        PushLog($"Session ID: {ID} will be skipped because the session has already been downloaded!", DownloadLogSeverity.Warning);
                        session?.Dispose();
                        continue;
                    }

                    if (IsSetResponseSuccess)
                    {
                        session.SeekStreamOutputToEnd();
                        this.Sessions.Add(session);
                    }
                    else
                    {
                        throw new HttpHelperUnhandledError($"{ID} PANIC as the response sent {session.SessionResponse.StatusCode} code!!!");
                    }
                }
            }
            catch (TaskCanceledException)
            {
                IsInitSucceeded = false;
                throw;
            }
            catch (OperationCanceledException)
            {
                IsInitSucceeded = false;
                throw;
            }
            catch (Exception ex)
            {
                PushLog($"Session initialization cannot be completed due to an error!\r\n{ex}", DownloadLogSeverity.Error);
                IsInitSucceeded = false;
                throw;
            }
            finally
            {
                if (!IsInitSucceeded)
                {
                    session?.Dispose();
                    DisposeAllSessions();
                    PushLog($"Session has been disposed during initialization!", DownloadLogSeverity.Error);
                }
            }

            if (this.Sessions.Count == 0)
                this.DownloadState = DownloadState.Idle;

            if (this.SizeAttribute.SizeDownloaded == this.SizeAttribute.SizeTotalToDownload)
                this.DownloadState = DownloadState.FinishedNeedMerge;
            else
                this.DownloadState = DownloadState.Downloading;
        }

        public void DisposeAllSessions() => this.Sessions?.ForEach(x => x.Dispose());

        public async Task WaitUntilInstanceDisposed()
        {
            while (!this.IsDisposed)
            {
                await Task.Delay(10);
            }
        }

        private Session ReinitializeSession(Session Input, bool ForceOverwrite = false,
            long? GivenOffsetStart = null, long? GivenOffsetEnd = null)
        {
            Input?.Dispose();
            return new Session(
                this.PathURL, Input.PathOutput, null,
                this.ConnectionToken, true, this._handler,
                ForceOverwrite ? GivenOffsetStart : Input.OffsetStart,
                ForceOverwrite ? GivenOffsetEnd : Input.OffsetStart,
                ForceOverwrite || this.PathOverwrite, this._clientUserAgent
                )
            {
                IsLastSession = Input.IsLastSession,
            };
        }

        public void DeleteMultisessionFiles(string Path, byte Sessions)
        {
            string SessionFilePath;
            string SessionFilePathLegacy;
            for (int t = 0; t < Sessions; t++)
            {
                SessionFilePath = Path + string.Format(PathSessionPrefix, GetHashNumber(Sessions, t));
                SessionFilePathLegacy = Path + string.Format(".{0:000}", t + 1);
                try
                {
                    FileInfo fileInfo = new FileInfo(SessionFilePath);
                    FileInfo fileInfoLegacy = new FileInfo(SessionFilePathLegacy);
                    if (fileInfo.Exists)
                    {
                        fileInfo.IsReadOnly = false;
                        fileInfo.Delete();
                    }
                    if (fileInfoLegacy.Exists)
                    {
                        fileInfoLegacy.IsReadOnly = false;
                        fileInfoLegacy.Delete();
                    }
                }
                catch { }
            }
        }

        public long CalculateExistingMultisessionFilesWithExpctdSize(string Path, byte Sessions, long ExpectedSize)
        {
            long Ret = 0;
            string SessionFilePath;
            FileInfo parentFile = new FileInfo(Path);
            if (parentFile.Exists)
            {
                if (parentFile.Length == ExpectedSize)
                    return parentFile.Length;
            }

            for (int t = 0; t < Sessions; t++)
            {
                SessionFilePath = Path + string.Format(PathSessionPrefix, GetHashNumber(Sessions, t));
                try
                {
                    FileInfo fileInfo = new FileInfo(SessionFilePath);
                    if (fileInfo.Exists) Ret += fileInfo.Length;
                }
                catch { }
            }

            return Ret;
        }

#if NETCOREAPP
        public async ValueTask<(int, bool)> GetURLStatus(string URL, CancellationToken Token)
#else
        public async Task<(int, bool)> GetURLStatus(string URL, CancellationToken Token)
#endif
        {
            using (HttpResponseMessage response = await _client.SendAsync(new HttpRequestMessage() { RequestUri = new Uri(URL) }, HttpCompletionOption.ResponseHeadersRead, Token))
            {
                return ((int)response.StatusCode, response.IsSuccessStatusCode);
            }
        }

#if NETCOREAPP
        public async ValueTask<long?> TryGetContentLength(string URL, CancellationToken Token)
#else
        public async Task<long?> TryGetContentLength(string URL, CancellationToken Token)
#endif
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

                    PushLog($"Error while fetching File Size (Retry Attempt: {CurrentRetry})...", DownloadLogSeverity.Warning);
                    await Task.Delay(this.RetryInterval, Token);
                }
            }
        }

#if NETCOREAPP
        private async ValueTask<long?> GetContentLength(string Input, CancellationToken token = new CancellationToken())
#else
        private async Task<long?> GetContentLength(string Input, CancellationToken token = new CancellationToken())
#endif
        {
            HttpResponseMessage response = await _client.SendAsync(new HttpRequestMessage() { RequestUri = new Uri(Input) }, HttpCompletionOption.ResponseHeadersRead, token);
            long? Length = response.Content.Headers.ContentLength;

            response.Dispose();

            return Length;
        }
    }
}

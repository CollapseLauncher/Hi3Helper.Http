﻿using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    public partial class Http
    {
#if NETCOREAPP
        private Session InitializeSingleSession(long? OffsetStart, long? OffsetEnd, bool IsFileMode = true, Stream _Stream = null)
#else
        private async Task<Session> InitializeSingleSession(long? OffsetStart, long? OffsetEnd, bool IsFileMode = true, Stream _Stream = null)
#endif
        {
            this.SizeAttribute.SizeTotalToDownload = 0;
            this.SizeAttribute.SizeDownloaded = 0;
            this.SizeAttribute.SizeDownloadedLast = 0;

            this.DownloadState = DownloadState.WaitingOnSession;

            Session session = new Session(this.PathURL, this.PathOutput, _Stream,
                this.ConnectionToken, IsFileMode, this._handler,
                OffsetStart, OffsetEnd, this.PathOverwrite, this._clientUserAgent, true);

            session.SessionRequest = new HttpRequestMessage()
            {
                RequestUri = new Uri(this.PathURL),
                Method = HttpMethod.Get
            };

            if (!!(session.OffsetEnd - session.OffsetStart < 0
                && session.OffsetEnd - session.OffsetStart == -1))
                return null;

            session.SessionRequest.Headers.Range = new RangeHeaderValue(session.OffsetStart, session.OffsetEnd);

#if NETCOREAPP           
            HttpResponseMessage Input = this._client.Send(session.SessionRequest, HttpCompletionOption.ResponseHeadersRead, session.SessionToken);
#else
            HttpResponseMessage Input = await this._client.SendAsync(session.SessionRequest, HttpCompletionOption.ResponseHeadersRead, session.SessionToken);
#endif
            if (!Input.IsSuccessStatusCode)
            {
                session.Dispose();
                throw new HttpHelperUnhandledError(string.Format("HttpResponse has returned unsuccessful code: {0}", Input.StatusCode));
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

#if NETCOREAPP           
        private void InitializeMultiSession()
#else
        private async Task InitializeMultiSession()
#endif
        {
            bool IsInitSucceeded = false;

            this.SizeAttribute.SizeTotalToDownload = 0;
            this.SizeAttribute.SizeDownloaded = 0;
            this.SizeAttribute.SizeDownloadedLast = 0;

            this.DownloadState = DownloadState.WaitingOnSession;
            string PathOut;

            // if (this.IsSessionContinue = LoadMetadata()) return;

            Session session = null;
            try
            {
#if NETCOREAPP
                long? RemoteLength = TryGetContentLength(this.PathURL, this.ConnectionToken);
#else
                long? RemoteLength = await TryGetContentLength(this.PathURL, this.ConnectionToken);
#endif

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

                    if (session.IsExistingFileOversized(StartOffset, EndOffset))
                    {
                        session = ReinitializeSession(session, true, StartOffset, EndOffset);
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
#if NETCOREAPP
                        IsSetResponseSuccess = session.TrySetHttpResponse();
#else
                        IsSetResponseSuccess = await session.TrySetHttpResponse();
#endif
                    }

                    IncrementDownloadedSize(session);

                    if (IsSetResponseSuccess)
                    {
                        session.SeekStreamOutputToEnd();
                        this.Sessions.Add(session);
                    }
                    else
                    {
                        throw new HttpHelperUnhandledError($"{ID} PANIC as the response sent {session.SessionResponse.StatusCode} code!!!");
                    }

                    StartOffset += SliceSize;

                    IsInitSucceeded = true;
                }
            }
            catch (TaskCanceledException ex)
            {
                throw ex;
            }
            catch (OperationCanceledException ex)
            {
                throw ex;
            }
            catch (IOException ex)
            {
                PushLog($"Session initialization cannot be completed due to an error!\r\n{ex}", DownloadLogSeverity.Error);
                throw ex;
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

            // CreateOrUpdateMetadata();
        }

        public void DisposeAllSessions() => this.Sessions?.ForEach(x => x.Dispose());

        public async Task WaitUntilInstanceDisposed()
        {
            while (!this.IsDisposed)
            {
                await Task.Delay(10);
            }
        }

        private void IncrementDownloadedSize(Session session) => this.SizeAttribute.SizeDownloaded += session.StreamOutputSize;

        private Session ReinitializeSession(Session Input, bool ForceOverwrite = false,
            long? GivenOffsetStart = null, long? GivenOffsetEnd = null)
        {
            Input.Dispose();
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

        public long CalculateExistingMultisessionFiles(string Path, byte Sessions)
        {
            long Ret = 0;
            string SessionFilePath;
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
        public long? TryGetContentLength(string URL, CancellationToken Token)
#else
        public async Task<long?> TryGetContentLength(string URL, CancellationToken Token)
#endif
        {
            byte CurrentRetry = 0;
            while (true)
            {
                try
                {
#if NETCOREAPP
                    return GetContentLength(URL, Token);
#else
                    return await GetContentLength(URL, Token);
#endif
                }
                catch (HttpRequestException)
                {
                    CurrentRetry++;
                    if (CurrentRetry > this.RetryMax)
                        throw;

                    PushLog($"Error while fetching File Size (Retry Attempt: {CurrentRetry})...", DownloadLogSeverity.Warning);
#if NETCOREAPP
                    Task.Delay(this.RetryInterval, Token).GetAwaiter().GetResult();
#else
                    await Task.Delay(this.RetryInterval, Token);
#endif
                }
            }
        }

#if NETCOREAPP
        private long? GetContentLength(string Input, CancellationToken token = new CancellationToken())
        {
            HttpResponseMessage response = _client.Send(new HttpRequestMessage() { RequestUri = new Uri(Input) }, HttpCompletionOption.ResponseHeadersRead, token);
#else
            private async Task<long?> GetContentLength(string Input, CancellationToken token = new CancellationToken())
        {
            HttpResponseMessage response = await _client.SendAsync(new HttpRequestMessage() { RequestUri = new Uri(Input) }, HttpCompletionOption.ResponseHeadersRead, token);
#endif

            long? Length = response.Content.Headers.ContentLength;

            response.Dispose();

            return Length;
        }
    }
}

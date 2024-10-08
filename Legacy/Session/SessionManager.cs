﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http.Legacy
{
    public sealed partial class Http
    {
#if NET6_0_OR_GREATER
        private async ValueTask<Session> InitializeSingleSession(long? offsetStart,
                                                                 long? offsetEnd,
                                                                 string? pathOutput = null,
                                                                 bool isOverwrite = false,
                                                                 Stream? stream = null,
                                                                 bool ignoreOutStreamLength = false,
                                                                 CancellationToken token = default)
#else
        private async Task<Session> InitializeSingleSession(long? offsetStart, long? offsetEnd, string pathOutput = null, bool isOverwrite = false, Stream stream = null, bool ignoreOutStreamLength = false, CancellationToken token = default)
#endif
        {
            this.SizeAttribute.SizeTotalToDownload = 0;
            this.SizeAttribute.SizeDownloaded = 0;
            this.SizeAttribute.SizeDownloadedLast = 0;

            this.DownloadState = DownloadState.WaitingOnSession;

            token.ThrowIfCancellationRequested();

            Session session = new Session(this.PathURL,
                this._handler, offsetStart, offsetEnd,
                this._clientUserAgent, true, this._client, ignoreOutStreamLength);
                session.SessionClient = this._client;

            if (string.IsNullOrEmpty(pathOutput) && stream == null)
                throw new ArgumentNullException(nameof(pathOutput), $"You cannot put PathOutput and _Stream argument both on null!");

            if (stream == null)
                await session.AssignOutputStreamFromFile(isOverwrite, pathOutput!, ignoreOutStreamLength);
            else
                session.AssignOutputStreamFromStream(stream, ignoreOutStreamLength);

            if (!await session.TryGetHttpRequest(token))
            {
#if NET6_0_OR_GREATER
                await session.DisposeAsync();
#else
                session.Dispose();
#endif
                throw new HttpRequestException();
            }

            if ((int)session.StreamInput._statusCode == 416) throw new HttpRequestException("Http session returned 416!");
            this.SizeAttribute.SizeTotalToDownload = session.IsFileMode ? session.StreamInput.Length + session.StreamOutputSize : session.StreamInput.Length;
            this.SizeAttribute.SizeDownloaded = session.StreamOutputSize;

            return session;
        }

        private async
            IAsyncEnumerable<Session>
            GetMultisessionTasks(string inputUrl, string outputPath, int sessionThread, bool isOverwrite,
            [EnumeratorCancellation]
            CancellationToken token)
        {
            this.SizeAttribute.SizeTotalToDownload = 0;
            this.SizeAttribute.SizeDownloaded = 0;
            this.SizeAttribute.SizeDownloadedLast = 0;

            this.DownloadState = DownloadState.WaitingOnSession;

            long minimumAllowedLength = (1 << 10) * sessionThread;
            long remoteLength = await GetContentLengthNonNull(inputUrl, token);
            if (remoteLength < minimumAllowedLength) throw new NullReferenceException($"The file size to be downloaded must be more than or equal minimum allowed size: {minimumAllowedLength}!");

            this.SizeAttribute.SizeTotalToDownload = remoteLength;
            long sliceSize = (long)Math.Ceiling((double)remoteLength / sessionThread);

            for (
                long startOffset = 0, endOffset = 0, currentThread = 0;
                currentThread < sessionThread;
                currentThread++
                )
            {
                bool isInitSucceed = true;
                Session session = null!;
                try
                {
                    long sessionId = GetHashNumber(sessionThread, currentThread);
                    string sessionOutPathOld = outputPath + string.Format(PathSessionPrefix, sessionId);
                    string sessionOutPathNew = outputPath + string.Format(".{0:000}", currentThread + 1);

                    endOffset = currentThread + 1 == sessionThread ? remoteLength - 1 : (startOffset + sliceSize - 1);
                    session = new Session(
                        inputUrl, this._handler, startOffset, endOffset,
                        this._clientUserAgent, true, this._client)
                    {
                        IsLastSession = currentThread + 1 == this.ConnectionSessions,
                        SessionID = sessionId
                    };
                    session.SessionState = DownloadState.WaitingOnSession;

                    long lastStartOffset = startOffset;
                    startOffset += sliceSize;
                    string toOutputPath = File.Exists(sessionOutPathOld) ? sessionOutPathOld : sessionOutPathNew;
                    await session.AssignOutputStreamFromFile(isOverwrite, toOutputPath, false);

                    if (session.IsExistingFileOversized(lastStartOffset, endOffset))
                    {
                        session =
#if NET6_0_OR_GREATER
                            await
#endif
                            ReinitializeSession(session, token, true, lastStartOffset, endOffset);

                        await session.AssignOutputStreamFromFile(true, toOutputPath, false);
                        PushLog($"Session ID: {sessionId} output file has been re-created due to the size being oversized!", DownloadLogSeverity.Warning);
                    }

                    this.SizeAttribute.SizeDownloaded += session.StreamOutputSize;
                    bool isRequestSuccess = true;
                    if ((session.StreamOutputSize == (endOffset - lastStartOffset) + 1)
                     || (!(isRequestSuccess = await session.TryGetHttpRequest(token)) && (int)session.StreamInput._statusCode == 413))
                    {
                        PushLog($"Session ID: {sessionId} will be skipped because the session has already been downloaded!", DownloadLogSeverity.Warning);
                        isInitSucceed = false;
                        continue;
                    }

                    if (!isRequestSuccess)
                        throw new HttpRequestException($"Error has occurred while requesting HTTP response to {inputUrl} with status code: {(int)session.StreamInput._statusCode} ({session.StreamInput._statusCode})");
                }
                catch (Exception ex)
                {
                    this.DownloadState = DownloadState.FailedDownloading;
                    session.SessionState = DownloadState.FailedDownloading;
                    PushLog($"Session initialization cannot be completed due to an error!\r\n{ex}", DownloadLogSeverity.Error);
                    isInitSucceed = false;
                    throw;
                }
                finally
                {
                    if (!isInitSucceed && session != null)
                    {
#if NET6_0_OR_GREATER
                        await session.DisposeAsync();
#else
                        session.Dispose();
#endif
                        PushLog($"Session has been disposed during initialization!", DownloadLogSeverity.Error);
                    }
                }

                PushLog($"Session: {currentThread + 1}/{sessionThread} has been started for the URL: {inputUrl}", DownloadLogSeverity.Info);
                if (isInitSucceed) yield return session;
                if (session.StreamOutputSize > sliceSize)
                    throw new Exception();
            }
        }

        [Obsolete("This method has no use anymore. Please consider to not calling this method as this will be removed in the next changes.")]
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task WaitUntilInstanceDisposed() { }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

        private
#if NET6_0_OR_GREATER
            async ValueTask<Session>
#else
            Session
#endif
            ReinitializeSession(Session Input, CancellationToken Token,
            bool ForceOverwrite = false, long? GivenOffsetStart = null, long? GivenOffsetEnd = null)
        {
            if (Input == null) throw new NullReferenceException("Input session cannot be null while reinitialization is requested!");
#if NET6_0_OR_GREATER
            await Input.DisposeAsync();
#else
            Input.Dispose();
#endif
            return new Session(
                Input.PathURL, this._handler, ForceOverwrite ? GivenOffsetStart : Input.OffsetStart,
                ForceOverwrite ? GivenOffsetEnd : Input.OffsetStart, this._clientUserAgent,
                true, this._client
                )
            {
                IsLastSession = Input.IsLastSession,
            };
        }

        public static void DeleteMultisessionFiles(string Path, byte Sessions)
        {
            string SessionFilePath;
            string SessionFilePathLegacy;
            for (int t = 0; t < Sessions; t++)
            {
                SessionFilePath = Path + string.Format(".{0:000}", t + 1);
                SessionFilePathLegacy = Path + string.Format(PathSessionPrefix, GetHashNumber(Sessions, t));
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

        public static long CalculateExistingMultisessionFilesWithExpctdSize(string Path, byte Sessions, long ExpectedSize)
        {
            long Ret = 0;
            string SessionFilePath;
            string SessionFilePathLegacy;
            FileInfo parentFile = new FileInfo(Path);
            if (parentFile.Exists)
            {
                if (parentFile.Length == ExpectedSize)
                    return parentFile.Length;
            }

            for (int t = 0; t < Sessions; t++)
            {
                SessionFilePath = Path + string.Format(".{0:000}", t + 1);
                SessionFilePathLegacy = Path + string.Format(PathSessionPrefix, GetHashNumber(Sessions, t));
                try
                {
                    FileInfo fileInfo = new FileInfo(SessionFilePath);
                    FileInfo fileInfoLegacy = new FileInfo(SessionFilePathLegacy);
                    if (fileInfo.Exists) Ret += fileInfo.Length;
                    else if (fileInfoLegacy.Exists) Ret += fileInfoLegacy.Length;
                }
                catch { }
            }

            return Ret;
        }

#if NET6_0_OR_GREATER
        public async ValueTask<Tuple<int, bool>> GetURLStatus(string URL, CancellationToken Token)
#else
        public async Task<Tuple<int, bool>> GetURLStatus(string URL, CancellationToken Token)
#endif
        {
            using (HttpResponseMessage response = await _client.SendAsync(new HttpRequestMessage() { RequestUri = new Uri(URL) }, HttpCompletionOption.ResponseHeadersRead, Token))
            {
                return new Tuple<int, bool>((int)response.StatusCode, response.IsSuccessStatusCode);
            }
        }

#if NET6_0_OR_GREATER
        public async ValueTask<long> GetContentLengthNonNull(string URL, CancellationToken Token)
#else
        public async Task<long> GetContentLengthNonNull(string URL, CancellationToken Token)
#endif
        {
            using (HttpRequestMessage message = new HttpRequestMessage() { RequestUri = new Uri(URL) })
            {
                using (HttpResponseMessage response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, Token))
                {
                    return response?.Content?.Headers?.ContentLength ?? 0;
                }
            }
        }

#if NET6_0_OR_GREATER
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

#if NET6_0_OR_GREATER
        private async ValueTask<long?> GetContentLength(string Input, CancellationToken token = new CancellationToken())
#else
        private async Task<long?> GetContentLength(string Input, CancellationToken token = new CancellationToken())
#endif
        {
            HttpRequestMessage message = new HttpRequestMessage() { RequestUri = new Uri(Input) };
            HttpResponseMessage response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
            long? Length = response.Content.Headers.ContentLength;

            message.Dispose();
            response.Dispose();

            return Length;
        }
    }
}

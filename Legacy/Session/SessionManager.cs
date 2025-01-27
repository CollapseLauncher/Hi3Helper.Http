using System;
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
            _sizeAttribute.SizeTotalToDownload = 0;
            _sizeAttribute.SizeDownloaded = 0;
            _sizeAttribute.SizeDownloadedLast = 0;

            DownloadState = DownloadState.WaitingOnSession;

            token.ThrowIfCancellationRequested();

            Session session = new(_pathURL,
                                  _handler, offsetStart, offsetEnd,
                                  _clientUserAgent, true, _client);
                session.SessionClient = _client;

            if (string.IsNullOrEmpty(pathOutput) && stream == null)
                throw new ArgumentNullException(nameof(pathOutput), "You cannot put PathOutput and _Stream argument both on null!");

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

            if ((int)session.StreamInput.StatusCode == 416) throw new HttpRequestException("Http session returned 416!");
            _sizeAttribute.SizeTotalToDownload = session.IsFileMode ? session.StreamInput.Length + session.StreamOutputSize : session.StreamInput.Length;
            _sizeAttribute.SizeDownloaded = session.StreamOutputSize;

            return session;
        }

        private async
            IAsyncEnumerable<Session>
            GetMultisessionTasks(string inputUrl, string outputPath, int sessionThread, bool isOverwrite,
            [EnumeratorCancellation]
            CancellationToken token)
        {
            _sizeAttribute.SizeTotalToDownload = 0;
            _sizeAttribute.SizeDownloaded = 0;
            _sizeAttribute.SizeDownloadedLast = 0;

            DownloadState = DownloadState.WaitingOnSession;

            long minimumAllowedLength = (1 << 10) * sessionThread;
            long remoteLength = await GetContentLengthNonNull(inputUrl, token);
            if (remoteLength < minimumAllowedLength) throw new NullReferenceException($"The file size to be downloaded must be more than or equal minimum allowed size: {minimumAllowedLength}!");

            _sizeAttribute.SizeTotalToDownload = remoteLength;
            long sliceSize = (long)Math.Ceiling((double)remoteLength / sessionThread);

            for (
                // Build error when removed
                // ReSharper disable once RedundantAssignment
                long startOffset = 0, endOffset = 0, currentThread = 0;
                currentThread < sessionThread;
                currentThread++
                )
            {
                bool isInitSucceed = true;
                Session session = null!;
                try
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    long sessionId = GetHashNumber(sessionThread, currentThread);
#pragma warning restore CS0618 // Type or member is obsolete
                    string sessionOutPathOld = outputPath + string.Format(PathSessionPrefix, sessionId);
                    string sessionOutPathNew = outputPath + $".{currentThread + 1:000}";

                    endOffset = currentThread + 1 == sessionThread ? remoteLength - 1 : startOffset + sliceSize - 1;
                    session = new Session(
                        inputUrl, _handler, startOffset, endOffset,
                        _clientUserAgent, true, _client)
                    {
                        IsLastSession = currentThread + 1 == _connectionSessions,
                        SessionID = sessionId
                    };
                    session.SessionState = DownloadState.WaitingOnSession;

                    long lastStartOffset = startOffset;
                    startOffset += sliceSize;
                    string toOutputPath = File.Exists(sessionOutPathOld) ? sessionOutPathOld : sessionOutPathNew;
                    await session.AssignOutputStreamFromFile(isOverwrite, toOutputPath, false);

                    if (session.IsExistingFileOversize(lastStartOffset, endOffset))
                    {
                        session =
#if NET6_0_OR_GREATER
                            await
#endif
                            ReinitializeSession(session, token, true, lastStartOffset, endOffset);

                        await session.AssignOutputStreamFromFile(true, toOutputPath, false);
                        PushLog($"Session ID: {sessionId} output file has been re-created due to the size being oversized!", DownloadLogSeverity.Warning);
                    }

                    _sizeAttribute.SizeDownloaded += session.StreamOutputSize;
                    bool isRequestSuccess;
                    if (session.StreamOutputSize == endOffset - lastStartOffset + 1
                     || (!(isRequestSuccess = await session.TryGetHttpRequest(token)) && (int)session.StreamInput.StatusCode == 413))
                    {
                        PushLog($"Session ID: {sessionId} will be skipped because the session has already been downloaded!", DownloadLogSeverity.Warning);
                        isInitSucceed = false;
                        continue;
                    }

                    if (!isRequestSuccess)
                        throw new HttpRequestException($"Error has occurred while requesting HTTP response to {inputUrl} with status code: {(int)session.StreamInput.StatusCode} ({session.StreamInput.StatusCode})");
                }
                catch (Exception ex)
                {
                    DownloadState = DownloadState.FailedDownloading;
                    session.SessionState = DownloadState.FailedDownloading;
                    PushLog($"Session initialization cannot be completed due to an error!\r\n{ex}", DownloadLogSeverity.Error);
                    isInitSucceed = false;
                    throw;
                }
                finally
                {
                    if (!isInitSucceed)
                    {
#if NET6_0_OR_GREATER
                        await session.DisposeAsync();
#else
                        session.Dispose();
#endif
                        PushLog("Session has been disposed during initialization!", DownloadLogSeverity.Error);
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
            ReinitializeSession(Session input, CancellationToken _,
            bool forceOverwrite = false, long? givenOffsetStart = null, long? givenOffsetEnd = null)
        {
            if (input == null) throw new NullReferenceException("Input session cannot be null while reinitialization is requested!");
#if NET6_0_OR_GREATER
            await input.DisposeAsync();
#else
            Input.Dispose();
#endif
            return new Session(
                input.PathURL, _handler, forceOverwrite ? givenOffsetStart : input.OffsetStart,
                forceOverwrite ? givenOffsetEnd : input.OffsetStart, _clientUserAgent,
                true, _client
                )
            {
                IsLastSession = input.IsLastSession,
            };
        }

        public static void DeleteMultisessionFiles(string path, int sessions)
        {
            string sessionFilePath;
            string sessionFilePathLegacy;
            for (int t = 0; t < sessions; t++)
            {
                sessionFilePath = path + $".{t + 1:000}";
#pragma warning disable CS0618 // Type or member is obsolete
                sessionFilePathLegacy = path + string.Format(PathSessionPrefix, GetHashNumber(sessions, t));
#pragma warning restore CS0618 // Type or member is obsolete
                try
                {
                    FileInfo fileInfo       = new(sessionFilePath);
                    FileInfo fileInfoLegacy = new(sessionFilePathLegacy);
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
                catch (Exception ex)
                {
                    LogInvoker.PushLog($"Failed to delete {sessionFilePath} or {sessionFilePathLegacy}" +
                                       $"{ex}", DownloadLogSeverity.Error);
                }
            }
        }

        public static long CalculateExistingMultisessionFilesWithExpctdSize(string path, int sessions, long expectedSize)
        {
            long ret = 0;
            string sessionFilePath;
            string sessionFilePathLegacy;
            FileInfo parentFile = new(path);
            if (parentFile.Exists)
            {
                if (parentFile.Length == expectedSize)
                    return parentFile.Length;
            }

            for (int t = 0; t < sessions; t++)
            {
                sessionFilePath = path + $".{t + 1:000}";
#pragma warning disable CS0618 // Type or member is obsolete
                sessionFilePathLegacy = path + string.Format(PathSessionPrefix, GetHashNumber(sessions, t));
#pragma warning restore CS0618 // Type or member is obsolete
                try
                {
                    FileInfo fileInfo       = new(sessionFilePath);
                    FileInfo fileInfoLegacy = new(sessionFilePathLegacy);
                    if (fileInfo.Exists) ret            += fileInfo.Length;
                    else if (fileInfoLegacy.Exists) ret += fileInfoLegacy.Length;
                }
                catch (Exception ex)
                {
                    LogInvoker.PushLog($"Failed to calculate existing multi-session file size on {sessionFilePath} or {sessionFilePathLegacy}" +
                                       $"{ex}", DownloadLogSeverity.Error);
                }
            }

            return ret;
        }

#if NET6_0_OR_GREATER
        public async ValueTask<Tuple<int, bool>> GetURLStatus(string url, CancellationToken token)
#else
        public async Task<Tuple<int, bool>> GetURLStatus(string URL, CancellationToken Token)
#endif
        {
            using (HttpResponseMessage response = await _client.SendAsync(new HttpRequestMessage { RequestUri = new Uri(url) }, HttpCompletionOption.ResponseHeadersRead, token))
            {
                return new Tuple<int, bool>((int)response.StatusCode, response.IsSuccessStatusCode);
            }
        }

#if NET6_0_OR_GREATER
        public async ValueTask<long> GetContentLengthNonNull(string url, CancellationToken token)
#else
        public async Task<long> GetContentLengthNonNull(string URL, CancellationToken Token)
#endif
        {
            using (HttpRequestMessage message = new())
            {
                message.RequestUri = new Uri(url);
                using (HttpResponseMessage response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    return response.Content.Headers.ContentLength ?? 0;
                }
            }
        }

#if NET6_0_OR_GREATER
        public async ValueTask<long?> TryGetContentLength(string url, CancellationToken token)
#else
        public async Task<long?> TryGetContentLength(string URL, CancellationToken Token)
#endif
        {
            byte currentRetry = 0;
            while (true)
            {
                try
                {
                    return await GetContentLength(url, token);
                }
                catch (HttpRequestException)
                {
                    currentRetry++;
                    if (currentRetry > _retryMax)
                        throw;

                    PushLog($"Error while fetching File Size (Retry Attempt: {currentRetry})...", DownloadLogSeverity.Warning);
                    await Task.Delay(_retryInterval, token);
                }
            }
        }

#if NET6_0_OR_GREATER
        private async ValueTask<long?> GetContentLength(string input, CancellationToken token = new())
#else
        private async Task<long?> GetContentLength(string Input, CancellationToken token = new CancellationToken())
#endif
        {
            HttpRequestMessage  message  = new() { RequestUri = new Uri(input) };
            HttpResponseMessage response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
            long?               length   = response.Content.Headers.ContentLength;

            message.Dispose();
            response.Dispose();

            return length;
        }
    }
}

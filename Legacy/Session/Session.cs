using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

#if NET6_0_OR_GREATER
using System.Net;
#endif
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable CheckNamespace

namespace Hi3Helper.Http.Legacy
{
    internal sealed class Session : IDisposable
#if NET6_0_OR_GREATER
        , IAsyncDisposable
#endif
    {
#nullable enable
        internal Session(string pathUrl,
                         HttpClientHandler clientHandler,
                         long? offsetStart = null,
                         long? offsetEnd = null, 
                         string? userAgent = null, 
                         bool useExternalSessionClient = false,
                         HttpClient? externalSessionClient = null)
        {
            // Initialize Properties
            PathUrl = pathUrl;
            IsDisposed = false;
            IsUseExternalSession = useExternalSessionClient;
            SessionState = DownloadState.Idle;
            
            if (useExternalSessionClient && externalSessionClient == null)
                throw new HttpHelperSessionNotReady("External session is null!");
            
            SessionClient = useExternalSessionClient ? externalSessionClient! : 
                new HttpClient(clientHandler)
                {
                    Timeout = TimeSpan.FromSeconds(TaskExtensions.DefaultTimeoutSec)
#if NET6_0_OR_GREATER
                    ,
                    DefaultRequestVersion = HttpVersion.Version30,
                    DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
#endif
                };
            SessionId = 0;

            if (!useExternalSessionClient && userAgent != null)
            {
                SessionClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            }

            OffsetStart = offsetStart;
            OffsetEnd = offsetEnd;
        }
#nullable restore

        // Seek the StreamOutput to the end of file
        // ReSharper disable once MemberCanBePrivate.Global
        internal void SeekStreamOutputToEnd() => StreamOutput.Seek(0, SeekOrigin.End);

        internal async Task AssignOutputStreamFromFile(bool isOverwrite, string filePath, bool ignoreOutStreamLength)
        {
            IsFileMode = true;
            FileInfo fileInfo = new(filePath);
            if (isOverwrite)
                StreamOutput = await Http.NaivelyOpenFileStreamAsync(fileInfo, FileMode.Create, FileAccess.Write, FileShare.Write);
            else
            {
                StreamOutput = await Http.NaivelyOpenFileStreamAsync(fileInfo, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write);
            }

            SeekStreamOutputToEnd();
            AdjustOffsets(OffsetStart, OffsetEnd, ignoreOutStreamLength);
        }

        internal void AssignOutputStreamFromStream(Stream stream, bool ignoreOutStreamLength)
        {
#if NET8_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(stream);
#else
            if (stream == null!)
            {
                throw new ArgumentNullException(nameof(stream));
            }
#endif
            StreamOutput = stream;
            IsFileMode = false;

            AdjustOffsets(OffsetStart, OffsetEnd, ignoreOutStreamLength);
        }

        private void AdjustOffsets(long? start, long? end, bool ignoreOutStreamLength = false)
        {
            OffsetStart = (start ?? 0) + (ignoreOutStreamLength ? 0 : StreamOutputSize);
            OffsetEnd = end;
        }

#if NET6_0_OR_GREATER
        internal async ValueTask<Tuple<bool, Exception>> TryReinitializeRequest(CancellationToken token)
#else
        internal async Task<Tuple<bool, Exception>> TryReinitializeRequest(CancellationToken token)
#endif
        {
            try
            {
                if (StreamInput != null!)
#if NET6_0_OR_GREATER
                    await StreamInput.DisposeAsync();
#else
                    StreamInput.Dispose();
#endif

                return new Tuple<bool, Exception>(await TryGetHttpRequest(token), null!);
            }
            catch (Exception ex)
            {
                Http.PushLog($"Failed while reinitialize session ID: {SessionId}\r\n{ex}", DownloadLogSeverity.Error);
                return new Tuple<bool, Exception>(false, ex);
            }
        }

        internal async
#if NETCOREAPP
        ValueTask<bool>
#else
        Task<bool>
#endif
        TryGetHttpRequest(CancellationToken token)
        {
            if (!IsExistingFileSizeValid())
            {
                return false;
            }

            ActionTimeoutValueTaskCallback<HttpResponseInputStream> createStreamCallback =
                async innerToken =>
                    await HttpResponseInputStream.CreateStreamAsync(SessionClient, PathUrl, OffsetStart, OffsetEnd, null, null, SessionRetryAttempt, innerToken);

            StreamInput = await TaskExtensions.WaitForRetryAsync(() => createStreamCallback, fromToken: token);
            return StreamInput != null!;

        }

        internal bool IsExistingFileOversize(long offsetStart, long offsetEnd) => StreamOutputSize > offsetEnd + 1 - offsetStart;

        private bool IsExistingFileSizeValid() =>
            !((IsLastSession ? OffsetEnd - 1 : OffsetEnd) - OffsetStart < 0
           && (IsLastSession ? OffsetEnd - 1 : OffsetEnd) - OffsetStart == -1);

        // Implement Disposable for IDisposable
        ~Session()
        {
            if (IsDisposed) return;

            Dispose();
        }

#if NET6_0_OR_GREATER
        public async ValueTask DisposeAsync()
        {
            if (IsDisposed) return;

            try
            {
                if (IsFileMode && StreamOutput != null!) await StreamOutput.DisposeAsync();
                if (StreamInput != null!) await StreamInput.DisposeAsync();
            }
            catch (Exception ex)
            {
                Http.PushLog($"Exception while disposing session: {ex}", DownloadLogSeverity.Warning);
            }

            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
#endif

        public void Dispose()
        {
            if (IsDisposed) return;

            try
            {
                if (IsFileMode)
                    StreamOutput.Dispose();

                StreamInput.Dispose();
                if (IsUseExternalSession) SessionClient.Dispose();
            }
            catch (Exception ex)
            {
                Http.PushLog($"Exception while disposing session: {ex}", DownloadLogSeverity.Warning);
            }

            IsDisposed = true;
            GC.SuppressFinalize(this);
        }

        // Session Offset Properties
        internal long? OffsetStart;
        internal long? OffsetEnd;

        // Path Properties
        internal string PathUrl;

        // Boolean Properties
        internal bool IsUseExternalSession;
        internal bool IsLastSession;
        internal bool IsFileMode;
        internal bool IsDisposed;

        // Session Properties
        internal HttpClient    SessionClient;
        internal DownloadState SessionState;
        internal int           SessionRetryAttempt { get; set; }
        internal long          SessionId;

        // Stream Properties
        internal HttpResponseInputStream StreamInput  = null!;
        internal Stream                  StreamOutput = null!;
        internal long                    StreamOutputSize => StreamOutput.CanWrite || StreamOutput.CanRead ? StreamOutput.Length : 0;
    }
}

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable InconsistentNaming
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Local
// ReSharper disable UnusedMember.Global
// ReSharper disable ConvertToUsingDeclaration
// ReSharper disable IdentifierTypo
// ReSharper disable ArrangeObjectCreationWhenTypeEvident

namespace Hi3Helper.Http
{
    public class HttpResponseInputStream : Stream
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        private HttpRequestMessage _networkRequest;
        private HttpResponseMessage? _networkResponse;
        private Stream _networkStream;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

        private long _networkLength;
        private long _currentPosition;
        private HttpStatusCode _statusCode;
        private bool _isSuccessStatusCode;

        public static async Task<HttpResponseInputStream?> CreateStreamAsync(
            HttpClient client,
            string url,
            long? startOffset,
            long? endOffset,
            TimeSpan? timeoutInterval,
            TimeSpan? retryInterval,
            int? retryCount,
            CancellationToken token)
        {
            return await CreateStreamAsync(client, new Uri(url), startOffset, endOffset, timeoutInterval, retryInterval,
                retryCount, token);
        }

        public static async Task<HttpResponseInputStream?> CreateStreamAsync(
            HttpClient client,
            Uri url,
            long? startOffset,
            long? endOffset,
            TimeSpan? timeoutInterval,
            TimeSpan? retryInterval,
            int? retryCount,
            CancellationToken token)
        {
            startOffset ??= 0;

            int currentRetry = 0;
            retryCount ??= 5;
            timeoutInterval ??= TimeSpan.FromSeconds(10);
            retryInterval ??= TimeSpan.FromSeconds(1);

            CancellationTokenSource? timeoutToken = null;
            CancellationTokenSource? coopToken = null;
        Start:
            try
            {
                timeoutToken = new CancellationTokenSource(timeoutInterval.Value);
                coopToken = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken.Token, token);

                HttpResponseInputStream httpResponseInputStream = new HttpResponseInputStream();
                httpResponseInputStream._networkRequest = new HttpRequestMessage
                {
                    RequestUri = url,
                    Method = HttpMethod.Get
                };

                token.ThrowIfCancellationRequested();

                httpResponseInputStream._networkRequest.Headers.Range = new RangeHeaderValue(startOffset, endOffset);
                httpResponseInputStream._networkResponse = await client
                    .SendAsync(httpResponseInputStream._networkRequest, HttpCompletionOption.ResponseHeadersRead,
                        coopToken.Token);

                httpResponseInputStream._statusCode = httpResponseInputStream._networkResponse.StatusCode;
                httpResponseInputStream._isSuccessStatusCode =
                    httpResponseInputStream._networkResponse.IsSuccessStatusCode;
                if (httpResponseInputStream._isSuccessStatusCode)
                {
                    httpResponseInputStream._networkLength =
                        httpResponseInputStream._networkResponse.Content.Headers.ContentLength ?? 0;
                    httpResponseInputStream._networkStream = await httpResponseInputStream._networkResponse.Content
#if NET6_0_OR_GREATER
                        .ReadAsStreamAsync(token);
#else
                        .ReadAsStreamAsync();
#endif
                    return httpResponseInputStream;
                }

                if ((int)httpResponseInputStream._statusCode != 416)
                {
                    throw new HttpRequestException(string.Format(
                        "HttpResponse for URL: \"{1}\" has returned unsuccessful code: {0}",
                        httpResponseInputStream._networkResponse.StatusCode, url));
                }

#if NET6_0_OR_GREATER
                await httpResponseInputStream.DisposeAsync();
#else
                httpResponseInputStream.Dispose();
#endif
                return null;
            }
            catch (TaskCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception)
            {
                currentRetry++;
                if (currentRetry > retryCount)
                {
                    throw;
                }

                await Task.Delay(retryInterval.Value, token);
                goto Start;
            }
            finally
            {
                timeoutToken?.Dispose();
                coopToken?.Dispose();
            }
        }

        ~HttpResponseInputStream()
        {
            Dispose();
        }


#if NET6_0_OR_GREATER
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int read = await _networkStream.ReadAsync(buffer, cancellationToken);
            _currentPosition += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            int read = _networkStream.Read(buffer);
            _currentPosition += read;
            return read;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            throw new NotSupportedException();
        }
#endif

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken)
        {
            int read = await _networkStream.ReadAsync(buffer, offset, count, cancellationToken);
            _currentPosition += read;
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _networkStream.Read(buffer, offset, count);
            _currentPosition += read;
            return read;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override void Flush()
        {
            _networkStream.Flush();
        }

        public override long Length => _networkLength;

        public override long Position
        {
            get => _currentPosition;
            set => throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            _networkRequest.Dispose();
            _networkResponse?.Dispose();
            _networkStream?.Dispose();
        }

#if NET6_0_OR_GREATER
        public override async ValueTask DisposeAsync()
        {
            _networkRequest.Dispose();
            _networkResponse?.Dispose();
            if (_networkStream != null)
                await _networkStream.DisposeAsync();

            GC.SuppressFinalize(this);
        }
#endif
    }
}
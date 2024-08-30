using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
// ReSharper disable UnusedMember.Global

namespace Hi3Helper.Http
{
    internal static class Utility
    {
        internal static Uri ToUri(this string? urlString)
        {
            if (!Uri.TryCreate(urlString, UriKind.RelativeOrAbsolute, out Uri? url))
                throw new InvalidOperationException($"URL string is not a valid url: {urlString}");

            return url;
        }

        internal static async ValueTask<long> GetUrlContentLengthAsync(
            this Uri uri,
            HttpClient client,
            int retryCount,
            TimeSpan retryInterval,
            TimeSpan timeoutInterval,
            CancellationToken token)
        {
            int currentRetry = 0;
        Start:
            HttpRequestMessage request = new HttpRequestMessage
            {
                RequestUri = uri
            };
            HttpResponseMessage? message = null;

            CancellationTokenSource cancelTimeoutToken = new CancellationTokenSource(timeoutInterval);
            CancellationTokenSource coopToken = CancellationTokenSource.CreateLinkedTokenSource(cancelTimeoutToken.Token, token);

            try
            {
                request.Headers.Range = new RangeHeaderValue(0, null);
                message = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, coopToken.Token);
                return message.Content.Headers.ContentLength ?? 0;
            }
            catch (TaskCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                currentRetry++;
                if (currentRetry > retryCount)
                    throw;

                await Task.Delay(retryInterval, token);
                goto Start;
            }
            finally
            {
                request.Dispose();
                message?.Dispose();
                cancelTimeoutToken.Dispose();
                coopToken.Dispose();
            }
        }

        internal static bool IsStreamCanSeeLength(this Stream stream)
        {
            try
            {
                _ = stream.Length;
                return true;
            }
            catch { return false; }
        }
    }
}

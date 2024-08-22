using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Http
{
    /// <summary>
    /// The callback to get the download progress of a file.
    /// </summary>
    /// <param name="size">Current size written to the target stream.</param>
    /// <param name="bytesProgress">The struct which consist the amount of data downloaded.</param>
    public delegate void DownloadProgressDelegate(int size, DownloadProgress bytesProgress);

    /// <summary>
    /// The class which consist the amount of data downloaded.
    /// </summary>
    public class DownloadProgress
    {
        /// <summary>
        /// The amount of data already downloaded.
        /// </summary>
        public long BytesDownloaded;

        /// <summary>
        /// The amount of data to be downloaded in total.
        /// </summary>
        public long BytesTotal;

        /// <summary>
        /// Increment the current <seealso cref="BytesDownloaded"/> field.
        /// </summary>
        /// <param name="size">How many bytes to increment the <seealso cref="BytesDownloaded"/> field.</param>
        internal void AdvanceBytesDownloaded(long size)
            => Interlocked.Add(ref BytesDownloaded, size);

        /// <summary>
        /// Set the value of <seealso cref="BytesTotal"/> field.
        /// </summary>
        /// <param name="size">Value to be set to <seealso cref="BytesTotal"/> field.</param>
        internal void SetBytesTotal(long size)
            => BytesTotal = size;
    }
}

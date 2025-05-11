using System;
// ReSharper disable UnusedType.Global

namespace Hi3Helper.Http
{
    /// <summary>
    /// The delegate of the speed change listener.
    /// </summary>
    /// <param name="sender">The object where the event is called from.</param>
    /// <param name="newRequestedSpeed">The new requested speed.</param>
    public delegate void DownloadSpeedLimiterChangeEventListener(object? sender, long newRequestedSpeed);
    public class DownloadSpeedLimiter
    {
        internal event EventHandler<long>? DownloadSpeedChangedEvent;
        // ReSharper disable once MemberCanBePrivate.Global
        internal long? InitialRequestedSpeed { get; set; }
        private EventHandler<long>? InnerListener { get; set; }

        private DownloadSpeedLimiter(long initialRequestedSpeed)
        {
            InitialRequestedSpeed = initialRequestedSpeed;
        }

        /// <summary>
        /// Create an instance by its initial speed to request.
        /// </summary>
        /// <param name="initialSpeed">The initial speed to be requested</param>
        /// <returns>An instance of the speed limiter</returns>
        public static DownloadSpeedLimiter CreateInstance(long initialSpeed)
            => new(initialSpeed);

        /// <summary>
        /// Get the listener for the parent event
        /// </summary>
        /// <returns>The EventHandler of the listener.</returns>
        /// <seealso cref="EventHandler"/>
        public EventHandler<long> GetListener() => InnerListener ??= DownloadSpeedChangeListener;

        private void DownloadSpeedChangeListener(object? sender, long newRequestedSpeed)
        {
            DownloadSpeedChangedEvent?.Invoke(this, newRequestedSpeed);
            InitialRequestedSpeed = newRequestedSpeed;
        }
    }
}

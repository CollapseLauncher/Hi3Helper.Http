using System;

namespace Hi3Helper.Http
{
    public class HttpHelperSessionNotReady : Exception
    {
        public HttpHelperSessionNotReady(string message) : base(message) { }
    }

    public class HttpHelperAllowedSessionsMaxed : Exception
    {
        public HttpHelperAllowedSessionsMaxed(string message) : base(message) { }
    }

    public class HttpHelperUnhandledError : Exception
    {
        public HttpHelperUnhandledError(string message) : base(message) { }
    }
}

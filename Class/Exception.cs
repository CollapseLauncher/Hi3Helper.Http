using System;

namespace Hi3Helper.Http
{
    [Serializable]
    public class HttpHelperSessionNotReady : Exception
    {
        public HttpHelperSessionNotReady(string message) : base(message) { }
    }

    public class HttpHelperAllowedSessionsMaxed : Exception
    {
        public HttpHelperAllowedSessionsMaxed(string message) : base(message) { }
    }

    public class HttpHelperSessionHTTPError416 : Exception
    {
        public HttpHelperSessionHTTPError416(string message) : base(message) { }
    }

    public class HttpHelperSessionMetadataNotExist : Exception
    {
        public HttpHelperSessionMetadataNotExist(string message) : base(message) { }
    }

    public class HttpHelperSessionMetadataInvalid : Exception
    {
        public HttpHelperSessionMetadataInvalid(string message) : base(message) { }
    }

    public class HttpHelperSessionFileExist : Exception
    {
        public HttpHelperSessionFileExist(string message) : base(message) { }
    }

    public class HttpHelperUnhandledError : Exception
    {
        public HttpHelperUnhandledError(string message, Exception ex) : base(message, ex) { }
    }
}

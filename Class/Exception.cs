using System;

namespace Hi3Helper.Http
{
    [Serializable]
    public class HttpHelperSessionNotReady : Exception
    {
        public HttpHelperSessionNotReady() { }
        public HttpHelperSessionNotReady(string message) : base(message) { }
    }

    public class HttpHelperAllowedSessionsMaxed : Exception
    {
        public HttpHelperAllowedSessionsMaxed() { }
        public HttpHelperAllowedSessionsMaxed(string message) : base(message) { }
    }

    public class HttpHelperSessionChunkOversize : Exception
    {
        public HttpHelperSessionChunkOversize() { }
        public HttpHelperSessionChunkOversize(string message) : base(message) { }
    }

    public class HttpHelperResponseNoSatisfiable : Exception
    {
        public HttpHelperResponseNoSatisfiable() { }
        public HttpHelperResponseNoSatisfiable(string message) : base(message) { }
    }

    public class HttpHelperSessionMetadataNotExist : Exception
    {
        public HttpHelperSessionMetadataNotExist() { }
        public HttpHelperSessionMetadataNotExist(string message) : base(message) { }
    }

    public class HttpHelperSessionMetadataInvalid : Exception
    {
        public HttpHelperSessionMetadataInvalid() { }
        public HttpHelperSessionMetadataInvalid(string message) : base(message) { }
    }

    public class HttpHelperSessionFileExist : Exception
    {
        public HttpHelperSessionFileExist() { }
        public HttpHelperSessionFileExist(string message) : base(message) { }
    }
}

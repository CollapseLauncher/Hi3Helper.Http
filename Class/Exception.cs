using System;

namespace Hi3Helper.Http
{
    [Serializable]
    public class HttpHelperSessionNotReady : Exception
    {
        public HttpHelperSessionNotReady() { }
        public HttpHelperSessionNotReady(string message) : base(message) { }
    }

    public class HttpHelperAllowedThreadsMaxed : Exception
    {
        public HttpHelperAllowedThreadsMaxed() { }
        public HttpHelperAllowedThreadsMaxed(string message) : base(message) { }
    }

    public class HttpHelperThreadChunkOversize : Exception
    {
        public HttpHelperThreadChunkOversize() { }
        public HttpHelperThreadChunkOversize(string message) : base(message) { }
    }

    public class HttpHelperResponseNoSatisfiable : Exception
    {
        public HttpHelperResponseNoSatisfiable() { }
        public HttpHelperResponseNoSatisfiable(string message) : base(message) { }
    }

    public class HttpHelperThreadMetadataNotExist : Exception
    {
        public HttpHelperThreadMetadataNotExist() { }
        public HttpHelperThreadMetadataNotExist(string message) : base(message) { }
    }

    public class HttpHelperThreadMetadataInvalid : Exception
    {
        public HttpHelperThreadMetadataInvalid() { }
        public HttpHelperThreadMetadataInvalid(string message) : base(message) { }
    }

    public class HttpHelperThreadFileExist : Exception
    {
        public HttpHelperThreadFileExist() { }
        public HttpHelperThreadFileExist(string message) : base(message) { }
    }
}

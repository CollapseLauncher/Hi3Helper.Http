using System;
using System.Collections.Generic;
using System.Text;

namespace Hi3Helper.Http
{
    internal class AttributesSize
    {
        public long SizeTotalToDownload { get; set; }
        public long SizeCurrent { get; set; }
        public long SizeDownloaded { get; set; }
        public long SizeDownloadedLast { get; set; }
    }
}

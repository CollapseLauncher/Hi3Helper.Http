namespace Hi3Helper.Http
{
    internal class ChunkRange
    {
        public long Start { get; set; }
        public long End { get; set; }

        internal void AdvanceStartOffset(long read) => Start += read;
    }
}

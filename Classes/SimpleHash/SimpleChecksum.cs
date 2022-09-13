using System;

namespace Hi3Helper.Http
{
    public class SimpleChecksum
    {
        public long Hash64 { get; private set; }
        public byte[] HashBytes64 { get => BitConverter.GetBytes(Hash64); }
        public string HashString64 { get => Byte2Hex(HashBytes64); }
        public int Hash32 { get; private set; }
        public byte[] HashBytes32 { get => BitConverter.GetBytes(Hash32); }
        public string HashString32 { get => Byte2Hex(HashBytes32); }
        public long LastChecksumPos { get; private set; }

        private readonly byte[] _inbuf32 = new byte[4];
        private readonly byte[] _inbuf64 = new byte[8];
        private int _bufpos = 0;
        private bool _cancompute;

        public void Flush()
        {
            Hash64 = 0;
            Hash32 = 0;
            _bufpos = 0;
             (_inbuf32[0], _inbuf32[1],
              _inbuf32[2], _inbuf32[3])
             =
             (0, 0,
              0, 0);

             (_inbuf64[0], _inbuf64[1],
              _inbuf64[2], _inbuf64[3],
              _inbuf64[4], _inbuf64[5],
              _inbuf64[6], _inbuf64[7])
             =
             (0, 0,
              0, 0,
              0, 0,
              0, 0);
            _cancompute = false;
        }

        public void InjectHash(long hash) => Hash64 = hash;
        public void InjectHash(int hash) => Hash32 = hash;
        public void InjectPos(long pos) => LastChecksumPos = pos;

        public void ComputeHash64(byte[] input, int length)
        {
            int i = 0;
            while (i < length)
            {
                while (!_cancompute && i < length)
                {
                    _inbuf64[_bufpos] = input[i];
                    _bufpos++;
                    i++;
                    _cancompute = _bufpos > 7;
                }

                if (_cancompute)
                {
                    Hash64 ^= _inbuf64[0] | (long)_inbuf64[1] << 8 | (long)_inbuf64[2] << 16 | (long)_inbuf64[3] << 24 |
                       (long)_inbuf64[4] << 32 | (long)_inbuf64[5] << 40 | (long)_inbuf64[6] << 48 | (long)_inbuf64[7] << 56;
                    _bufpos = 0;
                    _cancompute = false;
                }
            }

            LastChecksumPos += length;
        }

        public void ComputeHash32(byte[] input, int length)
        {
            int i = 0;
            while (i < length)
            {
                while (!_cancompute && i < length)
                {
                    _inbuf32[_bufpos] = input[i];
                    _bufpos++;
                    i++;
                    _cancompute = _bufpos > 3;
                }

                if (_cancompute)
                {
                    Hash32 ^= _inbuf32[0] | _inbuf32[1] << 8 | _inbuf32[2] << 16 | _inbuf32[3] << 24;
                    _bufpos = 0;
                    _cancompute = false;
                }
            }

            LastChecksumPos += length;
        }

        private string Byte2Hex(byte[] bytes)
        {
            char[] c = new char[bytes.Length * 2];
            int b;
            for (int i = 0; i < bytes.Length; i++)
            {
                b = bytes[i] >> 4;
                c[i * 2] = (char)(55 + b + (((b - 10) >> 31) & -7));
                b = bytes[i] & 0xF;
                c[i * 2 + 1] = (char)(55 + b + (((b - 10) >> 31) & -7));
            }
            return new string(c);
        }
    }
}

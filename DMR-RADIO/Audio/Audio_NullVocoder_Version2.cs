using System;

namespace DMR_RADIO.Audio
{
    /// <summary>Tichá záložní implementace; nenahrazuje skutečný AMBE+2 vokodér.</summary>
    public sealed class NullVocoder : IVocoder
    {
        public void Dispose() { }

        public short[] Decode(ReadOnlySpan<byte> ambeFrame9Bytes)
        {
            return new short[160];
        }

        public byte[] Encode(ReadOnlySpan<short> pcm160)
        {
            return new byte[9];
        }
    }
}
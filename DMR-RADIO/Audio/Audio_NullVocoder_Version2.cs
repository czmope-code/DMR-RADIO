using System;

namespace DMR_RADIO.Audio
{
    /// <summary>
    /// A safe fallback vocoder for testing: Decode -> silence, Encode -> zero bytes.
    /// Use only for RX testing and to allow TX "join" behaviour until a real encoder is added.
    /// </summary>
    public sealed class NullVocoder : IVocoder
    {
        public void Dispose() { /* nothing to free */ }

        public short[] Decode(ReadOnlySpan<byte> ambeFrame9Bytes)
        {
            // Return 160 samples silence (20 ms @ 8 kHz)
            return new short[160];
        }

        public byte[] Encode(ReadOnlySpan<short> pcm160)
        {
            // Return a 9-byte "empty" AMBE frame. This produces silent TX bursts on the network.
            return new byte[9];
        }
    }
}
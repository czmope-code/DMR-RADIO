namespace DmrRadio.Audio
{
    public interface IVocoder : IDisposable
    {
        /// <summary>
        /// Decode a single 9-byte AMBE+2 frame into 160 PCM samples (20 ms @ 8 kHz).
        /// </summary>
        short[] Decode(ReadOnlySpan<byte> ambeFrame9Bytes);

        /// <summary>
        /// Encode 160 PCM samples into a 9-byte AMBE+2 frame.
        /// If encoder is not available return a 9-byte "silence" frame or throw.
        /// </summary>
        byte[] Encode(ReadOnlySpan<short> pcm160);
    }
}
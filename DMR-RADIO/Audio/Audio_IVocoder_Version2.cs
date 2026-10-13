namespace DMR_RADIO.Audio
{
    public interface IVocoder : IDisposable
    {
        /// <summary>
        /// Dekóduje jeden 9bajtový (72bitový, kanálově kódovaný) DMR AMBE+2 rámec
        /// na 160 PCM vzorků (20 ms při 8 kHz). Skutečný dekodér uvnitř zpracuje
        /// kanálové kódování a 49bitová hlasová data.
        /// </summary>
        short[] Decode(ReadOnlySpan<byte> ambeFrame9Bytes);

        /// <summary>
        /// Zakóduje 160 PCM vzorků do 9bajtového DMR AMBE+2 rámce.
        /// Implementace musí vrátit i kanálově kódovaných 72 bitů.
        /// </summary>
        byte[] Encode(ReadOnlySpan<short> pcm160);
    }
}
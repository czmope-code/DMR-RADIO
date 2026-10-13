namespace DMR_RADIO.Audio;

/// <summary>
/// Mapuje tři 72bitové AMBE kanálové rámce mezi DMR burstem a vokodérem;
/// prostřední 48bitové pole obsahuje sync nebo embedded signalling.
/// </summary>
public static class DmrVoiceBurstCodec
{
    private const int BurstLength = 33;
    private const int SyncStartBit = 108;
    private const int SyncLengthBits = 48;
    private static readonly byte[] VoiceSync = [0x75, 0x5F, 0xD7, 0xDF, 0x75, 0xF7];
    private static readonly int[] BlockA = [0, 4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 68, 1, 5, 9, 13, 17, 21];
    private static readonly int[] BlockB = [25, 29, 33, 37, 41, 45, 49, 53, 57, 61, 65, 69, 2, 6, 10, 14, 18, 22, 26, 30, 34, 38, 42];
    private static readonly int[] BlockC = [46, 50, 54, 58, 62, 66, 70, 3, 7, 11, 15, 19, 23, 27, 31, 35, 39, 43, 47, 51, 55, 59, 63, 67, 71];

    public static byte[][] ExtractAmbeFrames(ReadOnlySpan<byte> burst)
    {
        if (burst.Length != BurstLength)
            throw new ArgumentException("DMR burst musí mít přesně 33 bajtů.", nameof(burst));

        var frames = new[] { new byte[9], new byte[9], new byte[9] };
        for (var frame = 0; frame < 3; frame++)
        {
            ExtractBlock(burst, frames[frame], BlockA, frame);
            ExtractBlock(burst, frames[frame], BlockB, frame);
            ExtractBlock(burst, frames[frame], BlockC, frame);
        }
        return frames;
    }

    public static byte[] CreateVoiceBurst(IReadOnlyList<byte[]> ambeFrames, bool voiceSync)
    {
        ArgumentNullException.ThrowIfNull(ambeFrames);
        if (ambeFrames.Count != 3 || ambeFrames.Any(frame => frame is null || frame.Length != 9))
            throw new ArgumentException("Hlasový burst vyžaduje tři AMBE rámce po 9 bajtech.", nameof(ambeFrames));

        var burst = new byte[BurstLength];
        for (var frame = 0; frame < 3; frame++)
        {
            WriteBlock(burst, ambeFrames[frame], BlockA, frame);
            WriteBlock(burst, ambeFrames[frame], BlockB, frame);
            WriteBlock(burst, ambeFrames[frame], BlockC, frame);
        }

        if (voiceSync)
        {
            for (var bit = 0; bit < SyncLengthBits; bit++)
                WriteBit(burst, SyncStartBit + bit, ReadBit(VoiceSync, bit));
        }
        return burst;
    }

    private static void ExtractBlock(ReadOnlySpan<byte> burst, Span<byte> frame, int[] positions, int frameIndex)
    {
        foreach (var position in positions)
            WriteBit(frame, position, ReadBit(burst, GetBurstBit(position, frameIndex)));
    }

    private static void WriteBlock(Span<byte> burst, ReadOnlySpan<byte> frame, int[] positions, int frameIndex)
    {
        foreach (var position in positions)
            WriteBit(burst, GetBurstBit(position, frameIndex), ReadBit(frame, position));
    }

    private static int GetBurstBit(int position, int frameIndex) => frameIndex switch
    {
        0 => position,
        1 => position + 72 + (position + 72 >= SyncStartBit ? SyncLengthBits : 0),
        2 => position + 192,
        _ => throw new ArgumentOutOfRangeException(nameof(frameIndex)),
    };

    private static int ReadBit(ReadOnlySpan<byte> data, int bit)
        => (data[bit / 8] >> (7 - bit % 8)) & 1;

    private static void WriteBit(Span<byte> data, int bit, int value)
    {
        if (value != 0)
            data[bit / 8] |= (byte)(1 << (7 - bit % 8));
    }
}

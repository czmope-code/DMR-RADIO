namespace DMR_RADIO.Protocol;

public static class DmrLinkControl
{
    private static readonly byte[] DataSync = [0x0D, 0xFF, 0x57, 0xD7, 0x5D, 0xF5];
    private static readonly byte[] HeaderCrcMask = [0x96, 0x96, 0x96];
    private static readonly byte[] TerminatorCrcMask = [0x99, 0x99, 0x99];

    public static byte[] BuildLinkControlData(uint source, uint destination, bool terminator)
    {
        ValidateId(source, nameof(source));
        ValidateId(destination, nameof(destination));

        var lc = new byte[12];
        lc[0] = 0x00;
        lc[1] = 0x00;
        lc[2] = 0x00;
        WriteId(lc.AsSpan(3, 3), destination);
        WriteId(lc.AsSpan(6, 3), source);

        var parity = EncodeRs129(lc.AsSpan(0, 9));
        var mask = terminator ? TerminatorCrcMask : HeaderCrcMask;
        lc[9] = (byte)(parity[0] ^ mask[0]);
        lc[10] = (byte)(parity[1] ^ mask[1]);
        lc[11] = (byte)(parity[2] ^ mask[2]);
        return lc;
    }

    public static byte[] BuildVoiceLcHeaderBurst(uint source, uint destination, byte colorCode = 1)
        => BuildDataBurst(BuildLinkControlData(source, destination, false), 1, colorCode);

    public static byte[] BuildTerminatorBurst(uint source, uint destination, byte colorCode = 1)
        => BuildDataBurst(BuildLinkControlData(source, destination, true), 2, colorCode);

    private static byte[] BuildDataBurst(byte[] lc, byte dataType, byte colorCode)
    {
        if (colorCode > 15)
            throw new ArgumentOutOfRangeException(nameof(colorCode));

        var coded = EncodeBptc19696(lc);
        var slotCode = EncodeGolay2087((byte)((colorCode << 4) | dataType));
        var burst = new byte[33];

        for (var bit = 0; bit < 98; bit++)
            WriteBit(burst, bit, coded[bit]);
        for (var bit = 0; bit < 10; bit++)
            WriteBit(burst, 98 + bit, ReadBit(slotCode, bit));
        for (var bit = 0; bit < 48; bit++)
            WriteBit(burst, 108 + bit, ReadBit(DataSync, bit));
        for (var bit = 0; bit < 10; bit++)
            WriteBit(burst, 156 + bit, ReadBit(slotCode, 10 + bit));
        for (var bit = 98; bit < coded.Length; bit++)
            WriteBit(burst, bit + 68, coded[bit]);
        return burst;
    }

    private static byte[] EncodeBptc19696(ReadOnlySpan<byte> payload)
    {
        var matrix = new bool[196];
        var payloadBit = 0;
        for (var column = 4; column <= 11; column++)
            matrix[column] = ReadBit(payload, payloadBit++);
        for (var row = 1; row <= 8; row++)
        {
            for (var column = 1; column <= 11; column++)
                matrix[row * 15 + column] = ReadBit(payload, payloadBit++);
        }

        for (var row = 0; row < 9; row++)
        {
            var offset = row * 15 + 1;
            var d = matrix.AsSpan(offset, 15);
            d[11] = Xor(d, 0, 1, 2, 3, 5, 7, 8);
            d[12] = Xor(d, 1, 2, 3, 4, 6, 8, 9);
            d[13] = Xor(d, 2, 3, 4, 5, 7, 9, 10);
            d[14] = Xor(d, 0, 1, 2, 4, 6, 7, 10);
        }

        Span<bool> columnBits = stackalloc bool[13];
        for (var column = 1; column <= 15; column++)
        {
            for (var row = 0; row < 9; row++)
                columnBits[row] = matrix[row * 15 + column];
            columnBits[9] = columnBits[0] ^ columnBits[1] ^ columnBits[3] ^ columnBits[5] ^ columnBits[6];
            columnBits[10] = columnBits[0] ^ columnBits[1] ^ columnBits[2] ^ columnBits[4] ^ columnBits[6] ^ columnBits[7];
            columnBits[11] = columnBits[0] ^ columnBits[1] ^ columnBits[2] ^ columnBits[3] ^ columnBits[5] ^ columnBits[7] ^ columnBits[8];
            columnBits[12] = columnBits[0] ^ columnBits[2] ^ columnBits[4] ^ columnBits[5] ^ columnBits[8];
            for (var row = 9; row < 13; row++)
                matrix[row * 15 + column] = columnBits[row];
        }

        var interleaved = new byte[196];
        for (var bit = 0; bit < 196; bit++)
            interleaved[(bit * 181) % 196] = matrix[bit] ? (byte)1 : (byte)0;
        return interleaved;
    }

    private static byte[] EncodeGolay2087(byte value)
    {
        var word = (uint)value << 11;
        var remainder = word;
        const uint generator = 0xC75;
        for (var bit = 18; bit >= 11; bit--)
        {
            if ((remainder & (1u << bit)) != 0)
                remainder ^= generator << (bit - 11);
        }

        var code19 = word | (remainder & 0x7FF);
        var parity = System.Numerics.BitOperations.PopCount(code19) & 1;
        var code20 = (code19 << 1) | (uint)parity;
        var bytes = new byte[3];
        for (var bit = 0; bit < 20; bit++)
            WriteBit(bytes, bit, (int)((code20 >> (19 - bit)) & 1));
        return bytes;
    }

    private static byte[] EncodeRs129(ReadOnlySpan<byte> data)
    {
        var parity = new byte[3];
        ReadOnlySpan<byte> polynomial = [64, 56, 14];
        foreach (var value in data)
        {
            var feedback = (byte)(value ^ parity[2]);
            parity[2] = (byte)(parity[1] ^ MultiplyGalois(polynomial[2], feedback));
            parity[1] = (byte)(parity[0] ^ MultiplyGalois(polynomial[1], feedback));
            parity[0] = MultiplyGalois(polynomial[0], feedback);
        }
        return [parity[2], parity[1], parity[0]];
    }

    private static byte MultiplyGalois(byte left, byte right)
    {
        var product = 0;
        var a = (int)left;
        var b = right;
        while (b != 0)
        {
            if ((b & 1) != 0)
                product ^= a;
            b >>= 1;
            a <<= 1;
            if ((a & 0x100) != 0)
                a ^= 0x11D;
        }
        return (byte)product;
    }

    private static bool Xor(ReadOnlySpan<bool> data, params int[] indexes)
    {
        var value = false;
        foreach (var index in indexes)
            value ^= data[index];
        return value;
    }

    private static bool ReadBit(ReadOnlySpan<byte> data, int bit)
        => (data[bit / 8] & (1 << (7 - bit % 8))) != 0;

    private static void WriteBit(Span<byte> data, int bit, bool value)
    {
        if (value)
            data[bit / 8] |= (byte)(1 << (7 - bit % 8));
    }

    private static void WriteBit(Span<byte> data, int bit, int value)
        => WriteBit(data, bit, value != 0);

    private static void WriteId(Span<byte> destination, uint id)
    {
        destination[0] = (byte)(id >> 16);
        destination[1] = (byte)(id >> 8);
        destination[2] = (byte)id;
    }

    private static void ValidateId(uint id, string name)
    {
        if (id > 0xFFFFFF)
            throw new ArgumentOutOfRangeException(name, "DMR ID musí mít nejvýše 24 bitů.");
    }
}

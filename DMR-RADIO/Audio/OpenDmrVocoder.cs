using System.IO;
using System.Runtime.InteropServices;

namespace DMR_RADIO.Audio;

public sealed class OpenDmrVocoder : IVocoder
{
    private IntPtr _decoder;
    private IntPtr _encoder;
    private bool _disposed;

    private OpenDmrVocoder()
    {
        _decoder = Native.opendmr_decoder_create();
        if (_decoder == IntPtr.Zero)
            throw new InvalidOperationException("OpenDMR nevytvořil AMBE+2 dekodér.");

        try
        {
            _encoder = Native.opendmr_encoder_create();
            if (_encoder == IntPtr.Zero)
                throw new InvalidOperationException("OpenDMR nevytvořil AMBE+2 kodér.");
        }
        catch
        {
            Native.opendmr_decoder_destroy(_decoder);
            _decoder = IntPtr.Zero;
            throw;
        }
    }

    public static IVocoder CreateOrFallback(out string? warning)
    {
        try
        {
            warning = null;
            return new OpenDmrVocoder();
        }
        catch (DllNotFoundException ex)
        {
            warning = $"OpenDMR není načten: {ex.Message}";
        }
        catch (EntryPointNotFoundException ex)
        {
            warning = $"Knihovna OpenDMR nemá očekávané C API: {ex.Message}";
        }
        catch (BadImageFormatException ex)
        {
            warning = $"Architektura knihovny OpenDMR neodpovídá aplikaci: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            warning = ex.Message;
        }

        return new NullVocoder();
    }

    public short[] Decode(ReadOnlySpan<byte> ambeFrame9Bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ambeFrame9Bytes.Length != 9)
            throw new ArgumentException("AMBE rámec musí mít přesně 9 bajtů.", nameof(ambeFrame9Bytes));

        var input = ambeFrame9Bytes.ToArray();
        var pcm = new short[160];
        if (!Native.opendmr_decode(_decoder, input, pcm, out _))
            throw new InvalidDataException("OpenDMR odmítl AMBE+2 rámec.");
        return pcm;
    }

    public byte[] Encode(ReadOnlySpan<short> pcm160)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pcm160.Length != 160)
            throw new ArgumentException("PCM rámec musí mít přesně 160 vzorků.", nameof(pcm160));

        var output = new byte[9];
        if (!Native.opendmr_encode(_encoder, pcm160.ToArray(), output))
            throw new InvalidDataException("OpenDMR odmítl PCM rámec.");
        return output;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Native.opendmr_decoder_destroy(_decoder);
        Native.opendmr_encoder_destroy(_encoder);
        _decoder = IntPtr.Zero;
        _encoder = IntPtr.Zero;
    }

    private static class Native
    {
        private const string LibraryName = "libopendmr";

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr opendmr_decoder_create();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void opendmr_decoder_destroy(IntPtr decoder);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool opendmr_decode(
            IntPtr decoder,
            [In] byte[] ambe,
            [Out] short[] pcm,
            out int errors);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr opendmr_encoder_create();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void opendmr_encoder_destroy(IntPtr encoder);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool opendmr_encode(
            IntPtr encoder,
            [In] short[] pcm,
            [Out] byte[] ambe);
    }
}

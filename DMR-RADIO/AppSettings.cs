using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DMR_RADIO.storage;

/// <summary>Uložené nastavení aplikace (%AppData%\DmrRadio2\settings.json).</summary>
public sealed class AppSettings
{
    public string DmrId { get; set; } = "";
    public string Server { get; set; } = "";
    public string Port { get; set; } = "62031";
    public string TalkGroup { get; set; } = "230";
    public double Volume { get; set; } = 70;
    public int AudioOutputDeviceNumber { get; set; } = -1;

    /// <summary>Heslo v paměti (do souboru se nikdy nezapisuje čitelně).</summary>
    [JsonIgnore]
    public string Password { get; set; } = "";

    /// <summary>Heslo zašifrované přes Windows DPAPI (účet aktuálního uživatele), base64.</summary>
    public string? PasswordProtected { get; set; }
}

public static class SettingsStore
{
    private static readonly string Directory_ = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DmrRadio2");

    private static readonly string FilePath = Path.Combine(Directory_, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Načte nastavení. Při chybějícím nebo poškozeném souboru vrátí výchozí hodnoty.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();

            if (!string.IsNullOrEmpty(settings.PasswordProtected))
            {
                try
                {
                    var plain = Dpapi.Unprotect(Convert.FromBase64String(settings.PasswordProtected));
                    settings.Password = Encoding.UTF8.GetString(plain);
                }
                catch (Exception)
                {
                    // jiný uživatel / jiný počítač / poškozená data – heslo se prostě zadá znovu
                    settings.Password = "";
                }
            }

            return settings;
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    /// <summary>Uloží nastavení. Vrací false, když se zápis nepovedl (aplikace kvůli tomu nemá spadnout).</summary>
    public static bool Save(AppSettings settings)
    {
        try
        {
            settings.PasswordProtected = string.IsNullOrEmpty(settings.Password)
                ? null
                : Convert.ToBase64String(Dpapi.Protect(Encoding.UTF8.GetBytes(settings.Password)));

            Directory.CreateDirectory(Directory_);

            // Zápis přes dočasný soubor, ať při pádu nezůstane poloprázdný settings.json.
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Šifrování přes Windows DPAPI (crypt32.dll), bez dalších NuGet balíčků.</summary>
internal static class Dpapi
{
    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn, string? szDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static byte[] Protect(byte[] data) => Transform(data, protect: true);

    public static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = new DataBlob { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
        try
        {
            Marshal.Copy(data, 0, input.pbData, data.Length);

            var ok = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output);

            if (!ok)
                throw new CryptographicExceptionLike(Marshal.GetLastWin32Error());

            try
            {
                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally
            {
                LocalFree(output.pbData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.pbData);
        }
    }

    private sealed class CryptographicExceptionLike(int code)
        : Exception($"DPAPI selhalo (kód {code}).");
}

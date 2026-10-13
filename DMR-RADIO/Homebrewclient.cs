using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace DMR_RADIO.Protocol;

/// <summary>Typ DMR rámce z bitů 5–4 příznakového bajtu paketu DMRD.</summary>
public enum DmrFrameType : byte
{
    Voice = 0,
    VoiceSync = 1,
    DataSync = 2,
    Reserved = 3,
}

/// <summary>Jeden paket DMRD (55 B) přijatý ze serveru.</summary>
public readonly record struct DmrdPacket(
    byte Sequence,
    uint Source,
    uint Destination,
    uint RepeaterId,
    bool Slot2,
    bool IsPrivate,
    DmrFrameType FrameType,
    byte DataType,
    uint StreamId,
    byte[] Burst,
    byte Ber,
    byte Rssi)
{
    public const int MinLength = 53;

    public bool IsTerminator => FrameType == DmrFrameType.DataSync && DataType == 2;

    public static bool TryParse(ReadOnlySpan<byte> d, out DmrdPacket packet)
    {
        packet = default;
        if (d.Length < MinLength)
            return false;

        var flags = d[15];
        packet = new DmrdPacket(
            Sequence: d[4],
            Source: (uint)(d[5] << 16 | d[6] << 8 | d[7]),
            Destination: (uint)(d[8] << 16 | d[9] << 8 | d[10]),
            RepeaterId: BinaryPrimitives.ReadUInt32BigEndian(d.Slice(11, 4)),
            Slot2: (flags & 0x80) != 0,
            IsPrivate: (flags & 0x40) != 0,
            FrameType: (DmrFrameType)((flags >> 4) & 0x03),
            DataType: (byte)(flags & 0x0F),
            StreamId: BinaryPrimitives.ReadUInt32BigEndian(d.Slice(16, 4)),
            Burst: d.Slice(20, 33).ToArray(),
            Ber: d.Length > 53 ? d[53] : (byte)0,
            Rssi: d.Length > 54 ? d[54] : (byte)0);
        return true;
    }
}

/// <summary>Nastavení spojení. Údaje v RPTC slouží jen k registraci na serveru.</summary>
public sealed record HomebrewOptions(uint RadioId, string Host, int Port, string Password)
{
    public string Callsign { get; init; } = "";
    public uint RxFrequencyHz { get; init; } = 439000000;
    public uint TxFrequencyHz { get; init; } = 439000000;
    public int TxPower { get; init; } = 1;
    public int ColorCode { get; init; } = 1;
    public double Latitude { get; init; } = 0;
    public double Longitude { get; init; } = 0;
    public int HeightMeters { get; init; } = 0;
    public string Location { get; init; } = "DmrRadio2";
    public string Description { get; init; } = "WPF DMR client";
    public string Url { get; init; } = "";
    public string SoftwareId { get; init; } = "DmrRadio2";
    public string PackageId { get; init; } = "DmrRadio2-0.1";
}

/// <summary>
/// Klient Homebrew (MMDVM) protokolu přes UDP s detailním ladicím výpisem do konzole.
/// </summary>
public sealed class HomebrewClient : IAsyncDisposable
{
    private enum Stage { Idle, Login, Auth, Config, Connected, Closed }

    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ServerTimeout = TimeSpan.FromSeconds(30);
    private const int HandshakeAttempts = 5;
    private static readonly TimeSpan HandshakeStep = TimeSpan.FromSeconds(2);

    private readonly HomebrewOptions _opt;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource _loginDone = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private UdpClient? _udp;
    private volatile Stage _stage = Stage.Idle;
    private byte[]? _salt;
    private long _lastRxTick;
    private int _closed;
    private Task? _receiveTask;
    private Task? _pingTask;

    public HomebrewClient(HomebrewOptions options) => _opt = options;

    /// <summary>Textové hlášení pro log a konzoli.</summary>
    public event Action<string>? Log;

    /// <summary>Přijatý paket DMRD (jen po úspěšném přihlášení).</summary>
    public event Action<DmrdPacket>? PacketReceived;

    /// <summary>Spojení se přerušilo po úspěšném přihlášení.</summary>
    public event Action<string>? ConnectionLost;

    private void WriteLog(string message)
    {
        var formatted = $"[DMR {DateTime.Now:HH:mm:ss.fff}] [{_stage}] {message}";
        Console.WriteLine(formatted);
        Log?.Invoke(formatted);
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_stage != Stage.Idle)
            throw new InvalidOperationException("Klient už byl použit.");

        WriteLog($"Zahajuji připojování k serveru {_opt.Host}:{_opt.Port} pro DMR ID: {_opt.RadioId}...");

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(_opt.Host, ct).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            WriteLog($"Chyba DNS: Server „{_opt.Host}“ se nepodařilo najít.");
            throw new InvalidOperationException($"Server „{_opt.Host}“ se nepodařilo najít.");
        }

        var ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                 ?? throw new InvalidOperationException($"Server „{_opt.Host}“ nemá IPv4 adresu.");

        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Connect(ip, _opt.Port);
        _lastRxTick = Environment.TickCount64;
        _stage = Stage.Login;

        WriteLog($"UDP socket úspěšně navázán na IPv4: {ip}:{_opt.Port}. Spouštím smyčku příjmu dat.");
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            for (var attempt = 0; attempt < HandshakeAttempts && !_loginDone.Task.IsCompleted; attempt++)
            {
                WriteLog($"Pokus o handshaking č. {attempt + 1}/{HandshakeAttempts}...");
                await SendStageMessageAsync(linked.Token).ConfigureAwait(false);
                await Task.WhenAny(_loginDone.Task, Task.Delay(HandshakeStep, linked.Token)).ConfigureAwait(false);
            }

            if (!_loginDone.Task.IsCompleted)
            {
                WriteLog("Handshake selhal: Server neodpověděl v časovém limitu.");
                throw new TimeoutException("Server neodpovídá. Zkontroluj adresu a port.");
            }

            await _loginDone.Task.ConfigureAwait(false);
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _pingTask = Task.Run(() => PingLoopAsync(_cts.Token));
        WriteLog("Autentizace a handshake kompletní. Klient je plně připojen.");
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var udp = _udp;
                if (udp is null)
                    return;

                var result = await udp.ReceiveAsync(ct).ConfigureAwait(false);
                await HandlePacketAsync(result.Buffer, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException ex)
        {
            Fail($"Chyba sítě (SocketException): {ex.Message}");
        }
        catch (Exception ex)
        {
            Fail($"Neočekávaná chyba při příjmu: {ex.Message}");
        }
    }

    private Task HandlePacketAsync(byte[] data, CancellationToken ct)
    {
        Volatile.Write(ref _lastRxTick, Environment.TickCount64);
        ReadOnlySpan<byte> d = data;

        if (d.StartsWith("DMRD"u8))
        {
            if (_stage == Stage.Connected && DmrdPacket.TryParse(d, out var packet))
            {
                WriteLog($"[DMRD] Přijat hlasový/datový rámec | Seq: {packet.Sequence} | Src: {packet.Source} | Dst: {packet.Destination} | Typ: {packet.FrameType} | Slot2: {packet.Slot2}");
                PacketReceived?.Invoke(packet);
            }
            return Task.CompletedTask;
        }

        if (d.StartsWith("RPTACK"u8))
        {
            WriteLog($"[RPTACK] Přijato potvrzení od serveru (délka: {data.Length} B).");
            return OnAckAsync(data, ct);
        }

        if (d.StartsWith("MSTPONG"u8))
        {
            WriteLog("[MSTPONG] Přijata odpověď na ping (Keep-Alive v pořádku).");
            return Task.CompletedTask;
        }

        if (d.StartsWith("MSTNAK"u8))
        {
            var reason = _stage switch
            {
                Stage.Connected => "Server odpojil klienta (MSTNAK).",
                Stage.Login => "Server odmítl DMR ID hned při přihlášení (MSTNAK po RPTL). ID není povolené, nebo je už připojené jinde.",
                Stage.Auth => "Server odmítl heslo (MSTNAK po RPTK). Zkontroluj heslo v nastavení.",
                Stage.Config => "Server odmítl konfiguraci (MSTNAK po RPTC).",
                _ => "Server odmítl spojení (MSTNAK).",
            };
            WriteLog($"[MSTNAK ZACHYCEN] {reason} (Fáze: {_stage})");
            Fail(reason);
            return Task.CompletedTask;
        }

        if (d.StartsWith("MSTCL"u8))
        {
            WriteLog("[MSTCL] Server vyžádal ukončení spojení.");
            Fail("Server ukončil spojení (MSTCL).");
        }

        return Task.CompletedTask;
    }

    private async Task OnAckAsync(byte[] data, CancellationToken ct)
    {
        switch (_stage)
        {
            case Stage.Login:
                if (data.Length < 10)
                {
                    WriteLog("Chyba: RPTACK ve fázi Login je příliš krátký (chybí salt).");
                    Fail("Neplatná odpověď serveru (chybí salt).");
                    return;
                }

                _salt = data.AsSpan(6, 4).ToArray();
                _stage = Stage.Auth;
                WriteLog($"Získán kryptografický Salt (4 bajty). Přecházím do fáze autentizace (Auth).");
                await SendStageMessageAsync(ct).ConfigureAwait(false);
                break;

            case Stage.Auth:
                _stage = Stage.Config;
                WriteLog("Heslo bylo serverem ověřeno a přijato. Přecházím k odeslání konfigurace (Config).");
                await SendStageMessageAsync(ct).ConfigureAwait(false);
                break;

            case Stage.Config:
                _stage = Stage.Connected;
                WriteLog("Konfigurace schválena serverem. Navázáno stabilní spojení!");
                _loginDone.TrySetResult();
                break;
        }
    }

    private async Task PingLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(PingInterval);
            var ping = BuildSimple("RPTPING");

            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var silence = Environment.TickCount64 - Volatile.Read(ref _lastRxTick);
                if (silence > ServerTimeout.TotalMilliseconds)
                {
                    WriteLog($"Timeout: Server neodpověděl po dobu {silence} ms.");
                    Fail("Server neodpovídá (timeout).");
                    return;
                }

                WriteLog("Odesílám RPTPING (Keep-Alive)...");
                await SendAsync(ping, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException ex)
        {
            Fail($"Chyba sítě v ping smyčce: {ex.Message}");
        }
    }

    private Task SendStageMessageAsync(CancellationToken ct)
    {
        switch (_stage)
        {
            case Stage.Login:
                WriteLog("Odesílám registrační paket RPTL (Login)...");
                return SendAsync(BuildSimple("RPTL"), ct);

            case Stage.Auth when _salt is not null:
                WriteLog("Odesílám ověřovací paket RPTK s hašem hesla (Auth)...");
                return SendAsync(BuildAuth(_salt), ct);

            case Stage.Config:
                WriteLog("Odesílám konfigurační paket RPTC (Config)...");
                return SendAsync(BuildConfig(), ct);

            default:
                return Task.CompletedTask;
        }
    }

    private async Task SendAsync(byte[] data, CancellationToken ct)
    {
        var udp = _udp;
        if (udp is null)
            return;

        await udp.SendAsync(data, ct).ConfigureAwait(false);
    }

    private byte[] BuildSimple(string tag)
    {
        var b = new byte[tag.Length + 4];
        Encoding.ASCII.GetBytes(tag.AsSpan(), b.AsSpan());
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(tag.Length), _opt.RadioId);
        return b;
    }

    private byte[] BuildAuth(byte[] salt)
    {
        var password = Encoding.UTF8.GetBytes(_opt.Password);
        var input = new byte[salt.Length + password.Length];
        salt.CopyTo(input, 0);
        password.CopyTo(input, salt.Length);
        var hash = SHA256.HashData(input);

        var b = new byte[4 + 4 + hash.Length];
        Encoding.ASCII.GetBytes("RPTK".AsSpan(), b.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4, 4), _opt.RadioId);
        hash.CopyTo(b, 8);
        return b;
    }

    private byte[] BuildConfig()
    {
        var inv = CultureInfo.InvariantCulture;
        var b = new byte[302];
        Array.Fill(b, (byte)' ');

        Put(b, 0, "RPTC", 4);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4, 4), _opt.RadioId);
        Put(b, 8, string.IsNullOrWhiteSpace(_opt.Callsign) ? _opt.RadioId.ToString(inv) : _opt.Callsign, 8);
        Put(b, 16, _opt.RxFrequencyHz.ToString("D9", inv), 9);
        Put(b, 25, _opt.TxFrequencyHz.ToString("D9", inv), 9);
        Put(b, 34, _opt.TxPower.ToString("D2", inv), 2);
        Put(b, 36, _opt.ColorCode.ToString("D2", inv), 2);
        Put(b, 38, _opt.Latitude.ToString("000.0000", inv), 8);
        Put(b, 46, _opt.Longitude.ToString("0000.0000", inv), 9);
      //  Put(b, 55, _opt.HeightMeters.HeightMetersString(), 3); // Zjednodušeno na standardní ToString
        Put(b, 55, _opt.HeightMeters.ToString("D3", inv), 3);
        Put(b, 58, _opt.Location, 20);
        Put(b, 78, _opt.Description, 19);
        b[97] = (byte)'4';
        Put(b, 98, _opt.Url, 124);
        Put(b, 222, _opt.SoftwareId, 40);
        Put(b, 262, _opt.PackageId, 40);
        return b;
    }

    private static void Put(byte[] buffer, int offset, string text, int length)
    {
        var n = Math.Min(text.Length, length);
        for (var i = 0; i < n; i++)
            buffer[offset + i] = text[i] < 128 ? (byte)text[i] : (byte)'?';
    }

    private void Fail(string reason)
    {
        var wasConnected = _stage == Stage.Connected;
        if (Interlocked.Exchange(ref _closed, 1) == 1)
            return;

        _stage = Stage.Closed;
        WriteLog($"Chyba/Ukončení spojení: {reason}");
        _loginDone.TrySetException(FormattableStringFactory.Create(reason) is object ? new InvalidOperationException(reason) : new InvalidOperationException(reason));
        ReleaseResources();

        if (wasConnected)
            ConnectionLost?.Invoke(reason);
    }

    private void ReleaseResources()
    {
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        try { _udp?.Dispose(); } catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            var wasConnected = _stage == Stage.Connected;
            _stage = Stage.Closed;
            WriteLog("Ukončuji činnost klienta a odesílám RPTCL na server...");
            _loginDone.TrySetCanceled();

            if (wasConnected && _udp is { } udp)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                    await udp.SendAsync(BuildSimple("RPTCL"), timeout.Token).ConfigureAwait(false);
                }
                catch (Exception) { }
            }

            ReleaseResources();
        }

        var tasks = new[] { _receiveTask, _pingTask }.OfType<Task>().ToArray();
        try
        {
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (Exception) { }

        _cts.Dispose();
        WriteLog("Klient byl úspěšně uzavřen a zdroje uvolněny.");
    }
}
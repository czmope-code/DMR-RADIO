using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DMR_RADIO.Audio;
using DMR_RADIO.Mvvm;
using DMR_RADIO.Protocol;
using DMR_RADIO.storage;

namespace DMR_RADIO.ViewModels;

public enum LinkState { Disconnected, Connecting, Connected }

public sealed record TgInfo(string Number, string Name)
{
    public string Label => $"TG {Number}";
}

/// <summary>Jeden segment VU metru.</summary>
public sealed class MeterSegment : ObservableObject
{
    private bool _isOn;

    public MeterSegment(Brush onBrush) => OnBrush = onBrush;

    public Brush OnBrush { get; }

    public bool IsOn
    {
        get => _isOn;
        set => Set(ref _isOn, value);
    }
}

public sealed class MainViewModel : ObservableObject
{
    private const int SegmentCount = 24;

    /// <summary>Po tolika ms bez paketu se hovor považuje za ukončený (kdyby chyběl terminátor).</summary>
    private const long CallTimeoutMs = 800;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _callTimer;
    private readonly Stopwatch _callWatch = new();

    private HomebrewClient? _client;

    // stav hovoru
    private bool _callActive;
    private uint _activeStream;
    private uint? _lastEndedStream;
    private long _lastPacketTick;

    private LinkState _state = LinkState.Disconnected;
    private string _dmrId = "";
    private string _server = "";
    private string _port = "62031";
    private string _errorText = "";
    private double _volume = 70;
    private double _level;
    private bool _isReceiving;
    private bool _isTransmitting;
    private string _callerCallsign = "";
    private string _callDetail = "";
    private int _slot = 1;
    private int _selectedSlot = 1;
    private string _elapsedText = "00:00";
    private TgInfo _selectedTg;
    private AudioPlayer? _audioPlayer;
    private JitterBuffer? _jitterBuffer;
    private CancellationTokenSource? _txCts;
    private Task? _txTask;

    public MainViewModel()
    {
        Favorites = new ObservableCollection<TgInfo>
        {
            new("91", "Celý svět"),
            new("92", "Evropa"),
            new("230", "Česká republika"),
            new("231", "Slovensko"),
            new("27", "DMR"),
        };
        _selectedTg = Favorites[2];

        // Uložené nastavení z minula (hodnoty se zapisují přímo do polí, aby se nespouštělo ukládání ani logování).
        var saved = SettingsStore.Load();
        _dmrId = saved.DmrId;
        _server = saved.Server;
        _port = string.IsNullOrWhiteSpace(saved.Port) ? "62031" : saved.Port;
        _volume = Math.Clamp(saved.Volume, 0, 100);
        _selectedTg = Favorites.FirstOrDefault(t => t.Number == saved.TalkGroup) ?? _selectedTg;
        Password = saved.Password;

        MeterSegments = new ObservableCollection<MeterSegment>();
        for (var i = 0; i < SegmentCount; i++)
        {
            var color = i < 16 ? Color.FromRgb(0x3D, 0xDC, 0x84)
                      : i < 21 ? Color.FromRgb(0xFF, 0xB5, 0x47)
                      : Color.FromRgb(0xFF, 0x4D, 0x4D);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            MeterSegments.Add(new MeterSegment(brush));
        }

        _callTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _callTimer.Tick += (_, _) => OnCallTimerTick();

        ConnectCommand = new AsyncRelayCommand(_ => ToggleConnectionAsync());
        DemoRxCommand = new AsyncRelayCommand(_ => DemoRxAsync());

        // Při zavření aplikace se slušně odhlásit od serveru (RPTCL).
        if (Application.Current is { } app)
            app.Exit += (_, _) => Shutdown();

        AddLog("Aplikace spuštěna.");
    }

    // ---- Kolekce ---------------------------------------------------------

    public ObservableCollection<TgInfo> Favorites { get; }
    public ObservableCollection<MeterSegment> MeterSegments { get; }
    public ObservableCollection<string> LogLines { get; } = new();

    // ---- Příkazy ---------------------------------------------------------

    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DemoRxCommand { get; }

    // ---- Nastavení připojení --------------------------------------------

    public string DmrId
    {
        get => _dmrId;
        set => Set(ref _dmrId, value);
    }

    public string Server
    {
        get => _server;
        set => Set(ref _server, value);
    }

    public string Port
    {
        get => _port;
        set => Set(ref _port, value);
    }

    /// <summary>Heslo hotspotu (BrandMeister SelfCare). Nastavuje se z PasswordBoxu.</summary>
    public string Password { get; set; } = "";

    public string ErrorText
    {
        get => _errorText;
        private set => Set(ref _errorText, value);
    }

    // ---- Stav spojení ----------------------------------------------------

    public LinkState State
    {
        get => _state;
        private set
        {
            if (!Set(ref _state, value))
                return;

            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(CanTransmit));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ConnectButtonText));
            RefreshDisplay();
        }
    }

    public bool IsConnected => State == LinkState.Connected;
    public bool IsEditable => State == LinkState.Disconnected;

    public string StatusText => State switch
    {
        LinkState.Connected => "PŘIPOJENO",
        LinkState.Connecting => "PŘIPOJUJI…",
        _ => "ODPOJENO",
    };

    public string ConnectButtonText => State switch
    {
        LinkState.Connected => "ODPOJIT",
        LinkState.Connecting => "PŘIPOJUJI…",
        _ => "PŘIPOJIT",
    };

    // ---- Rádio -----------------------------------------------------------

    public TgInfo SelectedTg
    {
        get => _selectedTg;
        set
        {
            if (value is null || !Set(ref _selectedTg, value))
                return;

            StopTransmit();
            EndCall();
            AddLog($"Vybrána {value.Label} ({value.Name}).");
            SaveSettings();
        }
    }

    public int SelectedSlotIndex
    {
        get => _selectedSlot - 1;
        set
        {
            var slot = value + 1;
            if (slot is < 1 or > 2 || _selectedSlot == slot)
                return;
            _selectedSlot = slot;
            OnPropertyChanged();
            StopTransmit();
            EndCall();
            AddLog($"Vybrán timeslot {_selectedSlot}.");
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            if (Set(ref _volume, Math.Clamp(value, 0, 100)))
            {
                if (_audioPlayer is not null)
                    _audioPlayer.Volume = _volume;
                OnPropertyChanged(nameof(VolumeText));
            }
        }
    }

    public string VolumeText => $"{Volume:0} %";

    public double Level
    {
        get => _level;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (Set(ref _level, value))
                UpdateMeter();
        }
    }

    public bool IsReceiving
    {
        get => _isReceiving;
        private set
        {
            if (Set(ref _isReceiving, value))
            {
                RefreshDisplay();
                OnPropertyChanged(nameof(CanTransmit));
            }
        }
    }

    public bool IsTransmitting
    {
        get => _isTransmitting;
        private set
        {
            if (Set(ref _isTransmitting, value))
                OnPropertyChanged(nameof(CanTransmit));
        }
    }

    public bool CanTransmit => IsConnected && (!IsReceiving || IsTransmitting);

    public string ElapsedText
    {
        get => _elapsedText;
        private set => Set(ref _elapsedText, value);
    }

    public string CallsignText => IsReceiving ? _callerCallsign : "-----";

    public string DetailText => IsReceiving
        ? _callDetail
        : IsConnected ? "Čekám na hovor…" : "Nepřipojeno";

    public string SlotText => IsReceiving ? $"TS{_slot}" : "TS –";

    // ---- Připojení -------------------------------------------------------

    private bool TryValidate(out string error)
    {
        var id = DmrId.Trim();
        if (id.Length is < 7 or > 9 || !id.All(char.IsAsciiDigit))
        {
            error = "DMR ID musí mít 7 až 9 číslic.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Server))
        {
            error = "Zadej adresu serveru.";
            return false;
        }

        if (!int.TryParse(Port, out var port) || port is < 1 or > 65535)
        {
            error = "Port musí být číslo 1–65535.";
            return false;
        }

        if (string.IsNullOrEmpty(Password))
        {
            error = "Zadej heslo hotspotu.";
            return false;
        }

        error = "";
        return true;
    }

    private async Task ToggleConnectionAsync()
    {
        if (State == LinkState.Connected)
        {
            await DisconnectAsync();
            return;
        }

        if (!TryValidate(out var error))
        {
            ErrorText = error;
            AddLog(error);
            return;
        }

        ErrorText = "";
        State = LinkState.Connecting;
        SaveSettings(); // údaje se uloží hned, i kdyby se připojení nepovedlo

        var host = Server.Trim();
        var port = int.Parse(Port.Trim());
        var options = new HomebrewOptions(uint.Parse(DmrId.Trim()), host, port, Password);

        var client = new HomebrewClient(options);
        client.Log += message => Post(() => AddLog(message));
        client.PacketReceived += packet => Post(() => OnPacket(packet));
        client.ConnectionLost += reason => Post(() => OnConnectionLost(client, reason));

        AddLog($"Připojuji k {host}:{port} jako {options.RadioId}…");

        try
        {
            await client.ConnectAsync();
        }
        catch (Exception ex)
        {
            await client.DisposeAsync();
            State = LinkState.Disconnected;
            ErrorText = ex.Message;
            AddLog($"Připojení selhalo: {ex.Message}");
            return;
        }

        _client = client;
        State = LinkState.Connected;
        AddLog("Připojeno. Čekám na hovory.");
    }

    private async Task DisconnectAsync()
    {
        await StopTransmitAsync();
        var client = _client;
        _client = null;

        EndCall();
        State = LinkState.Disconnected;

        if (client is not null)
            await client.DisposeAsync();

        AddLog("Odpojeno.");
    }

    private void OnConnectionLost(HomebrewClient client, string reason)
    {
        if (!ReferenceEquals(_client, client))
            return;

        _client = null;
        StopTransmit();
        EndCall();
        State = LinkState.Disconnected;
        ErrorText = reason;
        AddLog($"Spojení ztraceno: {reason}");

        _ = client.DisposeAsync().AsTask();
    }

    /// <summary>Volá se při ukončení aplikace.</summary>
    private void Shutdown()
    {
        SaveSettings();
        StopTransmit();

        var client = _client;
        _client = null;
        if (client is null)
            return;

        try
        {
            Task.Run(() => client.DisposeAsync().AsTask()).Wait(1500);
        }
        catch (Exception) { }
    }

    // ---- Příchozí hovory -------------------------------------------------

    private void OnPacket(DmrdPacket p)
    {
        if (p.Source.ToString() == DmrId || p.IsPrivate || p.Destination.ToString() != SelectedTg.Number ||
            (p.Slot2 ? 2 : 1) != _selectedSlot)
            return;

        // Zbytek hovoru, který už jsme ukončili terminátorem.
        if (p.StreamId == _lastEndedStream)
            return;

        if (!_callActive && p.FrameType is DmrFrameType.Voice or DmrFrameType.VoiceSync)
            BeginCall(p);
        else if (p.StreamId != _activeStream)
            return;

        if (!_callActive || p.StreamId != _activeStream)
            return;

        _lastPacketTick = Environment.TickCount64;

        if (p.FrameType is DmrFrameType.Voice or DmrFrameType.VoiceSync)
        {
            foreach (var frame in DmrVoiceBurstCodec.ExtractAmbeFrames(p.Burst))
                _jitterBuffer?.AddFrame(frame);
        }

        if (p.IsTerminator)
            EndCall();
    }

    private void BeginCall(DmrdPacket p)
    {
        _callActive = true;
        _activeStream = p.StreamId;
        _slot = p.Slot2 ? 2 : 1;
        _callerCallsign = p.Source.ToString();
        _callDetail = p.IsPrivate
            ? $"Privátní hovor → {p.Destination}"
            : $"TG {p.Destination}";
        _lastPacketTick = Environment.TickCount64;

        _callWatch.Restart();
        ElapsedText = "00:00";
        IsReceiving = true;
        _callTimer.Start();

        AddLog($"RX: {p.Source} → {(p.IsPrivate ? "privátně " : "TG ")}{p.Destination} (TS{_slot})");
        try
        {
            _audioPlayer = new AudioPlayer { Volume = Volume };
            _jitterBuffer = new JitterBuffer(new NullVocoder(), _audioPlayer)
            {
                TargetLatencyMs = 60
            };
            _jitterBuffer.LevelChanged += level => Post(() =>
            {
                if (_callActive)
                    Level = level;
            });
            _jitterBuffer.Start();
        }
        catch (Exception ex)
        {
            _jitterBuffer?.Dispose();
            _jitterBuffer = null;
            _audioPlayer?.Dispose();
            _audioPlayer = null;
            AddLog($"Audio výstup není dostupný: {ex.Message}");
        }
    }

    private void EndCall()
    {
        if (!_callActive)
        {
            StopAudio();
            return;
        }

        _callActive = false;
        _callTimer.Stop();
        _lastEndedStream = _activeStream;

        var seconds = _callWatch.Elapsed.TotalSeconds;
        _callWatch.Stop();

        Level = 0;
        ElapsedText = "00:00";
        IsReceiving = false;
        StopAudio();
        AddLog($"Konec hovoru ({seconds:0.0} s).");
    }

    private void StopAudio()
    {
        var jitterBuffer = _jitterBuffer;
        _jitterBuffer = null;
        _audioPlayer = null;
        if (jitterBuffer is not null)
            jitterBuffer.Dispose();
    }

    public void StartTransmit()
    {
        var client = _client;
        if (!CanTransmit || client is null || !uint.TryParse(SelectedTg.Number, out var destination))
            return;

        var cancellation = new CancellationTokenSource();
        var manager = new TxManager(
            client,
            new NullVocoder(),
            uint.Parse(DmrId),
            destination,
            uint.Parse(DmrId),
            _selectedSlot == 2,
            (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue),
            message => Post(() => AddLog(message)));

        _txCts = cancellation;
        IsTransmitting = true;
        AddLog($"TX: otevírám TG {destination} (TS{_selectedSlot}), bez audia.");
        _txTask = RunTransmitAsync(manager, cancellation);
    }

    public void StopTransmit()
    {
        _txCts?.Cancel();
    }

    private async Task StopTransmitAsync()
    {
        StopTransmit();
        if (_txTask is not null)
        {
            try { await _txTask.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception) { }
        }
    }

    private async Task RunTransmitAsync(TxManager manager, CancellationTokenSource cancellation)
    {
        using (manager)
        {
            try
            {
                await manager.RunAsync(cancellation.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AddLog($"TX se nezdařilo: {ex.Message}");
            }
        }

        cancellation.Dispose();
        Post(() =>
        {
            if (ReferenceEquals(_txCts, cancellation))
            {
                _txCts = null;
                _txTask = null;
                IsTransmitting = false;
                AddLog("TX ukončeno.");
            }
        });
    }

    private void OnCallTimerTick()
    {
        if (!_callActive)
            return;

        ElapsedText = _callWatch.Elapsed.ToString(@"mm\:ss");

        if (Environment.TickCount64 - _lastPacketTick > CallTimeoutMs)
            EndCall();
    }

    // ---- Demo ------------------------------------------------------------

    /// <summary>Jen pro ukázku vzhledu: nasimuluje příchozí hovor.</summary>
    private async Task DemoRxAsync()
    {
        if (IsReceiving)
            return;

        _callerCallsign = "OK1ABC";
        _callDetail = "Jan, Praha   ·   ID 2301234   ·   TG 230";
        _slot = 2;
        IsReceiving = true;
        AddLog("DEMO: příchozí hovor OK1ABC.");

        var random = new Random();
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(6))
        {
            Level = 0.2 + random.NextDouble() * 0.7;
            ElapsedText = sw.Elapsed.ToString(@"mm\:ss");
            await Task.Delay(60);
        }

        Level = 0;
        ElapsedText = "00:00";
        IsReceiving = false;
        AddLog("DEMO: konec hovoru.");
    }

    // ---- Pomocné ---------------------------------------------------------

    private void SaveSettings()
    {
        var ok = SettingsStore.Save(new AppSettings
        {
            DmrId = DmrId.Trim(),
            Server = Server.Trim(),
            Port = Port.Trim(),
            Password = Password,
            TalkGroup = SelectedTg.Number,
            Volume = Volume,
        });

        if (!ok)
            AddLog("Nastavení se nepodařilo uložit.");
    }

    private void Post(Action action) => _dispatcher.InvokeAsync(action);

    private void UpdateMeter()
    {
        var on = (int)Math.Round(_level * SegmentCount);
        for (var i = 0; i < MeterSegments.Count; i++)
            MeterSegments[i].IsOn = i < on;
    }

    private void RefreshDisplay()
    {
        OnPropertyChanged(nameof(CallsignText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(SlotText));
    }

    private void AddLog(string message)
    {
        LogLines.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (LogLines.Count > 200)
            LogLines.RemoveAt(LogLines.Count - 1);
    }
}

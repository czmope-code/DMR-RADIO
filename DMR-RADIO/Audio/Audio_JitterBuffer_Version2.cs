using System;
using System.Collections.Concurrent;

namespace DMR_RADIO.Audio
{
    /// <summary>
    /// Very small jitter buffer: accept incoming raw 9-byte AMBE frames and decode+play via IVocoder+AudioPlayer.
    /// Consumer runs on a background Task. Adjustable target latency in ms (approx).
    /// </summary>
    public class JitterBuffer : IDisposable
    {
        private readonly BlockingCollection<byte[]> _queue = new(new ConcurrentQueue<byte[]>(), 30);
        private readonly IVocoder _vocoder;
        private readonly AudioPlayer _player;
        private readonly CancellationTokenSource _cts = new();
        private Task? _consumer;
        private bool _started;
        public int TargetLatencyMs { get; set; } = 60;
        public event Action<double>? LevelChanged;

        public JitterBuffer(IVocoder vocoder, AudioPlayer player)
        {
            _vocoder = vocoder ?? throw new ArgumentNullException(nameof(vocoder));
            _player = player ?? throw new ArgumentNullException(nameof(player));
        }

        public void Start()
        {
            if (_started)
                return;
            _started = true;
            _player.Start();
            _consumer = Task.Run(ConsumeLoop);
        }

        public void Stop()
        {
            if (!_started)
                return;
            _cts.Cancel();
            try { _consumer?.Wait(500); } catch { }
            _player.Stop();
            _player.ClearBuffer();
            while (_queue.TryTake(out _)) { }
        }

        /// <summary>
        /// Incoming AMBE frame (should be 9 bytes). Caller filters by TG/slot before enqueue.
        /// </summary>
        public void AddFrame(byte[] ambe9)
        {
            if (ambe9 == null) return;
            if (ambe9.Length != 9) return;
            // Best-effort enqueue, drop if full
            if (_started && !_queue.IsAddingCompleted)
                _queue.TryAdd(ambe9.ToArray());
        }

        private async Task ConsumeLoop()
        {
            var token = _cts.Token;
            try
            {
                var startFrames = Math.Max(1, TargetLatencyMs / 20);
                while (_queue.Count < startFrames && !token.IsCancellationRequested)
                    await Task.Delay(5, token).ConfigureAwait(false);

                while (!token.IsCancellationRequested)
                {
                    if (_queue.TryTake(out var ambe))
                    {
                        try
                        {
                            var pcm = _vocoder.Decode(ambe);
                            if (pcm.Length != 160)
                                pcm = new short[160];
                            _player.AddPcm(pcm);
                            LevelChanged?.Invoke(GetLevel(pcm));
                        }
                        catch
                        {
                            var silence = new short[160];
                            _player.AddPcm(silence);
                            LevelChanged?.Invoke(0);
                        }
                    }
                    else
                    {
                        LevelChanged?.Invoke(0);
                    }
                    await Task.Delay(20, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        }

        private static double GetLevel(short[] pcm)
        {
            double sum = 0;
            foreach (var sample in pcm)
                sum += (double)sample * sample;
            return Math.Sqrt(sum / pcm.Length) / short.MaxValue;
        }

        public void Dispose()
        {
            Stop();
            _cts.Dispose();
            _queue.Dispose();
            _vocoder.Dispose();
        }
    }
}
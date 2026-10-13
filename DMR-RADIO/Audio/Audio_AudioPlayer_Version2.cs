using System;
using NAudio.Wave;

namespace DMR_RADIO.Audio
{
    /// <summary>
    /// Simple playback wrapper using NAudio BufferedWaveProvider.
    /// </summary>
    public class AudioPlayer : IDisposable
    {
        private readonly WaveOut _waveOut;
        private readonly BufferedWaveProvider _buffer;

        public AudioPlayer(int deviceNumber = -1)
        {
            var wf = new WaveFormat(8000, 16, 1); // 8 kHz, 16-bit, mono
            _buffer = new BufferedWaveProvider(wf, TimeSpan.FromSeconds(5))
            {
                DiscardOnBufferOverflow = true
            };
            _waveOut = new WaveOut { DeviceNumber = deviceNumber };
            _waveOut.Init(_buffer);
        }

        public double Volume
        {
            get => _waveOut.Volume * 100;
            set => _waveOut.Volume = (float)(Math.Clamp(value, 0, 100) / 100);
        }

        public void Start() => _waveOut.Play();
        public void Stop() => _waveOut.Stop();
        public void ClearBuffer() => _buffer.ClearBuffer();

        /// <summary>
        /// Add PCM samples (short[], 16-bit) to buffer for playback.
        /// </summary>
        public void AddPcm(short[] pcm)
        {
            if (pcm == null || pcm.Length == 0) return;
            var bytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            _buffer.AddSamples(bytes, 0, bytes.Length);
        }

        public void Dispose()
        {
            try
            {
                _waveOut?.Stop();
            }
            catch { }
            _waveOut?.Dispose();
        }
    }
}
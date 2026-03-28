using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using DawEngine.Core;

namespace DawEngine.UI
{
    // ── Provider que aplica una cadena de efectos DSP a la señal ─────────────
    internal class FxChainSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly List<IAudioProcessor> _fx;
        private readonly float[] _buf = new float[8192];

        public WaveFormat WaveFormat => _source.WaveFormat;

        public FxChainSampleProvider(ISampleProvider source, List<IAudioProcessor> fx)
        {
            _source = source;
            _fx     = fx;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            if (read == 0 || _fx.Count == 0) return read;

            // Copiar al buffer temporal, procesar, copiar de vuelta
            // (procesamos mono mezclando L+R, luego duplicamos)
            int frames = read / 2; // estéreo interleaved
            if (frames > _buf.Length) frames = _buf.Length;

            // Extraer canal L para DSP (mono-sumado)
            for (int i = 0; i < frames; i++)
                _buf[i] = (buffer[offset + i * 2] + buffer[offset + i * 2 + 1]) * 0.5f;

            var span = _buf.AsSpan(0, frames);
            foreach (var p in _fx)
                if (p.IsEnabled) p.Process(span);

            // Escribir de vuelta en estéreo
            for (int i = 0; i < frames; i++)
            {
                buffer[offset + i * 2]     = _buf[i];
                buffer[offset + i * 2 + 1] = _buf[i];
            }

            return read;
        }
    }

    // ── Provider que aplica trim + fade + offset a un clip ───────────────────
    internal class ClipSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly StudioTrack     _track;
        private readonly int             _sampleRate;
        private int _samplesRead = 0;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public ClipSampleProvider(ISampleProvider source, StudioTrack track, int sampleRate)
        {
            _source     = source;
            _track      = track;
            _sampleRate = sampleRate;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            if (read == 0) return 0;

            int channels      = WaveFormat.Channels;
            int fadInSamples  = (int)(_track.FadeIn  * _sampleRate) * channels;
            int fadOutSamples = (int)(_track.FadeOut * _sampleRate) * channels;
            int totalSamples  = (int)((_track.TrimEnd - _track.TrimStart) * _sampleRate) * channels;

            for (int i = 0; i < read; i++)
            {
                int abs = _samplesRead + i;

                // Fade in
                float gain = 1f;
                if (fadInSamples > 0 && abs < fadInSamples)
                    gain *= (float)abs / fadInSamples;

                // Fade out
                int fromEnd = totalSamples - abs;
                if (fadOutSamples > 0 && fromEnd < fadOutSamples && fromEnd >= 0)
                    gain *= (float)fromEnd / fadOutSamples;

                buffer[offset + i] *= gain;
            }

            _samplesRead += read;
            return read;
        }
    }

    public class StudioTrack : IDisposable
    {
        public string  Name     { get; set; } = "";
        public string  FilePath { get; set; } = "";
        public bool    IsMuted  { get; set; } = false;
        public bool    IsSolo   { get; set; } = false;
        public float   Volume   { get; set; } = 1.0f;
        public float   Pan      { get; set; } = 0.0f;
        public bool    IsLoaded => _reader != null;

        // ── Propiedades de edición de clip ────────────────────────────────────
        public double ClipOffset { get; set; } = 0;   // segundos desde el inicio del timeline
        public double TrimStart  { get; set; } = 0;   // cortar N segundos del inicio del audio
        public double TrimEnd    { get; set; } = -1;  // -1 = hasta el final original
        public double FadeIn     { get; set; } = 0;   // duración del fade in en segundos
        public double FadeOut    { get; set; } = 0;   // duración del fade out en segundos

        // Duración efectiva del clip (después del trim)
        public double ClipDuration => _reader == null ? 0
            : (TrimEnd > 0 ? TrimEnd : _reader.TotalTime.TotalSeconds) - TrimStart;

        public double Duration  => _reader?.TotalTime.TotalSeconds ?? 0;
        public double Position  => _reader?.CurrentTime.TotalSeconds ?? 0;
        public float[]? WaveformPeaks    { get; private set; }
        public int      SourceSampleRate { get; private set; }

        // ── Cadena de efectos por pista ───────────────────────────────────────
        public readonly List<IAudioProcessor> FxProcessors = new();

        private AudioFileReader?         _reader;
        private VolumeSampleProvider?    _volumeProvider;
        private ISampleProvider?         _chain;

        public static WaveFormat MasterFormat { get; set; } =
            WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        public ISampleProvider? Chain => _chain;

        public void Load()
        {
            Dispose();
            if (string.IsNullOrEmpty(FilePath)) return;

            _reader          = new AudioFileReader(FilePath);
            SourceSampleRate = _reader.WaveFormat.SampleRate;

            // Aplicar TrimStart — saltar al punto de inicio del clip
            if (TrimStart > 0)
            {
                long pos = (long)(TrimStart * _reader.WaveFormat.SampleRate)
                           * _reader.WaveFormat.Channels
                           * (_reader.WaveFormat.BitsPerSample / 8);
                _reader.Position = Math.Clamp(pos, 0, _reader.Length);
            }

            ISampleProvider src = _reader;

            if (_reader.WaveFormat.Channels == 1)
                src = new MonoToStereoSampleProvider(src);

            if (_reader.WaveFormat.SampleRate != MasterFormat.SampleRate)
                src = new WdlResamplingSampleProvider(src, MasterFormat.SampleRate);

            // Aplicar TrimEnd — limitar duración del clip
            double rawDur   = _reader.TotalTime.TotalSeconds;
            double trimEnd  = TrimEnd > 0 ? Math.Min(TrimEnd, rawDur) : rawDur;
            double clipDur  = trimEnd - TrimStart;
            if (clipDur > 0 && clipDur < rawDur)
            {
                // CORRECCIÓN APLICADA: Casteo explícito a (int) para evitar el error CS0266
                int takeSamples = (int)(clipDur * MasterFormat.SampleRate * MasterFormat.Channels);
                src = new OffsetSampleProvider(src) { TakeSamples = takeSamples };
            }

            // Aplicar fades
            if (FadeIn > 0 || FadeOut > 0)
                src = new ClipSampleProvider(src, this, MasterFormat.SampleRate);

            // Insertar FX chain
            if (FxProcessors.Count > 0)
                src = new FxChainSampleProvider(src, FxProcessors);

            _volumeProvider = new VolumeSampleProvider(src) { Volume = Volume };
            _chain          = _volumeProvider;

            BuildWaveformPeaks();
        }

        public void UpdateVolume()
        {
            if (_volumeProvider != null)
                _volumeProvider.Volume = IsMuted ? 0f : Volume;
        }

        private void BuildWaveformPeaks()
        {
            if (_reader == null) return;
            try
            {
                using var r     = new AudioFileReader(FilePath);
                int    ch       = r.WaveFormat.Channels;
                int    total    = (int)(r.TotalTime.TotalSeconds * r.WaveFormat.SampleRate);
                int    count    = Math.Min(2000, Math.Max(1, total));
                int    stride   = Math.Max(1, total / count);
                var    peaks    = new float[count];
                var    buf      = new float[stride * ch * 4];

                for (int p = 0; p < count; p++)
                {
                    int read = r.Read(buf, 0, Math.Min(buf.Length, stride * ch));
                    if (read == 0) break;
                    float mx = 0;
                    for (int i = 0; i < read; i++)
                        if (Math.Abs(buf[i]) > mx) mx = Math.Abs(buf[i]);
                    peaks[p] = mx;
                }
                WaveformPeaks = peaks;
            }
            catch { WaveformPeaks = null; }
        }

        public void Seek(double seconds)
        {
            if (_reader == null) return;
            long pos = (long)(seconds * _reader.WaveFormat.SampleRate)
                       * _reader.WaveFormat.Channels
                       * (_reader.WaveFormat.BitsPerSample / 8);
            _reader.Position = Math.Clamp(pos, 0, _reader.Length);
            // Reconstruir chain para limpiar estado del resampler
            Load();
        }

        public void Dispose()
        {
            _reader?.Dispose();
            _reader          = null;
            _volumeProvider  = null;
            _chain           = null;
        }
    }

    public class StudioEngine : IDisposable
    {
        private readonly List<StudioTrack> _tracks = new();
        private IWavePlayer?         _player;
        private MixingSampleProvider? _mixer;

        public IReadOnlyList<StudioTrack> Tracks   => _tracks;
        public bool   IsPlaying { get; private set; }
        public float MasterVol { get; set; } = 1.0f;

        // Detecta el sample rate más común entre los archivos
        private static int BestSampleRate(IEnumerable<StudioTrack> tracks)
        {
            var rates = tracks
                .Where(t => !string.IsNullOrEmpty(t.FilePath))
                .Select(t =>
                {
                    try { using var r = new AudioFileReader(t.FilePath); return r.WaveFormat.SampleRate; }
                    catch { return 44100; }
                }).ToList();
            if (rates.Count == 0) return 44100;
            return rates.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
        }

        public StudioTrack AddTrack(string name, string filePath)
        {
            var t = new StudioTrack { Name = name, FilePath = filePath };
            _tracks.Add(t);
            return t;
        }

        public void RemoveTrack(StudioTrack track)
        {
            track.Dispose();
            _tracks.Remove(track);
        }

        public void Play()
        {
            Stop();
            if (_tracks.Count == 0) return;

            // Detectar el mejor sample rate
            int sr  = BestSampleRate(_tracks);
            var fmt = WaveFormat.CreateIeeeFloatWaveFormat(sr, 2);

            // Setear el formato maestro ANTES de cargar las pistas
            StudioTrack.MasterFormat = fmt;

            // Cargar todas las pistas con el formato correcto
            foreach (var t in _tracks)
            {
                t.Dispose(); // forzar reload con el nuevo formato
                t.Load();
                t.UpdateVolume();
            }

            // MixingSampleProvider de NAudio — maneja N pistas en el mismo formato
            _mixer = new MixingSampleProvider(fmt);
            _mixer.ReadFully = true; // no cortar si alguna pista termina antes

            foreach (var t in _tracks)
                if (t.Chain != null) _mixer.AddMixerInput(t.Chain);

            // Crear player
            try   { _player = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100); }
            catch { _player = new WaveOutEvent { DesiredLatency = 150 }; }

            _player.Init(_mixer);
            _player.Play();
            IsPlaying = true;
        }

        public void Pause()  { _player?.Pause();  IsPlaying = false; }
        public void Resume() { _player?.Play();   IsPlaying = true;  }

        public void Stop()
        {
            _player?.Stop();
            _player?.Dispose();
            _player   = null;
            _mixer    = null;
            IsPlaying = false;
            foreach (var t in _tracks) t.Seek(0);
        }

        public double Position => _tracks.Count > 0
            ? _tracks.Where(t => t.Duration > 0).Select(t => t.Position).DefaultIfEmpty(0).Max()
            : 0;

        public double Duration => _tracks.Count > 0
            ? _tracks.Select(t => t.Duration).DefaultIfEmpty(0).Max()
            : 0;

        public void ExportToWav(string outputPath, Action<double>? progress = null)
        {
            if (_tracks.Count == 0) return;

            int sr  = BestSampleRate(_tracks);
            var fmt = WaveFormat.CreateIeeeFloatWaveFormat(sr, 2);
            StudioTrack.MasterFormat = fmt;

            foreach (var t in _tracks) { t.Dispose(); t.Load(); t.UpdateVolume(); }

            double total = _tracks.Select(t => t.Duration).DefaultIfEmpty(0).Max();

            var mix = new MixingSampleProvider(fmt) { ReadFully = true };
            foreach (var t in _tracks)
                if (t.Chain != null) mix.AddMixerInput(t.Chain);

            using var writer = new WaveFileWriter(outputPath, fmt);
            var buf  = new float[sr / 10 * 2]; // 100ms
            double done = 0;

            while (done < total)
            {
                int r = mix.Read(buf, 0, buf.Length);
                if (r == 0) break;
                writer.WriteSamples(buf, 0, r);
                done += (double)r / (sr * 2);
                progress?.Invoke(Math.Min(1.0, done / total));
            }

            // Recargar para reproducción
            foreach (var t in _tracks) { t.Dispose(); t.Load(); }
        }

        public void Dispose()
        {
            Stop();
            foreach (var t in _tracks) t.Dispose();
        }
    }
}
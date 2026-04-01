using System;
using System.Collections.Generic;

namespace DawEngine.Core
{
    // Nota musical con su frecuencia de referencia
    public record TunerNote(string Name, float Frequency);

    public class TunerEngine
    {
        // Notas de la guitarra estándar (E2 a E4)
        public static readonly TunerNote[] GuitarStrings =
        {
            new("E2",  82.41f),
            new("A2",  110.00f),
            new("D3",  146.83f),
            new("G3",  196.00f),
            new("B3",  246.94f),
            new("E4",  329.63f),
        };

        // Escala cromática completa para afinador
        private static readonly (string Name, float Freq)[] ChromaticScale = BuildChromaticScale();

        private readonly int   _sampleRate;
        private readonly float[] _fftBuffer;
        private readonly int   _fftSize = 4096;
        private int   _writePos = 0;
        private bool  _bufferReady = false;

        public TunerEngine(int sampleRate = 48000)
        {
            _sampleRate = sampleRate;
            _fftBuffer  = new float[_fftSize];
        }

        // Alimentar muestras desde el callback de audio ASIO
        public void Feed(float[] samples, int count)
        {
            for (int i = 0; i < count; i++)
            {
                _fftBuffer[_writePos] = samples[i];
                _writePos++;
                if (_writePos >= _fftSize)
                {
                    _writePos   = 0;
                    _bufferReady = true;
                }
            }
        }

        // Resultado del afinador
        public TunerResult? Analyze()
        {
            if (!_bufferReady) return null;

            float detectedFreq = DetectFrequency();
            if (detectedFreq <= 0) return null;

            return FindClosestNote(detectedFreq);
        }

        // ── FFT simplificada (Goertzel + autocorrelación) ─────────────────────
        private float DetectFrequency()
        {
            // Aplicar ventana de Hanning para reducir spectral leakage
            var windowed = new float[_fftSize];
            for (int i = 0; i < _fftSize; i++)
            {
                double w = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (_fftSize - 1)));
                windowed[i] = _fftBuffer[i] * (float)w;
            }

            // Autocorrelación para detección de pitch (más precisa para instrumentos)
            double bestCorr = 0;
            int    bestLag  = 0;

            // Rango de búsqueda: 60 Hz (C2) a 1200 Hz (D6)
            int minLag = (int)(_sampleRate / 1200.0);
            int maxLag = (int)(_sampleRate / 60.0);
            maxLag = Math.Min(maxLag, _fftSize / 2);

            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double corr = 0;
                for (int i = 0; i < _fftSize - lag; i++)
                    corr += windowed[i] * windowed[i + lag];

                if (corr > bestCorr)
                {
                    bestCorr = corr;
                    bestLag  = lag;
                }
            }

            if (bestLag == 0 || bestCorr < 0.01) return 0;

            // RMS check — no analizar si la señal es muy débil (ruido)
            double rms = 0;
            for (int i = 0; i < _fftSize; i++) rms += windowed[i] * windowed[i];
            rms = Math.Sqrt(rms / _fftSize);
            if (rms < 0.005) return 0;

            return (float)(_sampleRate / (double)bestLag);
        }

        // Encuentra la nota más cercana a la frecuencia detectada
        private static TunerResult FindClosestNote(float freq)
        {
            string bestName  = "";
            float  bestFreq  = 0;
            float  bestCents = float.MaxValue;

            foreach (var (name, noteFreq) in ChromaticScale)
            {
                // Diferencia en cents: 1200 * log2(f1/f2)
                float cents = (float)(1200.0 * Math.Log2(freq / noteFreq));
                float absCents = Math.Abs(cents);

                if (absCents < Math.Abs(bestCents))
                {
                    bestCents = cents;
                    bestName  = name;
                    bestFreq  = noteFreq;
                }
            }

            // Buscar la cuerda de guitarra más cercana
            string? guitarString = null;
            float   bestStringDiff = float.MaxValue;
            foreach (var gs in GuitarStrings)
            {
                float diff = Math.Abs(freq - gs.Frequency);
                if (diff < bestStringDiff)
                {
                    bestStringDiff = diff;
                    guitarString   = gs.Name;
                }
            }

            return new TunerResult
            {
                DetectedFreq  = freq,
                ClosestNote   = bestName,
                TargetFreq    = bestFreq,
                CentsOff      = bestCents,
                IsInTune      = Math.Abs(bestCents) < 5f,
                GuitarString  = guitarString,
            };
        }

        // Genera la escala cromática desde C1 hasta C7
        private static (string, float)[] BuildChromaticScale()
        {
            string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            var list = new List<(string, float)>();

            // A4 = 440 Hz como referencia
            for (int octave = 1; octave <= 7; octave++)
            {
                for (int n = 0; n < 12; n++)
                {
                    // Semitones from A4
                    int semitones = (octave - 4) * 12 + (n - 9);
                    float freq    = 440f * (float)Math.Pow(2, semitones / 12.0);
                    list.Add(($"{noteNames[n]}{octave}", freq));
                }
            }
            return list.ToArray();
        }
    }

    public class TunerResult
    {
        public float   DetectedFreq  { get; set; }
        public string  ClosestNote   { get; set; } = "";
        public float   TargetFreq    { get; set; }
        public float   CentsOff      { get; set; }   // Negativo = bemol, positivo = sostenido
        public bool    IsInTune      { get; set; }
        public string? GuitarString  { get; set; }
    }
}

using System;

namespace DawEngine.Core
{
    public class DelayProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        // El Buffer Circular para guardar el pasado (hasta 2 segundos a 48kHz)
        private readonly float[] _circularBuffer;
        private int _writeIndex = 0;

        private readonly int _sampleRate;
        private int _delaySamples; // La "D" en tu fórmula

        // Parámetros
        private float _feedback = 0.4f; // La "f" en tu fórmula (0.0 a 0.9 máximo para no saturar)
        private float _mix = 0.5f;      // Mezcla entre señal seca y mojada

        public DelayProcessor(int sampleRate = 48000, float delayMs = 350f)
        {
            _sampleRate = sampleRate;
            _circularBuffer = new float[sampleRate * 2];
            UpdateDelayTime(delayMs);
        }

        public void UpdateParameter(string name, float value)
        {
            if (name == "Time") UpdateDelayTime(value);
            else if (name == "Feedback") _feedback = Math.Clamp(value, 0f, 0.95f);
            else if (name == "Mix") _mix = Math.Clamp(value, 0f, 1f);
        }

        private void UpdateDelayTime(float ms)
        {
            // Convertimos milisegundos a cantidad de muestras (samples)
            _delaySamples = (int)(_sampleRate * (ms / 1000f));
            if (_delaySamples >= _circularBuffer.Length) _delaySamples = _circularBuffer.Length - 1;
        }

        public void Process(Span<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                float x_n = buffer[i]; // x[n]: La señal que entra de la guitarra

                // Calculamos dónde está el pasado en el buffer circular
                int readIndex = _writeIndex - _delaySamples;
                if (readIndex < 0) readIndex += _circularBuffer.Length;

                float y_past = _circularBuffer[readIndex]; // y[n-D]

                // La ecuación matemática del efecto: y[n] = x[n] + f * y[n-D]
                float y_n = x_n + (_feedback * y_past);

                // Guardamos el resultado en el buffer circular para el futuro
                _circularBuffer[_writeIndex] = y_n;

                // Avanzamos el puntero en círculo
                _writeIndex++;
                if (_writeIndex >= _circularBuffer.Length) _writeIndex = 0;

                // Salida final: mezcla de la guitarra limpia (dry) + el eco (wet)
                buffer[i] = x_n + (y_past * _mix);
            }
        }
    }
}
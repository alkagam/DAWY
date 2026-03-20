using System;

namespace DawEngine.Core
{
    public class ChorusProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        private float _sampleRate = 48000f;

        // Memoria del Delay (Un buffer circular corto, máximo 1 segundo)
        private readonly float[] _circularBuffer;
        private int _writeIndex = 0;

        // Parámetros del LFO y el Delay
        private float _rate = 1.5f;       // Velocidad del LFO en Hz
        private float _baseDelayMs = 20f; // Tiempo base de retraso (Chorus usa ~20ms, Flanger usa ~3ms)
        private float _depthMs = 5f;      // Cuánto varía el tiempo (Amplitud de la modulación)
        private float _mix = 0.5f;        // 50% señal original, 50% modulada

        // Reloj del LFO
        private float _phase = 0f;

        public ChorusProcessor(int sampleRate = 48000)
        {
            _sampleRate = sampleRate;
            _circularBuffer = new float[sampleRate]; // 1 segundo de memoria es suficiente
        }

        public void UpdateParameter(string name, float value)
        {
            if (name == "Rate") _rate = Math.Max(0.1f, value);
            else if (name == "Depth") _depthMs = Math.Max(0f, value);
            else if (name == "Delay") _baseDelayMs = Math.Max(0.1f, value);
            else if (name == "Mix") _mix = Math.Clamp(value, 0f, 1f);
        }

        public void Process(Span<float> buffer)
        {
            float phaseIncrement = 2f * MathF.PI * _rate / _sampleRate;

            for (int i = 0; i < buffer.Length; i++)
            {
                float x = buffer[i];

                // 1. Escribimos la señal actual en el buffer circular
                _circularBuffer[_writeIndex] = x;

                // 2. Calculamos el LFO (-1.0 a 1.0)
                float lfo = MathF.Sin(_phase);

                // 3. Calculamos d(t): el delay actual en milisegundos y lo pasamos a muestras
                float currentDelayMs = _baseDelayMs + (_depthMs * lfo);
                float delaySamples = currentDelayMs * (_sampleRate / 1000f);

                // 4. Encontramos la posición de lectura (hacia atrás en el tiempo)
                float readPosition = _writeIndex - delaySamples;
                if (readPosition < 0) readPosition += _circularBuffer.Length;

                // 5. ¡EL RETO: INTERPOLACIÓN LINEAL!
                int index1 = (int)readPosition; // Parte entera
                int index2 = index1 + 1;        // La muestra siguiente
                if (index2 >= _circularBuffer.Length) index2 = 0; // Wrap-around

                float fraction = readPosition - index1; // Parte fraccional (0.0 a 1.0)

                // y_{interpolado} = (1 - f) * x[i] + f * x[i+1]
                float delayedSample = (1f - fraction) * _circularBuffer[index1] + fraction * _circularBuffer[index2];

                // 6. Mezclamos: Señal original + Señal modulada (Tu fórmula maestra)
                buffer[i] = x * (1f - _mix) + delayedSample * _mix;

                // 7. Avanzamos los relojes
                _writeIndex++;
                if (_writeIndex >= _circularBuffer.Length) _writeIndex = 0;

                _phase += phaseIncrement;
                if (_phase >= 2f * MathF.PI) _phase -= 2f * MathF.PI;
            }
        }
    }
}
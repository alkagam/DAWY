using System;

namespace DawEngine.Core
{
    public class MultiTapEchoProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        private float _sampleRate = 48000f;

        // El Buffer Circular de la línea de tiempo (2 segundos de memoria)
        private readonly float[] _buffer;
        private int _writeIndex = 0;

        // La distancia "D" entre cada rebote
        private float _baseDelayMs = 250f;
        private float _mix = 0.5f;

        // "a_k" en tu fórmula: Las amplitudes de cada rebote.
        // N = 3 (Tres ecos: 60% de volumen, 30% de volumen, 10% de volumen)
        private readonly float[] _tapAmplitudes = { 0.6f, 0.3f, 0.1f };

        public MultiTapEchoProcessor(int sampleRate = 48000)
        {
            _sampleRate = sampleRate;
            _buffer = new float[sampleRate * 2];
        }

        public void UpdateParameter(string name, float value)
        {
            if (name == "Delay") _baseDelayMs = Math.Clamp(value, 10f, 500f);
            else if (name == "Mix") _mix = Math.Clamp(value, 0f, 1f);
        }

        public void Process(Span<float> buffer)
        {
            // Convertimos la distancia 'D' (milisegundos) a muestras de memoria
            int delaySamples = (int)(_sampleRate * (_baseDelayMs / 1000f));

            for (int i = 0; i < buffer.Length; i++)
            {
                float x_n = buffer[i];
                float echoesSum = 0f;

                // Aplicamos la sumatoria de tu fórmula: k=1 hasta N
                for (int k = 1; k <= _tapAmplitudes.Length; k++)
                {
                    // Calculamos el índice en el pasado: n - kD
                    int readIndex = _writeIndex - (k * delaySamples);

                    // Manejo del buffer circular (Wrap-around)
                    while (readIndex < 0) readIndex += _buffer.Length;

                    // Sumamos este tap: a_k * x[n - kD]
                    echoesSum += _tapAmplitudes[k - 1] * _buffer[readIndex];
                }

                // Escribimos la guitarra cruda en el buffer para que sea leída en el futuro
                _buffer[_writeIndex] = x_n;

                // Salida final: y[n] = x[n] + sumatoria (mezclado por seguridad)
                buffer[i] = x_n * (1f - _mix) + echoesSum * _mix;

                // Avanzamos el reloj maestro
                _writeIndex++;
                if (_writeIndex >= _buffer.Length) _writeIndex = 0;
            }
        }
    }
}
using System;

namespace DawEngine.Core
{
    public class PitchShifterProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        private readonly float[] _buffer;
        private int _writeIndex = 0;

        // El reloj de lectura fraccional
        private float _readIndex = 0f;

        // La 'alpha' de tu fórmula (Rate). 
        // 1.0 = Tono original, 2.0 = Una octava arriba, 0.5 = Una octava abajo
        private float _alpha = 1.0f;

        public PitchShifterProcessor(int bufferSize = 48000)
        {
            _buffer = new float[bufferSize]; // 1 segundo de memoria
        }

        public void UpdateParameter(string name, float value)
        {
            if (name == "Alpha")
            {
                _alpha = Math.Clamp(value, 0.5f, 2.0f);
            }
        }

        public void Process(Span<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                float x_n = buffer[i];

                // 1. Escribimos la guitarra normal en la memoria a velocidad constante
                _buffer[_writeIndex] = x_n;

                // 2. Leemos la memoria usando tu matemática: índice * alpha
                // Usamos interpolación lineal rápida para los decimales
                int index1 = (int)_readIndex;
                int index2 = (index1 + 1) % _buffer.Length;
                float fraction = _readIndex - index1;

                float y_n = (1f - fraction) * _buffer[index1] + fraction * _buffer[index2];

                // 3. Salida final
                buffer[i] = y_n;

                // 4. Avanzamos los relojes
                _writeIndex++;
                if (_writeIndex >= _buffer.Length) _writeIndex = 0;

                // El reloj de lectura avanza según 'alpha'. 
                // Si alpha es 2, el lector avanza el doble de rápido que el escritor.
                _readIndex += _alpha;
                if (_readIndex >= _buffer.Length) _readIndex -= _buffer.Length;
            }
        }
    }
}
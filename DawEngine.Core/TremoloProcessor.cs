using System;

namespace DawEngine.Core
{
    public class TremoloProcessor : IAudioProcessor
    {
        public bool IsEnabled { get; set; } = true;

        private float _sampleRate = 48000f; // F_s

        // Parámetros
        private float _rate = 5.0f;  // La "f" en tu fórmula: Velocidad del LFO en Hz
        private float _depth = 0.8f; // La "d" en tu fórmula: Qué tan profundo es el efecto (0.0 a 1.0)

        // Memoria de estado para el LFO
        private float _phase = 0f;

        public void UpdateParameter(string name, float value)
        {
            if (name == "Rate") _rate = Math.Max(0.1f, value);
            else if (name == "Depth") _depth = Math.Clamp(value, 0f, 1f);
        }

        public void Process(Span<float> buffer)
        {
            // Calculamos cuánto avanza el ángulo por cada muestra procesada
            float phaseIncrement = 2f * MathF.PI * _rate / _sampleRate;

            for (int i = 0; i < buffer.Length; i++)
            {
                // 1. Calculamos el valor del oscilador: sin(2*pi*f*n / F_s)
                float lfo = MathF.Sin(_phase);

                // 2. Aplicamos tu ecuación matemática
                // Multiplicamos la señal original por la onda del oscilador
                buffer[i] = buffer[i] * (1f + _depth * lfo);

                // 3. Avanzamos el reloj del LFO
                _phase += phaseIncrement;

                // Si damos la vuelta completa al círculo trigonométrico, reiniciamos 
                // para evitar el desbordamiento de memoria (overflow)
                if (_phase >= 2f * MathF.PI)
                {
                    _phase -= 2f * MathF.PI;
                }
            }
        }
    }
}
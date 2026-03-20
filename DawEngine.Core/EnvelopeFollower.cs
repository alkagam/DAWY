using System;

namespace DawEngine.Core
{
    // Nota: Esta clase no modifica el audio (no es IAudioProcessor), 
    // solo lo "escucha" y nos da un valor para la interfaz gráfica.
    public class EnvelopeFollower
    {
        // 'alpha' en tu fórmula: Qué tan rápido reacciona la aguja (0.0 a 1.0)
        // 0.999f es un movimiento suave, 0.5f es muy nervioso
        private float _alpha = 0.999f;

        // env[n-1]: La memoria del estado anterior
        private float _envelope = 0f;

        // Propiedad pública para que la interfaz (XAML) pueda leer el nivel en tiempo real
        public float CurrentLevel => _envelope;

        public void ProcessBlock(ReadOnlySpan<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                // Tu ecuación matemática exacta:
                // env[n] = (1 - alpha) * |x[n]| + alpha * env[n-1]
                _envelope = (1f - _alpha) * MathF.Abs(buffer[i]) + _alpha * _envelope;
            }
        }
    }
}
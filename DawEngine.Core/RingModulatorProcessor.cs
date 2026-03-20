using System;

namespace DawEngine.Core
{
	public class RingModulatorProcessor : IAudioProcessor
	{
		public bool IsEnabled { get; set; } = true;

		private float _sampleRate = 48000f;

		// 'f' en tu fórmula: Frecuencia de la portadora (Carrier)
		// Valores entre 100Hz y 1000Hz generan los sonidos más locos.
		private float _frequency = 400f;

		// Reloj de fase para el oscilador
		private float _phase = 0f;

		public RingModulatorProcessor(int sampleRate = 48000)
		{
			_sampleRate = sampleRate;
		}

		public void UpdateParameter(string name, float value)
		{
			if (name == "Frequency")
			{
				_frequency = Math.Clamp(value, 20f, 2000f);
			}
		}

		public void Process(Span<float> buffer)
		{
			float phaseIncrement = 2f * MathF.PI * _frequency / _sampleRate;

			for (int i = 0; i < buffer.Length; i++)
			{
				// 1. Calculamos la onda portadora (Seno puro de -1.0 a 1.0)
				float carrier = MathF.Sin(_phase);

				// 2. Tu ecuación exacta: y[n] = x[n] * sin(...)
				buffer[i] = buffer[i] * carrier;

				// 3. Avanzamos el reloj
				_phase += phaseIncrement;
				if (_phase >= 2f * MathF.PI) _phase -= 2f * MathF.PI;
			}
		}
	}
}
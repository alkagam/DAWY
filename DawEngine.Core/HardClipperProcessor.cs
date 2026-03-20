using System;

namespace DawEngine.Core
{
	public class HardClipperProcessor : IAudioProcessor
	{
		public bool IsEnabled { get; set; } = true;

		// Parámetros de tu fórmula
		private float _drive = 10.0f; // Ganancia de entrada antes de recortar
		private float _threshold = 0.5f; // "T" en tu fórmula. El límite de voltaje digital.

		public void UpdateParameter(string name, float value)
		{
			if (name == "Drive") _drive = Math.Max(value, 1f);
			else if (name == "Threshold") _threshold = Math.Clamp(value, 0.1f, 1f);
		}

		public void Process(Span<float> buffer)
		{
			for (int i = 0; i < buffer.Length; i++)
			{
				// 1. Amplificamos la señal (Drive) para forzar el recorte
				float x = buffer[i] * _drive;

				// 2. Aplicamos la lógica matemática de tu lista maestra (función a trozos)
				if (x < -_threshold)
				{
					buffer[i] = -_threshold; // y = -T
				}
				else if (x > _threshold)
				{
					buffer[i] = _threshold; // y = T
				}
				else
				{
					buffer[i] = x; // y = x
				}

				// Nota de ingeniero: Math.Clamp(x, -_threshold, _threshold) 
				// hace exactamente lo mismo y es más rápido, pero este código 
				// es matemáticamente idéntico a tu fórmula escrita.
			}
		}
	}
}
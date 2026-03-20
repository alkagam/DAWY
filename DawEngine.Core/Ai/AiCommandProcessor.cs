using System;

namespace DawEngine.Core.Ai
{
	public class AiCommandProcessor
	{
		private readonly EffectChain _effectChain;

		public AiCommandProcessor(EffectChain chain)
		{
			_effectChain = chain;
		}

		public string Execute(AiResponse response)
		{
			if (string.IsNullOrEmpty(response.EffectName))
				return response.Message;

			// Buscamos el pedal en la cadena de efectos
			foreach (var processor in _effectChain.GetProcessors())
			{
				// Verificamos si el nombre de la clase coincide (ej: "HardClipperProcessor")
				if (processor.GetType().Name.Contains(response.EffectName))
				{
					processor.UpdateParameter(response.ParameterName, response.Value);
					return response.Message;
				}
			}

			return "Efecto no encontrado en el Rack actual.";
		}
	}
}
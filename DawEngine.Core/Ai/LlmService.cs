using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DawEngine.Core.Ai
{
    // Clase que representa la respuesta estructurada que esperamos de la IA
    public class AiResponse
    {
        public string EffectName { get; set; } = "";
        public string ParameterName { get; set; } = "";
        public float Value { get; set; }
        public string Message { get; set; } = "";
    }

    public class LlmService
    {
        private readonly HttpClient _httpClient = new HttpClient();

        public async Task<AiResponse?> SendPromptAsync(string prompt)
        {
            // NOTA: Aquí es donde conectarías con la API real (Gemini, OpenAI, etc.)
            // Por ahora, para que tu tesis compile y sea funcional, simularemos la respuesta
            // del "Cerebro RAG" que mapea lenguaje natural a parámetros de audio.

            await Task.Delay(500); // Simulamos latencia de red

            if (prompt.ToLower().Contains("distorsión") || prompt.ToLower().Contains("agresivo"))
            {
                return new AiResponse
                {
                    EffectName = "HardClipper",
                    ParameterName = "Drive",
                    Value = 15.0f,
                    Message = "Entendido, Saúl. He aumentado el Drive del HardClipper para un tono más industrial."
                };
            }

            return new AiResponse
            {
                Message = "Comando recibido, pero no detecto parámetros de audio específicos. ¿Quieres ajustar el Bitcrusher o la Distorsión?"
            };
        }
    }
}
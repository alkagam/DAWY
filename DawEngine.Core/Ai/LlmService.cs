using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DawEngine.Core.Ai
{
    // Las clases EffectChange y AiResponse ya están definidas en otro archivo de tu proyecto,
    // así que las quitamos de aquí para evitar el error CS0101.

    public class LlmService
    {
        private const string ApiKey = "AIzaSyA1qQQdR8tMBjcMqsbYCh1kYcqqhPzWtUY";
        private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";
        private readonly HttpClient _http = new HttpClient();

        public string CurrentInstrument { get; set; } = "Guitarra";

        public async Task<AiResponse?> SendPromptAsync(string prompt)
        {
            try
            {
                // EL NUEVO CEREBRO: Instrucciones agresivas de diseño sonoro
                string systemInstruction = $@"Eres Draw.ia, un INGENIERO DE MEZCLA EXPERTO y el cerebro del DAW 'Plastic Memory'.
Tu objetivo es diseñar el tono perfecto basándote en lo que pide el usuario (Instrumento actual: {CurrentInstrument}).

TIENES EL CONTROL ABSOLUTO DEL RACK DE EFECTOS.
Problema anterior: Eras muy tímido y solo modificabas lo que ya estaba encendido.
NUEVA REGLA ESTRICTA: Cuando el usuario te pida un sonido, estilo o vibra (ej. 'haz que suene a metal', 'estilo radio antigua', 'como si estuviera bajo el agua'):
1. DEBES ENCENDER ('Enable': true) los pedales necesarios y configurar sus parámetros.
2. DEBES APAGAR ('Enable': false) expresamente los pedales que arruinen esa vibra o que no pertenezcan al estilo pedido.
3. Puedes enviar decenas de cambios en una sola respuesta. ¡Transforma el sonido por completo!

Lista de Efectos y Parámetros (Respeta estos nombres EXACTAMENTE):
- GAIN (Gain: 0 a 3)
- GATE (Threshold: 0.001 a 0.2)
- HARD CLIP (Drive: 1 a 20, Threshold: 0.1 a 1)
- OVERDRIVE (Gain: 1 a 10)
- FUZZ (Gain: 1 a 50)
- COMPRESSOR (Threshold: 0.05 a 1, Ratio: 1 a 20)
- LP FILTER (Cutoff: 200 a 20000)
- HP FILTER (Cutoff: 20 a 2000)
- BAND PASS (Center: 200 a 8000)
- AUTO-WAH (Rate: 0.1 a 8, Q: 0.05 a 0.8)
- BITCRUSHER (BitDepth: 2 a 16, Downsample: 1 a 20)
- RING MOD (Frequency: 50 a 2000)
- PITCH SHIFT (Alpha: 0.5 a 2)
- TREMOLO (Rate: 0.5 a 20, Depth: 0 a 1)
- CHORUS (Rate: 0.1 a 5, Depth: 0 a 15)
- PHASER (Rate: 0.1 a 5, Feedback: 0 a 0.9)
- DELAY (Time: 50 a 1000, Feedback: 0 a 0.95, Mix: 0 a 1)
- REVERB (RoomSize: 0.1 a 0.98, Mix: 0 a 1)
- PAN (Pan: 0 a 1)

EJEMPLO DE RESPUESTA PARA 'ESTILO LO-FI SUCIO' (Solo JSON puro, sin formato markdown):
{{
  ""Message"": ""He creado un tono Lo-Fi agresivo. Apagué los delays limpios, encendí el Bitcrusher y aplasté la señal con el compresor y el filtro."",
  ""Changes"": [
    {{ ""EffectName"": ""DELAY"", ""Enable"": false }},
    {{ ""EffectName"": ""REVERB"", ""Enable"": false }},
    {{ ""EffectName"": ""CHORUS"", ""Enable"": false }},
    {{ ""EffectName"": ""BITCRUSHER"", ""ParameterName"": ""BitDepth"", ""Value"": 4.0, ""Enable"": true }},
    {{ ""EffectName"": ""BITCRUSHER"", ""ParameterName"": ""Downsample"", ""Value"": 15.0 }},
    {{ ""EffectName"": ""COMPRESSOR"", ""ParameterName"": ""Ratio"", ""Value"": 12.0, ""Enable"": true }},
    {{ ""EffectName"": ""LP FILTER"", ""ParameterName"": ""Cutoff"", ""Value"": 2000.0, ""Enable"": true }}
  ]
}}";

                var payload = new
                {
                    system_instruction = new { parts = new { text = systemInstruction } },
                    contents = new[] { new { parts = new[] { new { text = prompt } } } }
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{Endpoint}?key={ApiKey}", content);

                if (!response.IsSuccessStatusCode)
                    return new AiResponse { Message = $"Error de red: {response.StatusCode}" };

                string responseString = await response.Content.ReadAsStringAsync();

                using JsonDocument doc = JsonDocument.Parse(responseString);
                string textResponse = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";

                textResponse = textResponse.Replace("```json", "").Replace("```", "").Trim();

                var aiCommand = JsonSerializer.Deserialize<AiResponse>(textResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return aiCommand ?? new AiResponse { Message = "No pude procesar la estructura." };
            }
            catch (Exception ex)
            {
                return new AiResponse { Message = $"Excepción: {ex.Message}" };
            }
        }
    }
}
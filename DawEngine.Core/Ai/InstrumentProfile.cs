using System.Collections.Generic;

namespace DawEngine.Core.Ai
{
    // Un ajuste individual: qué parámetro cambiar y a qué valor
    public class EffectSetting
    {
        public string ParameterName { get; set; } = "";
        public float  Value         { get; set; }
        public bool   Enable        { get; set; } = true; // Si este efecto debe encenderse
    }

    // Perfil completo de un instrumento:
    // clave = nombre del pedal (igual que en _pedalDefs de MainWindow)
    // valor = lista de parámetros a ajustar
    public class InstrumentProfile
    {
        public string Name        { get; set; } = "";
        public string PresetLabel { get; set; } = ""; // Ej: "Metal", "Clean", "Slap"
        public string Description { get; set; } = "";

        // Efectos que se ENCIENDEN con sus valores
        // Key: nombre del pedal (ej: "HARD CLIP", "REVERB", "DELAY"...)
        public Dictionary<string, List<EffectSetting>> Effects { get; set; } = new();

        // Efectos que se APAGAN explícitamente (todos los demás se quedan como están)
        public List<string> DisabledEffects { get; set; } = new();
    }

    // Respuesta extendida de la IA — puede cambiar múltiples efectos a la vez
    public class AiResponse
    {
        public string          Message        { get; set; } = "";
        public string?         ProfileApplied { get; set; } = null;

        // Lista de cambios individuales (para ajustes finos)
        public List<EffectChange> Changes { get; set; } = new();
    }

    public class EffectChange
    {
        public string EffectName     { get; set; } = "";
        public string ParameterName  { get; set; } = "";
        public float  Value          { get; set; }
        public bool?  Enable         { get; set; } = null; // null = no cambiar el estado
    }
}

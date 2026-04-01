using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.Wave;
using DawEngine.Core;
using DawEngine.Core.Ai;

namespace DawEngine.UI
{
    public partial class LiveWindow : Window
    {
        // ── Hardware ──────────────────────────────────────────────────────────
        private AsioOut? _asioOut;
        private DspEngine? _dspEngine;
        private EffectChain? _chain;
        private float[]? _audioBuffer;

        // ── Afinador ─────────────────────────────────────────────────────────
        private readonly TunerEngine _tuner = new(48000);
        private readonly DispatcherTimer _tunerTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

        // ── IA ────────────────────────────────────────────────────────────────
        private readonly LlmService _ai = new();
        private AiCommandProcessor? _brain;
        private readonly DawUser _user;
        private readonly string _instrument;

        // ── Rack dinámico ────────────────────────────────────────────────────
        // Lista ordenada de pedales activos en la pedalera (en orden de señal)
        private readonly List<PedalSlot> _activeSlots = new();

        // Pedal siendo arrastrado
        private PedalSlot? _dragSlot;
        private int _dragSourceIndex = -1;

        // Directorio de presets
        private static readonly string PresetsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DAWY", "LivePresets");

        // ── Definición completa del catálogo de 19 pedales ───────────────────
        private static readonly DawEngine.UI.PedalDef[] AllPedals =
        {
            new("GAIN",        "#FFD700", new[]{ new DawEngine.UI.PedalParam("LEVEL","Gain",0,3,1) }),
            new("GATE",        "#88FF44", new[]{ new DawEngine.UI.PedalParam("THRESH","Threshold",0.001,0.2,0.01) }),
            new("HARD CLIP",   "#FF007F", new[]{ new DawEngine.UI.PedalParam("DRIVE","Drive",1,20,5), new DawEngine.UI.PedalParam("THRESH","Threshold",0.1,1,0.5) }),
            new("OVERDRIVE",   "#FF8800", new[]{ new DawEngine.UI.PedalParam("GAIN","Gain",1,10,2) }),
            new("FUZZ",        "#FF4400", new[]{ new DawEngine.UI.PedalParam("FUZZ","Gain",1,50,20) }),
            new("COMPRESSOR",  "#AAFFAA", new[]{ new DawEngine.UI.PedalParam("THRESH","Threshold",0.05,1,0.5), new DawEngine.UI.PedalParam("RATIO","Ratio",1,20,4) }),
            new("LP FILTER",   "#00E5FF", new[]{ new DawEngine.UI.PedalParam("CUTOFF","Cutoff",200,20000,4000) }),
            new("HP FILTER",   "#00AAFF", new[]{ new DawEngine.UI.PedalParam("CUTOFF","Cutoff",20,2000,100) }),
            new("BAND PASS",   "#0088FF", new[]{ new DawEngine.UI.PedalParam("CENTER","Center",200,8000,1000) }),
            new("WAH",         "#FFAA00", new[]{ new DawEngine.UI.PedalParam("RATE","Rate",0.1,8,2), new DawEngine.UI.PedalParam("Q","Q",0.05,0.8,0.2) }),
            new("BITCRUSHER",  "#B000FF", new[]{ new DawEngine.UI.PedalParam("BITS","BitDepth",2,16,16), new DawEngine.UI.PedalParam("RATE÷","Downsample",1,20,1) }),
            new("RING MOD",    "#FF00AA", new[]{ new DawEngine.UI.PedalParam("FREQ","Frequency",50,2000,400) }),
            new("PITCH SHIFT", "#FF44FF", new[]{ new DawEngine.UI.PedalParam("RATIO","Alpha",0.5,2,1) }),
            new("TREMOLO",     "#44FFDD", new[]{ new DawEngine.UI.PedalParam("RATE","Rate",0.5,20,5), new DawEngine.UI.PedalParam("DEPTH","Depth",0,1,0.8) }),
            new("CHORUS",      "#44DDFF", new[]{ new DawEngine.UI.PedalParam("RATE","Rate",0.1,5,1.5), new DawEngine.UI.PedalParam("DEPTH","Depth",0,15,5) }),
            new("PHASER",      "#AA44FF", new[]{ new DawEngine.UI.PedalParam("RATE","Rate",0.1,5,1), new DawEngine.UI.PedalParam("FDBK","Feedback",0,0.9,0.5) }),
            new("DELAY",       "#FFDD00", new[]{ new DawEngine.UI.PedalParam("TIME","Time",50,1000,350), new DawEngine.UI.PedalParam("FDBK","Feedback",0,0.95,0.4), new DawEngine.UI.PedalParam("MIX","Mix",0,1,0.5) }),
            new("REVERB",      "#88AAFF", new[]{ new DawEngine.UI.PedalParam("ROOM","RoomSize",0.1,0.98,0.8), new DawEngine.UI.PedalParam("MIX","Mix",0,1,0.4) }),
            new("PAN",         "#FFFFFF", new[]{ new DawEngine.UI.PedalParam("L◄►R","Pan",0,1,0.5) }),
        };

        // ─────────────────────────────────────────────────────────────────────
        public LiveWindow(DawUser user, string instrument)
        {
            InitializeComponent();
            _user = user;
            _instrument = instrument;

            Title = $"DAWY — En Vivo  //  {instrument}  //  {user.Name}";
            TxtInstrument.Text = instrument;
            TxtInstrumentSub.Text = instrument;
            _ai.CurrentInstrument = instrument;

            Directory.CreateDirectory(PresetsDir);

            BuildCatalog();
            BuildQuickPresets();
            LoadDefaultProfile();

            _tunerTimer.Tick += TunerTimer_Tick;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _tunerTimer.Stop();
            StopAudio();
        }

        // ══════════════════════════════════════════════════════════════════════
        // CATÁLOGO DE PEDALES
        // ══════════════════════════════════════════════════════════════════════

        private void BuildCatalog()
        {
            CatalogPanel.Children.Clear();
            foreach (var def in AllPedals)
            {
                var color = (Color)ColorConverter.ConvertFromString(def.ColorHex);
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 4),
                    Cursor = Cursors.Hand,
                    Tag = def.Name,
                };

                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Margin = new Thickness(0, 0, 6, 0),
                    Fill = new SolidColorBrush(color),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                row.Children.Add(new TextBlock
                {
                    Text = def.Name,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                });

                border.Child = row;
                border.MouseLeftButtonDown += CatalogItem_Click;
                border.MouseMove += CatalogItem_DragStart;
                CatalogPanel.Children.Add(border);
            }
        }

        private void CatalogItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string name)
                AddPedalToBoard(name);
        }

        private void CatalogItem_DragStart(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (sender is Border b && b.Tag is string name)
                DragDrop.DoDragDrop(b, $"CATALOG:{name}", DragDropEffects.Copy);
        }

        // ══════════════════════════════════════════════════════════════════════
        // PEDALERA — agregar, quitar, reordenar
        // ══════════════════════════════════════════════════════════════════════

        private void AddPedalToBoard(string pedalName, bool enabled = true,
            Dictionary<string, double>? paramValues = null, bool rebuild = true)
        {
            var def = AllPedals.FirstOrDefault(p => p.Name == pedalName);
            if (def == null) return;

            // Crear procesador de audio
            var processor = CreateProcessor(pedalName);
            if (processor == null) return;
            processor.IsEnabled = enabled;

            // Valores de parámetros
            var currentValues = new Dictionary<string, double>();
            foreach (var p in def.Params)
            {
                double v = paramValues != null && paramValues.TryGetValue(p.Key, out var pv) ? pv : p.Default;
                currentValues[p.Key] = v;
                processor.UpdateParameter(p.Key, (float)v);
            }

            var slot = new PedalSlot
            {
                PedalName = pedalName,
                Def = def,
                Processor = processor,
                IsEnabled = enabled,
                ParamValues = currentValues,
            };

            _activeSlots.Add(slot);

            // Reconstruir chain solo si se pide (evitar rebuild en cada iteración de un loop)
            if (rebuild) RebuildChain();

            slot.Visual = BuildPedalVisual(slot);
            PedalboardPanel.Children.Add(slot.Visual);

            AddMsg($"Pedal \"{pedalName}\" agregado a la pedalera.", false);
        }

        private void RemovePedalFromBoard(PedalSlot slot)
        {
            _activeSlots.Remove(slot);
            PedalboardPanel.Children.Remove(slot.Visual);
            // Rebuild chain
            RebuildChain();
            AddMsg($"Pedal \"{slot.PedalName}\" eliminado.", false);
        }

        // Construye el visual de un pedal en la pedalera
        private UIElement BuildPedalVisual(PedalSlot slot)
        {
            var def = slot.Def;
            var color = (Color)ColorConverter.ConvertFromString(def.ColorHex);

            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x15)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(10, 8, 10, 8),
                Width = 90 + def.Params.Length * 44,
                AllowDrop = true,
                Cursor = Cursors.SizeAll,
                Tag = slot,
            };

            // Drag desde el pedal para reordenar
            container.MouseLeftButtonDown += PedalVisual_MouseDown;
            container.MouseMove += PedalVisual_DragStart;
            container.Drop += PedalVisual_Drop;
            container.DragOver += Pedalboard_DragOver;

            var stack = new StackPanel();

            // Header: gripper + LED + nombre + X
            var hdr = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Gripper visual
            var grip = new TextBlock
            {
                Text = "⠿",
                Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
                Cursor = Cursors.SizeAll,
            };
            Grid.SetColumn(grip, 0);

            // LED toggle
            var led = new CheckBox
            {
                IsChecked = slot.IsEnabled,
                Style = (Style)FindResource("LedToggle"),
                Margin = new Thickness(0, 0, 5, 0),
            };
            slot.Led = led;
            led.Checked += (_, _) => { slot.Processor.IsEnabled = true; slot.IsEnabled = true; };
            led.Unchecked += (_, _) => { slot.Processor.IsEnabled = false; slot.IsEnabled = false; };
            Grid.SetColumn(led, 1);

            // Nombre
            var lbl = new TextBlock
            {
                Text = def.Name,
                Foreground = new SolidColorBrush(color),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(lbl, 2);

            // Botón X — quitar pedal
            var btnX = new Button
            {
                Content = "✕",
                Width = 18,
                Height = 18,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                FontSize = 9,
                Cursor = Cursors.Hand,
            };
            var capturedSlot = slot;
            btnX.Click += (_, _) => RemovePedalFromBoard(capturedSlot);
            Grid.SetColumn(btnX, 3);

            hdr.Children.Add(grip);
            hdr.Children.Add(led);
            hdr.Children.Add(lbl);
            hdr.Children.Add(btnX);
            stack.Children.Add(hdr);

            // Separador
            stack.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)),
                Margin = new Thickness(0, 0, 0, 8),
            });

            // Knobs
            var knobRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            foreach (var param in def.Params)
            {
                double curVal = slot.ParamValues.TryGetValue(param.Key, out var pv) ? pv : param.Default;
                double dragY = 0;
                double dragVal = curVal;
                bool dragging = false;
                bool mouseDown = false;   // click sin confirmar drag aún
                double mouseDownY = 0;
                const double DragThreshold = 4.0; // píxeles mínimos para iniciar drag

                var kStack = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
                var canvas = new Canvas { Width = 44, Height = 44, Cursor = Cursors.SizeNS };
                DrawKnob(canvas, color, curVal, param.Min, param.Max);

                var cc = canvas; var cp = param; var cs = slot;

                canvas.MouseLeftButtonDown += (_, ev) =>
                {
                    mouseDown = true;
                    mouseDownY = ev.GetPosition(null).Y;
                    dragVal = curVal;
                    ev.Handled = true;
                };
                canvas.MouseMove += (_, ev) =>
                {
                    if (!mouseDown) return;

                    double currentY = ev.GetPosition(null).Y;

                    // Iniciar drag solo si superamos el umbral
                    if (!dragging)
                    {
                        if (Math.Abs(currentY - mouseDownY) < DragThreshold) return;
                        dragging = true;
                        dragY = mouseDownY;
                        cc.CaptureMouse();
                    }

                    double dy = dragY - currentY;
                    double nv = Math.Clamp(dragVal + dy / 100.0 * (cp.Max - cp.Min), cp.Min, cp.Max);
                    curVal = nv;
                    cs.ParamValues[cp.Key] = nv;
                    cs.Processor.UpdateParameter(cp.Key, (float)nv);
                    DrawKnob(cc, color, nv, cp.Min, cp.Max);
                };
                canvas.MouseLeftButtonUp += (_, _) =>
                {
                    mouseDown = false;
                    if (dragging)
                    {
                        dragging = false;
                        cc.ReleaseMouseCapture();
                    }
                };
                canvas.MouseLeave += (_, _) =>
                {
                    if (!dragging)
                    {
                        mouseDown = false;
                    }
                };
                canvas.MouseWheel += (_, ev) =>
                {
                    double step = (cp.Max - cp.Min) / 100.0;
                    double nv = Math.Clamp(curVal + (ev.Delta > 0 ? step : -step), cp.Min, cp.Max);
                    curVal = nv;
                    cs.ParamValues[cp.Key] = nv;
                    cs.Processor.UpdateParameter(cp.Key, (float)nv);
                    DrawKnob(cc, color, nv, cp.Min, cp.Max);
                    ev.Handled = true;
                };

                // Guardar referencia al canvas para actualización desde Draw.ia
                slot.KnobCanvases[param.Key] = (canvas, param.Min, param.Max, color,
                    (double v) => { curVal = v; }
                );

                kStack.Children.Add(canvas);
                kStack.Children.Add(new TextBlock
                {
                    Text = param.Label,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                    FontSize = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0),
                });
                knobRow.Children.Add(kStack);
            }

            stack.Children.Add(knobRow);
            container.Child = stack;
            return container;
        }

        // ══════════════════════════════════════════════════════════════════════
        // DRAG & DROP — reordenar pedales
        // ══════════════════════════════════════════════════════════════════════

        private Point _pedalDragStart;
        private bool _pedalMouseDown;

        private void PedalVisual_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            _pedalMouseDown = true;
            _pedalDragStart = e.GetPosition(null);
        }

        private void PedalVisual_DragStart(object sender, MouseEventArgs e)
        {
            if (!_pedalMouseDown || e.LeftButton != MouseButtonState.Pressed) return;

            var current = e.GetPosition(null);
            double dist = Math.Sqrt(
                Math.Pow(current.X - _pedalDragStart.X, 2) +
                Math.Pow(current.Y - _pedalDragStart.Y, 2));

            if (dist < 8) return; // umbral de 8px para iniciar drag del pedal

            if (sender is Border b && b.Tag is PedalSlot slot)
            {
                _pedalMouseDown = false;
                _dragSlot = slot;
                _dragSourceIndex = _activeSlots.IndexOf(slot);
                DragDrop.DoDragDrop(b, $"PEDAL:{slot.PedalName}", DragDropEffects.Move);
            }
        }

        private void PedalVisual_Drop(object sender, DragEventArgs e)
        {
            if (sender is Border b && b.Tag is PedalSlot targetSlot && _dragSlot != null
                && _dragSlot != targetSlot)
            {
                int fromIdx = _activeSlots.IndexOf(_dragSlot);
                int toIdx = _activeSlots.IndexOf(targetSlot);
                if (fromIdx < 0 || toIdx < 0) return;

                _activeSlots.RemoveAt(fromIdx);
                _activeSlots.Insert(toIdx, _dragSlot);
                RebuildPedalboardUI();
                RebuildChain();
                _dragSlot = null;
            }
        }

        private void Pedalboard_Drop(object sender, DragEventArgs e)
        {
            string? data = e.Data.GetData(DataFormats.StringFormat) as string;
            if (data == null) return;

            if (data.StartsWith("CATALOG:"))
                AddPedalToBoard(data[8..]);
            else if (data.StartsWith("PEDAL:") && _dragSlot != null)
            {
                // Soltar al final
                _activeSlots.Remove(_dragSlot);
                _activeSlots.Add(_dragSlot);
                RebuildPedalboardUI();
                RebuildChain();
                _dragSlot = null;
            }
        }

        private void Pedalboard_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void RebuildPedalboardUI()
        {
            PedalboardPanel.Children.Clear();
            foreach (var slot in _activeSlots)
            {
                slot.Visual = BuildPedalVisual(slot);
                PedalboardPanel.Children.Add(slot.Visual);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // MOTOR DE AUDIO ASIO
        // ══════════════════════════════════════════════════════════════════════

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Asegurar que el chain está actualizado con todos los slots
                RebuildChain();

                // Crear el motor DSP con el chain actual
                _chain ??= new EffectChain();
                _dspEngine = new DspEngine(48000, 2, _chain);

                var drivers = AsioOut.GetDriverNames();
                if (drivers.Length == 0)
                {
                    MessageBox.Show("No se detectaron drivers ASIO.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string driverName = drivers[0];
                foreach (var d in drivers)
                    if (d.ToLower().Contains("maono") || d.ToLower().Contains("ps22") || d.ToLower().Contains("focusrite"))
                        driverName = d;

                _asioOut = new AsioOut(driverName);
                _asioOut.InputChannelOffset = 1;
                _asioOut.InitRecordAndPlayback(_dspEngine.ToWaveProvider(), 1, 48000);
                _asioOut.AudioAvailable += OnAudioAvailable;
                _asioOut.Play();

                BtnStart.IsEnabled = false;
                BtnStop.IsEnabled = true;
                _tunerTimer.Start();
                AddMsg("Motor ASIO activo. Rack en vivo.", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error ASIO: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopAudio();
            BtnStart.IsEnabled = true;
            BtnStop.IsEnabled = false;
            AddMsg("Motor detenido.", false);
        }

        private void StopAudio()
        {
            _tunerTimer.Stop();
            _asioOut?.Stop();
            _asioOut?.Dispose();
            _asioOut = null;
        }

        private void OnAudioAvailable(object? sender, AsioAudioAvailableEventArgs e)
        {
            int size = e.SamplesPerBuffer * e.InputBuffers.Length;
            if (_audioBuffer == null || _audioBuffer.Length != size)
                _audioBuffer = new float[size];

            e.GetAsInterleavedSamples(_audioBuffer);

            // Alimentar afinador
            _tuner.Feed(_audioBuffer, e.SamplesPerBuffer);

            // DSP
            _dspEngine?.ProcessInput(_audioBuffer, e.SamplesPerBuffer);

            // VU meter
            double rms = 0;
            for (int i = 0; i < e.SamplesPerBuffer; i++) rms += _audioBuffer[i] * _audioBuffer[i];
            rms = Math.Sqrt(rms / e.SamplesPerBuffer);

            Dispatcher.InvokeAsync(() =>
            {
                double w = Math.Min(rms * 500, 140);
                VuFill.Width = w;
                string col = rms < 0.3 ? "#FF007F" : rms < 0.7 ? "#FFDD00" : "#FF3333";
                VuFill.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(col));
            });
        }

        // Reconstruye el EffectChain en el orden actual de la pedalera
        private void RebuildChain()
        {
            _chain = new EffectChain();
            foreach (var slot in _activeSlots)
                _chain.AddProcessor(slot.Processor);

            // Reconectar el cerebro IA (si se requiere)
            _brain = new AiCommandProcessor(_chain);

            // CRÍTICO: actualizar el chain en el DspEngine existente
            // sin crear uno nuevo — así el ASIO sigue funcionando
            _dspEngine?.UpdateChain(_chain);
        }

        // ══════════════════════════════════════════════════════════════════════
        // PERFILES — cargar, guardar, preset rápido
        // ══════════════════════════════════════════════════════════════════════

        private void LoadDefaultProfile()
        {
            var profile = InstrumentProfileService.GetDefault(_instrument);
            if (profile == null) return;

            // Agregar los efectos del perfil en orden lógico
            foreach (var def in AllPedals)
            {
                if (profile.Effects.ContainsKey(def.Name))
                {
                    var paramVals = new Dictionary<string, double>();
                    foreach (var s in profile.Effects[def.Name])
                        paramVals[s.ParameterName] = s.Value;
                    AddPedalToBoard(def.Name, true, paramVals, rebuild: false);
                }
            }

            TxtPresetName.Text = $"Default — {profile.PresetLabel}";
            AddMsg($"Perfil cargado: {profile.PresetLabel} — {profile.Description}", false);
        }

        private void BuildQuickPresets()
        {
            QuickPresets.Children.Clear();
            foreach (var presetName in InstrumentProfileService.GetPresetNames(_instrument))
            {
                var btn = new Button
                {
                    Content = presetName,
                    Height = 28,
                    Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                    BorderThickness = new Thickness(1),
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 4),
                    Cursor = Cursors.Hand,
                };
                var capturedName = presetName;
                btn.Click += (_, _) => ApplyPresetByName(capturedName);
                QuickPresets.Children.Add(btn);
            }
        }

        private void ApplyPresetByName(string presetName)
        {
            var profile = InstrumentProfileService.Get(_instrument, presetName);
            if (profile == null) return;

            // Limpiar pedalera actual
            _activeSlots.Clear();
            PedalboardPanel.Children.Clear();

            // Cargar el nuevo perfil
            foreach (var def in AllPedals)
            {
                if (profile.Effects.ContainsKey(def.Name))
                {
                    var paramVals = new Dictionary<string, double>();
                    foreach (var s in profile.Effects[def.Name])
                        paramVals[s.ParameterName] = s.Value;
                    AddPedalToBoard(def.Name, true, paramVals, rebuild: false);
                }
            }

            RebuildChain();
            TxtPresetName.Text = presetName;
            AddMsg($"Preset aplicado: \"{presetName}\" — {profile.Description}", false);
        }

        private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Guardar preset de pedalera",
                Filter = "Preset DAWY|*.dawpreset",
                InitialDirectory = PresetsDir,
                FileName = $"{_instrument} — Mi sonido",
            };
            if (dlg.ShowDialog() != true) return;

            string name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"instrument\": \"{_instrument}\",");
            sb.AppendLine($"  \"presetName\": \"{Esc(name)}\",");
            sb.AppendLine($"  \"savedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
            sb.AppendLine("  \"pedals\": [");

            for (int i = 0; i < _activeSlots.Count; i++)
            {
                var s = _activeSlots[i];
                var sep = i < _activeSlots.Count - 1 ? "," : "";
                sb.AppendLine("    {");
                sb.AppendLine($"      \"name\": \"{Esc(s.PedalName)}\",");
                sb.AppendLine($"      \"enabled\": {(s.IsEnabled ? "true" : "false")},");
                sb.AppendLine("      \"params\": {");
                var pkeys = s.ParamValues.Keys.ToList();
                for (int p = 0; p < pkeys.Count; p++)
                {
                    var psep = p < pkeys.Count - 1 ? "," : "";
                    sb.AppendLine($"        \"{Esc(pkeys[p])}\": {s.ParamValues[pkeys[p]]:F4}{psep}");
                }
                sb.AppendLine("      }");
                sb.AppendLine($"    }}{sep}");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            TxtPresetName.Text = name;
            AddMsg($"Preset guardado: \"{name}\"", false);
        }

        private void BtnLoadPreset_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Cargar preset de pedalera",
                Filter = "Preset DAWY|*.dawpreset|Todos|*.*",
                InitialDirectory = PresetsDir,
            };
            if (dlg.ShowDialog() != true) return;

            LoadPresetFile(dlg.FileName);
        }

        private void LoadPresetFile(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                _activeSlots.Clear();
                PedalboardPanel.Children.Clear();

                // Parseo simple del JSON
                int pedalStart = json.IndexOf("\"pedals\"");
                if (pedalStart < 0) return;

                // Extraer bloques de pedales
                int arrStart = json.IndexOf('[', pedalStart);
                int arrEnd = json.LastIndexOf(']');
                string inner = json.Substring(arrStart + 1, arrEnd - arrStart - 1);

                // Dividir por objetos (muy simplificado pero funcional para nuestro formato)
                var blocks = SplitJsonObjects(inner);

                foreach (var block in blocks)
                {
                    string name = ExtractStr(block, "name");
                    bool enabled = block.Contains("\"enabled\": true");
                    if (string.IsNullOrEmpty(name)) continue;

                    var paramVals = new Dictionary<string, double>();
                    int pi = block.IndexOf("\"params\"");
                    if (pi >= 0)
                    {
                        int pb = block.IndexOf('{', pi);
                        int pe = block.IndexOf('}', pb);
                        if (pb >= 0 && pe > pb)
                        {
                            string paramsStr = block.Substring(pb + 1, pe - pb - 1);
                            foreach (var line in paramsStr.Split('\n'))
                            {
                                var parts = line.Trim().TrimEnd(',').Split(':');
                                if (parts.Length == 2)
                                {
                                    string k = parts[0].Trim().Trim('"');
                                    if (double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out double v))
                                        paramVals[k] = v;
                                }
                            }
                        }
                    }

                    AddPedalToBoard(name, enabled, paramVals.Count > 0 ? paramVals : null, rebuild: false);
                }

                RebuildChain();
                string presetName = System.IO.Path.GetFileNameWithoutExtension(path);
                TxtPresetName.Text = presetName;
                AddMsg($"Preset cargado: \"{presetName}\"", false);
            }
            catch (Exception ex)
            {
                AddMsg($"Error al cargar preset: {ex.Message}", false);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // AFINADOR
        // ══════════════════════════════════════════════════════════════════════

        private Border? _selectedStringBorder;

        private void String_Click(object sender, MouseButtonEventArgs e)
        {
            if (_selectedStringBorder != null)
            {
                _selectedStringBorder.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                _selectedStringBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
            }

            if (sender is Border b)
            {
                _selectedStringBorder = b;
                b.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x08, 0x08));
                b.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x7F));
                AddMsg($"Cuerda seleccionada: {b.Tag}", false);
            }
        }

        // Afinaciones alternativas — (nombre display, notas, frecuencias en Hz)
        private static readonly Dictionary<string, (string Display, string[] Notes, float[] Freqs)> Tunings = new()
        {
            ["Standard"] = ("Estándar: E A D G B e", new[] { "E2", "A2", "D3", "G3", "B3", "E4" }, new[] { 82.41f, 110f, 146.83f, 196f, 246.94f, 329.63f }),
            ["HalfDown"] = ("½ abajo: Eb Ab Db Gb Bb eb", new[] { "Eb2", "Ab2", "Db3", "Gb3", "Bb3", "Eb4" }, new[] { 77.78f, 103.83f, 138.59f, 185f, 233.08f, 311.13f }),
            ["DropD"] = ("Drop D: D A D G B e", new[] { "D2", "A2", "D3", "G3", "B3", "E4" }, new[] { 73.42f, 110f, 146.83f, 196f, 246.94f, 329.63f }),
            ["DropC"] = ("Drop C: C G C F A d", new[] { "C2", "G2", "C3", "F3", "A3", "D4" }, new[] { 65.41f, 98f, 130.81f, 174.61f, 220f, 293.66f }),
            ["Eb"] = ("Eb: Eb Ab Db Gb Bb eb", new[] { "Eb2", "Ab2", "Db3", "Gb3", "Bb3", "Eb4" }, new[] { 77.78f, 103.83f, 138.59f, 185f, 233.08f, 311.13f }),
            ["DStandard"] = ("D Std: D G C F A d", new[] { "D2", "G2", "C3", "F3", "A3", "D4" }, new[] { 73.42f, 98f, 130.81f, 174.61f, 220f, 293.66f }),
            ["OpenG"] = ("Open G: D G D G B D", new[] { "D2", "G2", "D3", "G3", "B3", "D4" }, new[] { 73.42f, 98f, 146.83f, 196f, 246.94f, 293.66f }),
            ["OpenE"] = ("Open E: E B E G# B E", new[] { "E2", "B2", "E3", "G#3", "B3", "E4" }, new[] { 82.41f, 123.47f, 164.81f, 207.65f, 246.94f, 329.63f }),
            ["DADGAD"] = ("DADGAD: D A D G A D", new[] { "D2", "A2", "D3", "G3", "A3", "D4" }, new[] { 73.42f, 110f, 146.83f, 196f, 220f, 293.66f }),
        };

        private string _currentTuning = "Standard";

        private void Tuning_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && Tunings.TryGetValue(tag, out var tuning))
            {
                _currentTuning = tag;
                TxtTuningName.Text = tuning.Display;

                // Actualizar etiquetas de las cuerdas
                var strBorders = new[] { StrE2, StrA, StrD, StrG, StrB, StrE4 };
                for (int i = 0; i < strBorders.Length && i < tuning.Notes.Length; i++)
                {
                    strBorders[i].Tag = tuning.Notes[i];
                    if (strBorders[i].Child is TextBlock tb)
                        tb.Text = tuning.Notes[i].TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                }

                AddMsg($"Afinación: {tuning.Display}", false);
            }
        }

        private void TunerTimer_Tick(object? sender, EventArgs e)
        {
            var result = _tuner.Analyze();
            if (result == null)
            {
                TxtTunerNote.Text = "--";
                TxtTunerFreq.Text = "-- Hz";
                TxtTunerStatus.Text = "Toca una nota...";
                NeedleRotate.Angle = 0;
                return;
            }

            TxtTunerNote.Text = result.ClosestNote;
            TxtTunerFreq.Text = $"{result.DetectedFreq:F1} Hz";

            // Aguja: -50 cents = -45°, +50 cents = +45°
            double angle = Math.Clamp(result.CentsOff / 50.0 * 45.0, -45, 45);
            NeedleRotate.Angle = angle;

            if (result.IsInTune)
            {
                TxtTunerNote.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88));
                TxtTunerStatus.Text = "✓ AFINADO";
                TxtTunerStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88));
                TunerNeedle.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88));
            }
            else
            {
                string direction = result.CentsOff < 0 ? "▼ Subir" : "▲ Bajar";
                TxtTunerNote.Foreground = Brushes.White;
                TxtTunerStatus.Text = $"{direction} {Math.Abs(result.CentsOff):F0} cents";
                TxtTunerStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xDD, 0x00));
                TunerNeedle.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x7F));
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // DRAW.IA CHAT
        // ══════════════════════════════════════════════════════════════════════

        private void TxtPrompt_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            { ProcessPrompt(); e.Handled = true; }
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e) => ProcessPrompt();

        private async void ProcessPrompt()
        {
            string input = TxtPrompt.Text.Trim();
            if (string.IsNullOrEmpty(input)) return;

            AddMsg(input, true);
            TxtPrompt.Clear();
            BtnSend.IsEnabled = false;

            try
            {
                var resp = await _ai.SendPromptAsync(input);
                if (resp != null)
                {
                    AddMsg(resp.Message, false);
                    if (resp.ProfileApplied != null) TxtPresetName.Text = resp.ProfileApplied;

                    if (resp.Changes != null && resp.Changes.Count > 0)
                    {
                        bool chainChanged = false;
                        foreach (var change in resp.Changes)
                        {
                            string eName = change.EffectName?.ToUpper() ?? "";
                            var slot = _activeSlots.FirstOrDefault(s => s.PedalName.ToUpper() == eName);

                            // A) La IA decide ENCENDER un pedal
                            if (change.Enable == true)
                            {
                                if (slot == null)
                                {
                                    AddPedalToBoard(eName, true, null, false);
                                    slot = _activeSlots.LastOrDefault();
                                    chainChanged = true;
                                }
                                else if (slot.Led != null)
                                {
                                    slot.Led.IsChecked = true;
                                }
                            }
                            // B) La IA decide APAGAR o DESTRUIR un pedal
                            else if (change.Enable == false && slot != null)
                            {
                                RemovePedalFromBoard(slot);
                                slot = null;
                                chainChanged = true;
                            }

                            // C) La IA gira la perilla
                            if (slot != null && !string.IsNullOrEmpty(change.ParameterName))
                            {
                                string pName = change.ParameterName.ToUpper();
                                var exactKey = slot.KnobCanvases.Keys.FirstOrDefault(k => k.ToUpper() == pName);

                                if (exactKey != null && slot.KnobCanvases.TryGetValue(exactKey, out var knobInfo))
                                {
                                    slot.Processor.UpdateParameter(exactKey, change.Value);
                                    slot.ParamValues[exactKey] = change.Value;

                                    DrawKnob(knobInfo.Canvas, knobInfo.Accent, change.Value, knobInfo.Min, knobInfo.Max);
                                    knobInfo.SetValue(change.Value);
                                }
                            }
                        }

                        if (chainChanged) RebuildChain();
                    }
                }
                else
                {
                    AddMsg("Sin respuesta de Draw.ia.", false);
                }
            }
            catch (Exception ex) { AddMsg($"Error: {ex.Message}", false); }
            finally { BtnSend.IsEnabled = true; }
        }

        private void AddMsg(string text, bool isUser)
        {
            var border = new Border
            {
                Background = isUser
                    ? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E))
                    : Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 5),
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };

            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, MaxWidth = 200 };
            if (isUser)
            {
                tb.Text = text; tb.Foreground = Brushes.White;
            }
            else
            {
                tb.Inlines.Add(new Run("Draw.ia  ") { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF007F")), FontWeight = FontWeights.Bold, FontSize = 9 });
                tb.Inlines.Add(new LineBreak());
                tb.Inlines.Add(new Run(text) { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF")) });
            }

            border.Child = tb;
            ChatPanel.Children.Add(border);
            ChatScroller.ScrollToEnd();
        }

        // ══════════════════════════════════════════════════════════════════════
        // KNOB VISUAL
        // ══════════════════════════════════════════════════════════════════════

        private static void DrawKnob(Canvas c, Color accent, double val, double min, double max)
        {
            c.Children.Clear();
            double s = c.Width, cx = s / 2, cy = s / 2, r = s / 2 - 4;

            // 1. Guardamos la figura en una variable local segura
            var bgEllipse = new Ellipse
            {
                Width = s - 4,
                Height = s - 4,
                Fill = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
                Stroke = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                StrokeThickness = 1,
            };

            // 2. Le ponemos las coordenadas ANTES de meterla al Canvas
            Canvas.SetLeft(bgEllipse, 2);
            Canvas.SetTop(bgEllipse, 2);

            // 3. Ahora sí, la inyectamos a la pantalla
            c.Children.Add(bgEllipse);

            double t = (val - min) / (max - min);
            double sweep = 270 * t;
            if (sweep > 0.5)
            {
                double sRad = 135 * Math.PI / 180;
                double eRad = (135 + sweep) * Math.PI / 180;
                var arc = new System.Windows.Shapes.Path
                {
                    Stroke = new SolidColorBrush(accent),
                    StrokeThickness = 2.5,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    { Color = accent, BlurRadius = 5, ShadowDepth = 0 },
                };
                var geo = new PathGeometry();
                var fig = new PathFigure { StartPoint = new Point(cx + r * Math.Cos(sRad), cy + r * Math.Sin(sRad)) };
                fig.Segments.Add(new ArcSegment
                {
                    Point = new Point(cx + r * Math.Cos(eRad), cy + r * Math.Sin(eRad)),
                    Size = new Size(r, r),
                    IsLargeArc = sweep > 180,
                    SweepDirection = SweepDirection.Clockwise,
                });
                geo.Figures.Add(fig);
                arc.Data = geo;
                c.Children.Add(arc);

                c.Children.Add(new Line
                {
                    X1 = cx,
                    Y1 = cy,
                    X2 = cx + (r - 5) * Math.Cos(eRad),
                    Y2 = cy + (r - 5) * Math.Sin(eRad),
                    Stroke = Brushes.White,
                    StrokeThickness = 1.5,
                    Opacity = 0.5,
                });
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // FACTORY DE PROCESADORES
        // ══════════════════════════════════════════════════════════════════════

        private static IAudioProcessor? CreateProcessor(string name) => name switch
        {
            "GAIN" => new GainProcessor(),
            "GATE" => new NoiseGateProcessor(),
            "HARD CLIP" => new HardClipperProcessor(),
            "OVERDRIVE" => new OverdriveProcessor(),
            "FUZZ" => new FuzzProcessor(),
            "COMPRESSOR" => new CompressorProcessor(),
            "LP FILTER" => new LowPassFilter(),
            "HP FILTER" => new HighPassProcessor(),
            "BAND PASS" => new BandPassProcessor(),
            "WAH" => new WahProcessor(),
            "BITCRUSHER" => new BitcrusherProcessor(),
            "RING MOD" => new RingModulatorProcessor(),
            "PITCH SHIFT" => new PitchShifterProcessor(),
            "TREMOLO" => new TremoloProcessor(),
            "CHORUS" => new ChorusProcessor(),
            "PHASER" => new PhaserProcessor(),
            "DELAY" => new DelayProcessor(),
            "REVERB" => new SchroederReverbProcessor(),
            "PAN" => new PanProcessor(),
            _ => null,
        };

        // ── Helpers ───────────────────────────────────────────────────────────
        private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        private static string ExtractStr(string block, string key)
        {
            int ki = block.IndexOf($"\"{key}\"");
            if (ki < 0) return "";
            int ci = block.IndexOf(':', ki);
            int q1 = block.IndexOf('"', ci + 1);
            int q2 = block.IndexOf('"', q1 + 1);
            if (q1 < 0 || q2 < 0) return "";
            return block.Substring(q1 + 1, q2 - q1 - 1).Replace("\\\\", "\\").Replace("\\\"", "\"");
        }
        private static List<string> SplitJsonObjects(string json)
        {
            var result = new List<string>();
            int depth = 0, start = -1;
            for (int i = 0; i < json.Length; i++)
            {
                if (json[i] == '{') { if (depth == 0) start = i; depth++; }
                else if (json[i] == '}') { depth--; if (depth == 0 && start >= 0) { result.Add(json.Substring(start, i - start + 1)); start = -1; } }
            }
            return result;
        }
    }

    // ── Slot de pedal en la pedalera ──────────────────────────────────────────
    public class PedalSlot
    {
        public string PedalName { get; set; } = "";
        public PedalDef Def { get; set; } = null!;
        public IAudioProcessor Processor { get; set; } = null!;
        public bool IsEnabled { get; set; } = true;
        public UIElement Visual { get; set; } = null!;
        public CheckBox? Led { get; set; }
        public Dictionary<string, double> ParamValues { get; set; } = new();

        public Dictionary<string, (Canvas Canvas, double Min, double Max, Color Accent, Action<double> SetValue)>
            KnobCanvases
        { get; set; } = new();
    }

    public record PedalDef(string Name, string ColorHex, PedalParam[] Params);
    public record PedalParam(string Label, string Key, double Min, double Max, double Default);
}
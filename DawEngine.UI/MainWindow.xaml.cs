using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IO = System.IO;
using NAudio.Wave;
using DawEngine.Core;
using DawEngine.Core.Ai;

namespace DawEngine.UI
{
    // ── Representa una pista de audio en el Arrangement ──────────────────────
    public class AudioTrack
    {
        public string Name { get; set; } = "";
        public string FilePath { get; set; } = "";
        public Color TrackColor { get; set; }
        public float[]? Samples { get; set; }
        public int SampleRate { get; set; } = 44100;
    }

    public partial class MainWindow : Window
    {
        // ── Hardware & Motor ──────────────────────────────────────────────────
        private AsioOut? _asioOut;
        private DspEngine? _dspEngine;
        private EffectChain? _chain;
        private float[]? _audioBuffer;
        private float _masterVolume = 0.8f;

        // ── Procesadores del Rack ─────────────────────────────────────────────
        private GainProcessor? _gain;
        private NoiseGateProcessor? _gate;
        private HardClipperProcessor? _clipper;
        private OverdriveProcessor? _overdrive;
        private FuzzProcessor? _fuzz;
        private CompressorProcessor? _compressor;
        private LowPassFilter? _lowPass;
        private HighPassProcessor? _highPass;
        private BandPassProcessor? _bandPass;
        private WahProcessor? _wah;
        private BitcrusherProcessor? _bitcrusher;
        private RingModulatorProcessor? _ringMod;
        private PitchShifterProcessor? _pitchShifter;
        private TremoloProcessor? _tremolo;
        private ChorusProcessor? _chorus;
        private PhaserProcessor? _phaser;
        private DelayProcessor? _delay;
        private SchroederReverbProcessor? _reverb;
        private PanProcessor? _pan;

        // ── IA ────────────────────────────────────────────────────────────────
        private LlmService _aiService;
        private AiCommandProcessor? _aiBrain;

        // ── Tracks del Arrangement ────────────────────────────────────────────
        private readonly List<AudioTrack> _tracks = new();

        // 🛡️ DICCIONARIOS ROBUSTOS (Ignoran mayúsculas y enlazan al audio real)
        private readonly Dictionary<string, (CheckBox Led, string ExactPedalName)> _ledVisuals = new();
        private readonly Dictionary<string, (Canvas Canvas, Color Accent, double Min, double Max, Action<double> UpdateVal, string ExactPedalName, string ExactParamName)> _knobVisuals = new();

        private static readonly Color[] TrackPalette =
        {
            (Color)ColorConverter.ConvertFromString("#00B4D8"), // Cyan
            (Color)ColorConverter.ConvertFromString("#FF006E"), // Rosa
            (Color)ColorConverter.ConvertFromString("#8338EC"), // Violeta
            (Color)ColorConverter.ConvertFromString("#06D6A0"), // Verde
            (Color)ColorConverter.ConvertFromString("#FFB703"), // Ámbar
            (Color)ColorConverter.ConvertFromString("#FB5607"), // Naranja
            (Color)ColorConverter.ConvertFromString("#3A86FF"), // Azul
        };

        // ── Definición del rack (nombre, color hex, parámetros) ───────────────
        private record PedalParam(string Label, string Key, double Min, double Max, double Default);
        private record PedalDef(string Name, string ColorHex, PedalParam[] Params);

        private readonly PedalDef[] _pedalDefs = {
            new("GAIN",        "#FFD700", new[]{ new PedalParam("LEVEL","Gain",0,3,1) }),
            new("GATE",        "#88FF44", new[]{ new PedalParam("THRESH","Threshold",0.001,0.2,0.01) }),
            new("HARD CLIP",   "#FF007F", new[]{ new PedalParam("DRIVE","Drive",1,20,5), new PedalParam("THRESH","Threshold",0.1,1,0.5) }),
            new("OVERDRIVE",   "#FF8800", new[]{ new PedalParam("GAIN","Gain",1,10,2) }),
            new("FUZZ",        "#FF4400", new[]{ new PedalParam("FUZZ","Gain",1,50,20) }),
            new("COMPRESSOR",  "#AAFFAA", new[]{ new PedalParam("THRESH","Threshold",0.05,1,0.5), new PedalParam("RATIO","Ratio",1,20,4) }),
            new("LP FILTER",   "#00E5FF", new[]{ new PedalParam("CUTOFF","Cutoff",200,20000,4000) }),
            new("HP FILTER",   "#00AAFF", new[]{ new PedalParam("CUTOFF","Cutoff",20,2000,100) }),
            new("BAND PASS",   "#0088FF", new[]{ new PedalParam("CENTER","Center",200,8000,1000) }),
            new("AUTO-WAH",    "#FFAA00", new[]{ new PedalParam("RATE","Rate",0.1,8,2), new PedalParam("Q","Q",0.05,0.8,0.2) }),
            new("BITCRUSHER",  "#B000FF", new[]{ new PedalParam("BITS","BitDepth",2,16,16), new PedalParam("RATE÷","Downsample",1,20,1) }),
            new("RING MOD",    "#FF00AA", new[]{ new PedalParam("FREQ","Frequency",50,2000,400) }),
            new("PITCH SHIFT", "#FF44FF", new[]{ new PedalParam("RATIO","Alpha",0.5,2,1) }),
            new("TREMOLO",     "#44FFDD", new[]{ new PedalParam("RATE","Rate",0.5,20,5), new PedalParam("DEPTH","Depth",0,1,0.8) }),
            new("CHORUS",      "#44DDFF", new[]{ new PedalParam("RATE","Rate",0.1,5,1.5), new PedalParam("DEPTH","Depth",0,15,5) }),
            new("PHASER",      "#AA44FF", new[]{ new PedalParam("RATE","Rate",0.1,5,1), new PedalParam("FDBK","Feedback",0,0.9,0.5) }),
            new("DELAY",       "#FFDD00", new[]{ new PedalParam("TIME","Time",50,1000,350), new PedalParam("FDBK","Feedback",0,0.95,0.4), new PedalParam("MIX","Mix",0,1,0.5) }),
            new("REVERB",      "#88AAFF", new[]{ new PedalParam("ROOM","RoomSize",0.1,0.98,0.8), new PedalParam("MIX","Mix",0,1,0.4) }),
            new("PAN",         "#FFFFFF", new[]{ new PedalParam("L◄►R","Pan",0,1,0.5) }),
        };

        // ─────────────────────────────────────────────────────────────────────
        public MainWindow(string template = "Empty", string? sessionPath = null)
        {
            InitializeComponent();
            _aiService = new LlmService();
            _activeTemplate = template;
            _sessionPath = sessionPath;
            BuildRack();
            DrawRuler();
            Loaded += (_, _) => ApplyStartup();
        }

        private readonly string _activeTemplate;
        private readonly string? _sessionPath;

        // ══════════════════════════════════════════════════════════════════════
        // RACK: construcción dinámica de pedales con knob visual
        // ══════════════════════════════════════════════════════════════════════

        private void BuildRack()
        {
            RackPanel.Children.Clear();
            _knobVisuals.Clear();
            _ledVisuals.Clear();

            foreach (var def in _pedalDefs)
            {
                var pedalColor = (Color)ColorConverter.ConvertFromString(def.ColorHex);
                var pedalBrush = new SolidColorBrush(pedalColor);

                var pedalBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
                    CornerRadius = new CornerRadius(8),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(60, pedalColor.R, pedalColor.G, pedalColor.B)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 8, 0),
                    Padding = new Thickness(10, 8, 10, 8),
                    Width = 90 + def.Params.Length * 40,
                };

                var outerStack = new StackPanel();

                // Fila superior: LED + Nombre + Power btn
                var headerRow = new Grid();
                headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var led = new CheckBox { Style = (Style)FindResource("LedToggle"), IsChecked = false, Margin = new Thickness(0, 0, 5, 0) };

                // GUARDAMOS EL LED CON SU LLAVE EN MAYÚSCULAS
                _ledVisuals[def.Name.ToUpper()] = (led, def.Name);
                Grid.SetColumn(led, 0);

                var nameLabel = new TextBlock
                {
                    Text = def.Name,
                    Foreground = pedalBrush,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(nameLabel, 1);

                var pwrBtn = new Button { Style = (Style)FindResource("PowerBtn"), Content = "⏻" };
                Grid.SetColumn(pwrBtn, 2);

                var capturedLed = led;
                pwrBtn.Click += (_, _) => capturedLed.IsChecked = !capturedLed.IsChecked;

                headerRow.Children.Add(led);
                headerRow.Children.Add(nameLabel);
                headerRow.Children.Add(pwrBtn);
                outerStack.Children.Add(headerRow);

                outerStack.Children.Add(new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromArgb(40, pedalColor.R, pedalColor.G, pedalColor.B)),
                    Margin = new Thickness(0, 6, 0, 8),
                });

                // Knobs row
                var knobRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

                foreach (var param in def.Params)
                {
                    var knobStack = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };

                    var knobCanvas = new Canvas { Width = 44, Height = 44, Cursor = Cursors.SizeNS };
                    DrawKnob(knobCanvas, pedalColor, param.Default, param.Min, param.Max);

                    // Estado del drag
                    double currentValue = param.Default;
                    double dragStartY = 0;
                    double dragStartVal = param.Default;
                    bool isDragging = false;

                    // ¡EL CORAZÓN DE LA SOLUCIÓN! Guardamos la perilla con un Action para actualizar el currentValue
                    string dictKey = $"{def.Name.ToUpper()}_{param.Key.ToUpper()}";
                    _knobVisuals[dictKey] = (knobCanvas, pedalColor, param.Min, param.Max, (val) => currentValue = val, def.Name, param.Key);

                    var capturedCanvas = knobCanvas;
                    var capturedParam = param;
                    var capturedColor = pedalColor;
                    var capturedDef = def;

                    knobCanvas.MouseLeftButtonDown += (s, e) =>
                    {
                        isDragging = true;
                        dragStartY = e.GetPosition(null).Y;
                        dragStartVal = currentValue;
                        capturedCanvas.CaptureMouse();
                        e.Handled = true;
                    };

                    knobCanvas.MouseMove += (s, e) =>
                    {
                        if (!isDragging) return;
                        double dy = dragStartY - e.GetPosition(null).Y;
                        double range = capturedParam.Max - capturedParam.Min;
                        double newVal = Math.Clamp(dragStartVal + dy / 100.0 * range,
                                                    capturedParam.Min, capturedParam.Max);
                        currentValue = newVal;
                        DrawKnob(capturedCanvas, capturedColor, newVal, capturedParam.Min, capturedParam.Max);
                        UpdateProcessor(capturedDef.Name, capturedParam.Key, (float)newVal);
                        e.Handled = true;
                    };

                    knobCanvas.MouseLeftButtonUp += (s, e) =>
                    {
                        isDragging = false;
                        capturedCanvas.ReleaseMouseCapture();
                        e.Handled = true;
                    };

                    knobCanvas.MouseWheel += (s, e) =>
                    {
                        double range = capturedParam.Max - capturedParam.Min;
                        double step = range / 100.0;
                        double newVal = Math.Clamp(currentValue + (e.Delta > 0 ? step : -step),
                                                   capturedParam.Min, capturedParam.Max);
                        currentValue = newVal;
                        DrawKnob(capturedCanvas, capturedColor, newVal, capturedParam.Min, capturedParam.Max);
                        UpdateProcessor(capturedDef.Name, capturedParam.Key, (float)newVal);
                        e.Handled = true;
                    };

                    var capturedDef2 = def;
                    led.Checked += (_, _) => SetProcessorEnabled(capturedDef2.Name, true);
                    led.Unchecked += (_, _) => SetProcessorEnabled(capturedDef2.Name, false);

                    knobStack.Children.Add(knobCanvas);

                    var paramLabel = new TextBlock
                    {
                        Text = param.Label,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                        FontSize = 8,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 4, 0, 0),
                    };
                    knobStack.Children.Add(paramLabel);
                    knobRow.Children.Add(knobStack);
                }

                outerStack.Children.Add(knobRow);
                pedalBorder.Child = outerStack;
                RackPanel.Children.Add(pedalBorder);
            }
        }

        // Dibuja un knob circular con arco de progreso
        private static void DrawKnob(Canvas canvas, Color accent, double value, double min, double max)
        {
            canvas.Children.Clear();
            double size = canvas.Width;
            double cx = size / 2;
            double cy = size / 2;
            double radius = size / 2 - 4;

            var bg = new Ellipse
            {
                Width = size - 4,
                Height = size - 4,
                Fill = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                Stroke = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                StrokeThickness = 1,
            };
            Canvas.SetLeft(bg, 2); Canvas.SetTop(bg, 2);
            canvas.Children.Add(bg);

            double t = (value - min) / (max - min); // 0..1
            double startDeg = 135;
            double sweepDeg = 270 * t;
            double startRad = startDeg * Math.PI / 180;
            double endRad = (startDeg + sweepDeg) * Math.PI / 180;

            if (sweepDeg > 0.5)
            {
                var arc = new Path
                {
                    Stroke = new SolidColorBrush(accent),
                    StrokeThickness = 3,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = accent,
                        BlurRadius = 6,
                        ShadowDepth = 0,
                    },
                };
                double x1 = cx + radius * Math.Cos(startRad);
                double y1 = cy + radius * Math.Sin(startRad);
                double x2 = cx + radius * Math.Cos(endRad);
                double y2 = cy + radius * Math.Sin(endRad);
                bool largeArc = sweepDeg > 180;

                var geo = new PathGeometry();
                var fig = new PathFigure { StartPoint = new Point(x1, y1) };
                fig.Segments.Add(new ArcSegment
                {
                    Point = new Point(x2, y2),
                    Size = new Size(radius, radius),
                    IsLargeArc = largeArc,
                    SweepDirection = SweepDirection.Clockwise,
                });
                geo.Figures.Add(fig);
                arc.Data = geo;
                canvas.Children.Add(arc);
            }

            double indRad = endRad;
            double ix = cx + (radius - 6) * Math.Cos(indRad);
            double iy = cy + (radius - 6) * Math.Sin(indRad);
            var indicator = new Line
            {
                X1 = cx,
                Y1 = cy,
                X2 = ix,
                Y2 = iy,
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 1.5,
                Opacity = 0.6,
            };
            canvas.Children.Add(indicator);
        }

        // ══════════════════════════════════════════════════════════════════════
        // ARRANQUE SEGÚN PLANTILLA
        // ══════════════════════════════════════════════════════════════════════

        private void ApplyStartup()
        {
            if (_sessionPath != null && _activeTemplate == "Open")
            {
                LoadSession(_sessionPath);
                return;
            }

            switch (_activeTemplate)
            {
                case "Guitar":
                    AddChatMessage("Plantilla Guitar Session cargada. Gain, Clipper, Delay y Reverb habilitados.", false);
                    EnableEffect("GAIN", true);
                    EnableEffect("GATE", true);
                    EnableEffect("HARD CLIP", true);
                    EnableEffect("DELAY", true);
                    EnableEffect("REVERB", true);
                    break;
                case "Beat":
                    AddChatMessage("Plantilla Beat Production cargada. Compressor, Bitcrusher y filtros habilitados.", false);
                    EnableEffect("COMPRESSOR", true);
                    EnableEffect("LP FILTER", true);
                    EnableEffect("BITCRUSHER", true);
                    break;
                case "Podcast":
                    AddChatMessage("Plantilla Podcast / Voz cargada. Gate, Comp y HP Filter habilitados.", false);
                    EnableEffect("GAIN", true);
                    EnableEffect("GATE", true);
                    EnableEffect("COMPRESSOR", true);
                    EnableEffect("HP FILTER", true);
                    break;
                default:
                    AddChatMessage("Proyecto vacío listo. Agrega pistas con el botón + PISTA.", false);
                    break;
            }
        }

        // Encendido maestro unificado
        private void EnableEffect(string name, bool on)
        {
            if (string.IsNullOrEmpty(name)) return;

            // Ignoramos mayúsculas
            if (_ledVisuals.TryGetValue(name.ToUpper(), out var data))
            {
                data.Led.IsChecked = on; // Actualiza interfaz visual
                SetProcessorEnabled(data.ExactPedalName, on); // Actualiza audio
            }
        }

        public void LoadAudioFile(string path)
        {
            try
            {
                using var reader = new AudioFileReader(path);
                int total = (int)(reader.Length / (reader.WaveFormat.BitsPerSample / 8));
                var samples = new float[total];
                reader.Read(samples, 0, total);

                var track = new AudioTrack
                {
                    Name = IO.Path.GetFileNameWithoutExtension(path),
                    FilePath = path,
                    Samples = samples,
                    SampleRate = reader.WaveFormat.SampleRate,
                    TrackColor = TrackPalette[_tracks.Count % TrackPalette.Length],
                };
                _tracks.Add(track);
                AddTrackRow(track, _tracks.Count - 1);
            }
            catch (Exception ex)
            {
                AddChatMessage($"Error al cargar '{IO.Path.GetFileName(path)}': {ex.Message}", false);
            }
        }

        private void AddTrackRow(AudioTrack track, int index, bool isLive = false)
        {
            var trackColor = track.TrackColor;
            var trackBrush = new SolidColorBrush(trackColor);

            var row = new Grid { Height = 72, Margin = new Thickness(0, 0, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderThickness = new Thickness(0, 0, 1, 1),
            };
            var leftStack = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };

            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            nameRow.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = trackBrush, Margin = new Thickness(0, 0, 6, 0) });
            nameRow.Children.Add(new TextBlock
            {
                Text = track.Name,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            });
            leftStack.Children.Add(nameRow);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            btnRow.Children.Add(MakeTrackBtn("M", "#555"));
            btnRow.Children.Add(MakeTrackBtn("S", "#555"));
            leftStack.Children.Add(btnRow);

            leftPanel.Child = leftStack;
            Grid.SetColumn(leftPanel, 0);
            row.Children.Add(leftPanel);

            var wavePanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(30, trackColor.R, trackColor.G, trackColor.B)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                ClipToBounds = true,
            };

            var waveCanvas = new Canvas { Height = 72 };
            wavePanel.Child = waveCanvas;

            var waveLabel = new TextBlock
            {
                Text = track.Name + "_T" + (index + 1),
                Foreground = new SolidColorBrush(Color.FromArgb(120, trackColor.R, trackColor.G, trackColor.B)),
                FontSize = 9,
                FontFamily = new FontFamily("Courier New"),
                Margin = new Thickness(8, 4, 0, 0),
            };
            waveCanvas.Children.Add(waveLabel);

            if (isLive)
            {
                var liveBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(180, 255, 0, 127)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 2, 5, 2),
                    Margin = new Thickness(8, 4, 0, 0),
                };
                liveBadge.Child = new TextBlock
                {
                    Text = "● LIVE",
                    Foreground = Brushes.White,
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily("Courier New"),
                };
                waveCanvas.Children.Add(liveBadge);
                Canvas.SetLeft(liveBadge, 8);
                Canvas.SetTop(liveBadge, 48);
            }
            {
                DrawWaveform(waveCanvas, track, trackColor);
            }
            ;
            waveCanvas.SizeChanged += (_, _) =>
            {
                DrawWaveform(waveCanvas, track, trackColor);
            };

            wavePanel.AllowDrop = true;
            wavePanel.Drop += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                    if (files.Length > 0)
                    {
                        LoadAudioIntoTrack(track, files[0], waveCanvas, trackColor);
                    }
                }
            };

            Grid.SetColumn(wavePanel, 1);
            row.Children.Add(wavePanel);

            _waveCanvases[track] = waveCanvas;

            TrackListPanel.Children.Add(row);
        }

        private void LoadAudioIntoTrack(AudioTrack track, string path, Canvas canvas, Color color)
        {
            try
            {
                using var reader = new AudioFileReader(path);
                int channels = reader.WaveFormat.Channels;
                int totalSamples = (int)(reader.Length / (reader.WaveFormat.BitsPerSample / 8));
                var buffer = new float[totalSamples];
                reader.Read(buffer, 0, totalSamples);

                if (channels == 2)
                {
                    var mono = new float[totalSamples / 2];
                    for (int i = 0; i < mono.Length; i++)
                        mono[i] = (buffer[i * 2] + buffer[i * 2 + 1]) / 2f;
                    track.Samples = mono;
                }
                else
                {
                    track.Samples = buffer;
                }

                track.FilePath = path;
                track.SampleRate = reader.WaveFormat.SampleRate;
                track.Name = IO.Path.GetFileNameWithoutExtension(path);

                Dispatcher.Invoke(() => DrawWaveform(canvas, track, color));
                AddChatMessage($"Audio cargado: {track.Name} ({track.Samples.Length / track.SampleRate:F1}s)", false);
            }
            catch (Exception ex)
            {
                AddChatMessage($"Error: {ex.Message}", false);
            }
        }

        private static void DrawWaveform(Canvas canvas, AudioTrack track, Color color)
        {
            for (int i = canvas.Children.Count - 1; i >= 0; i--)
                if (canvas.Children[i] is Polyline || canvas.Children[i] is Rectangle r && r.Tag?.ToString() == "wave")
                    canvas.Children.RemoveAt(i);

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            double midY = h / 2;

            if (track.Samples == null || track.Samples.Length == 0)
            {
                DrawSimulatedWaveform(canvas, color, w, h, midY, track.Name.GetHashCode());
                return;
            }

            int samplesPerPixel = Math.Max(1, track.Samples.Length / (int)w);
            var waveColor = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B));

            for (int px = 0; px < (int)w; px++)
            {
                int start = px * samplesPerPixel;
                int end = Math.Min(start + samplesPerPixel, track.Samples.Length);
                float max = 0, min = 0;
                for (int s = start; s < end; s++)
                {
                    if (track.Samples[s] > max) max = track.Samples[s];
                    if (track.Samples[s] < min) min = track.Samples[s];
                }
                double top = midY - (max * (midY - 6));
                double bottom = midY - (min * (midY - 6));
                double barH = Math.Max(1, bottom - top);

                var bar = new Rectangle
                {
                    Width = 1,
                    Height = barH,
                    Fill = waveColor,
                    Tag = "wave",
                };
                Canvas.SetLeft(bar, px);
                Canvas.SetTop(bar, top);
                canvas.Children.Add(bar);
            }
        }

        private static void DrawSimulatedWaveform(Canvas canvas, Color color, double w, double h, double midY, int seed)
        {
            var rng = new Random(seed);
            var brush = new SolidColorBrush(Color.FromArgb(180, color.R, color.G, color.B));

            double amplitude = 0.7;
            for (int px = 0; px < (int)w; px += 2)
            {
                double env = Math.Sin(px / w * Math.PI);
                double v = (rng.NextDouble() * 2 - 1) * amplitude * env;
                double barH = Math.Max(2, Math.Abs(v) * (midY - 6) * 2);
                double top = midY - barH / 2;

                var bar = new Rectangle
                {
                    Width = 1.5,
                    Height = barH,
                    Fill = brush,
                    Tag = "wave",
                };
                Canvas.SetLeft(bar, px);
                Canvas.SetTop(bar, top);
                canvas.Children.Add(bar);
            }
        }

        private void DrawRuler()
        {
            RulerCanvas.Loaded += (_, _) => RenderRuler();
            RulerCanvas.SizeChanged += (_, _) => RenderRuler();
        }

        private void RenderRuler()
        {
            RulerCanvas.Children.Clear();
            double w = RulerCanvas.ActualWidth;
            if (w <= 0) return;
            int bars = 20;
            double barW = w / bars;
            for (int i = 1; i <= bars; i++)
            {
                double x = i * barW;
                var line = new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = 14,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                    StrokeThickness = 1,
                };
                RulerCanvas.Children.Add(line);
                var label = new TextBlock
                {
                    Text = i.ToString(),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                    FontSize = 9,
                    FontFamily = new FontFamily("Courier New"),
                };
                Canvas.SetLeft(label, x + 3);
                Canvas.SetTop(label, 2);
                RulerCanvas.Children.Add(label);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // TRANSPORTE
        // ══════════════════════════════════════════════════════════════════════

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _chain = new EffectChain();

                _gain = new GainProcessor();
                _gate = new NoiseGateProcessor();
                _clipper = new HardClipperProcessor();
                _overdrive = new OverdriveProcessor();
                _fuzz = new FuzzProcessor();
                _compressor = new CompressorProcessor();
                _lowPass = new LowPassFilter();
                _highPass = new HighPassProcessor();
                _bandPass = new BandPassProcessor();
                _wah = new WahProcessor();
                _bitcrusher = new BitcrusherProcessor();
                _ringMod = new RingModulatorProcessor();
                _pitchShifter = new PitchShifterProcessor();
                _tremolo = new TremoloProcessor();
                _chorus = new ChorusProcessor();
                _phaser = new PhaserProcessor();
                _delay = new DelayProcessor();
                _reverb = new SchroederReverbProcessor();
                _pan = new PanProcessor();

                _chain.AddProcessor(_gain);
                _chain.AddProcessor(_gate);
                _chain.AddProcessor(_clipper);
                _chain.AddProcessor(_overdrive);
                _chain.AddProcessor(_fuzz);
                _chain.AddProcessor(_compressor);
                _chain.AddProcessor(_lowPass);
                _chain.AddProcessor(_highPass);
                _chain.AddProcessor(_bandPass);
                _chain.AddProcessor(_wah);
                _chain.AddProcessor(_bitcrusher);
                _chain.AddProcessor(_ringMod);
                _chain.AddProcessor(_pitchShifter);
                _chain.AddProcessor(_tremolo);
                _chain.AddProcessor(_chorus);
                _chain.AddProcessor(_phaser);
                _chain.AddProcessor(_delay);
                _chain.AddProcessor(_reverb);
                _chain.AddProcessor(_pan);

                _aiBrain = new AiCommandProcessor(_chain);
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

                BtnPlay.IsEnabled = false;
                BtnStop.IsEnabled = true;
                AddChatMessage("Motor DSP activo. Rack de 19 efectos inicializado.", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al inicializar ASIO: {ex.Message}", "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _asioOut?.Stop();
            _asioOut?.Dispose();
            _asioOut = null;
            BtnPlay.IsEnabled = true;
            BtnStop.IsEnabled = false;
            AddChatMessage("Audio detenido.", false);
        }

        private void BtnRewind_Click(object sender, RoutedEventArgs e) { }
        private void BtnFwd_Click(object sender, RoutedEventArgs e) { }
        private void BtnRec_Click(object sender, RoutedEventArgs e) { }

        private void UpdateProcessor(string pedalName, string paramKey, float value)
        {
            IAudioProcessor? proc = pedalName switch
            {
                "GAIN" => _gain,
                "GATE" => _gate,
                "HARD CLIP" => _clipper,
                "OVERDRIVE" => _overdrive,
                "FUZZ" => _fuzz,
                "COMPRESSOR" => _compressor,
                "LP FILTER" => _lowPass,
                "HP FILTER" => _highPass,
                "BAND PASS" => _bandPass,
                "AUTO-WAH" => _wah,
                "BITCRUSHER" => _bitcrusher,
                "RING MOD" => _ringMod,
                "PITCH SHIFT" => _pitchShifter,
                "TREMOLO" => _tremolo,
                "CHORUS" => _chorus,
                "PHASER" => _phaser,
                "DELAY" => _delay,
                "REVERB" => _reverb,
                "PAN" => _pan,
                _ => null,
            };
            proc?.UpdateParameter(paramKey, value);
        }

        private void SetProcessorEnabled(string pedalName, bool enabled)
        {
            IAudioProcessor? proc = pedalName switch
            {
                "GAIN" => _gain,
                "GATE" => _gate,
                "HARD CLIP" => _clipper,
                "OVERDRIVE" => _overdrive,
                "FUZZ" => _fuzz,
                "COMPRESSOR" => _compressor,
                "LP FILTER" => _lowPass,
                "HP FILTER" => _highPass,
                "BAND PASS" => _bandPass,
                "AUTO-WAH" => _wah,
                "BITCRUSHER" => _bitcrusher,
                "RING MOD" => _ringMod,
                "PITCH SHIFT" => _pitchShifter,
                "TREMOLO" => _tremolo,
                "CHORUS" => _chorus,
                "PHASER" => _phaser,
                "DELAY" => _delay,
                "REVERB" => _reverb,
                "PAN" => _pan,
                _ => null,
            };
            if (proc != null) proc.IsEnabled = enabled;
        }

        private void OnAudioAvailable(object? sender, AsioAudioAvailableEventArgs e)
        {
            int size = e.SamplesPerBuffer * e.InputBuffers.Length;
            if (_audioBuffer == null || _audioBuffer.Length != size)
                _audioBuffer = new float[size];

            e.GetAsInterleavedSamples(_audioBuffer);
            _dspEngine?.ProcessInput(_audioBuffer, e.SamplesPerBuffer);

            double sumL = 0, sumR = 0;
            for (int i = 0; i < e.SamplesPerBuffer; i++)
            {
                float s = _audioBuffer[i] * _masterVolume;
                sumL += s * s;
                sumR += s * s;
            }
            double rmsL = Math.Sqrt(sumL / e.SamplesPerBuffer);
            double rmsR = Math.Sqrt(sumR / e.SamplesPerBuffer);

            Dispatcher.InvokeAsync(() =>
            {
                double vuW = Math.Min(rmsL * 500, 120);
                VuFill.Width = vuW;
                string vuColor = rmsL < 0.3 ? "#00E5FF" : rmsL < 0.7 ? "#FFDD00" : "#FF3333";
                VuFill.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(vuColor));

                double maxH = 120;
                double hL = Math.Min(rmsL * 600, maxH);
                double hR = Math.Min(rmsR * 600, maxH);
                VuL.Height = hL;
                VuR.Height = hR;

                string mColor = rmsL < 0.3 ? "#00E5FF" : rmsL < 0.7 ? "#FFDD00" : "#FF3333";
                var mBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(mColor));
                VuL.Background = mBrush;
                VuR.Background = mBrush;

                double db = rmsL > 0.00001 ? 20 * Math.Log10(rmsL) : double.NegativeInfinity;
                TxtDb.Text = double.IsNegativeInfinity(db) ? "-∞ dB" : $"{db:F1} dB";
            });
        }

        private void SldMasterVol_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
            => _masterVolume = (float)e.NewValue;

        // ══════════════════════════════════════════════════════════════════════
        // CHAT IA: LA MAGIA ROBUSTA OCURRE AQUÍ
        // ══════════════════════════════════════════════════════════════════════

        private void TxtPrompt_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                ProcessUserPrompt();
                e.Handled = true;
            }
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e) => ProcessUserPrompt();

        private async void ProcessUserPrompt()
        {
            string input = TxtPrompt.Text.Trim();
            if (string.IsNullOrEmpty(input)) return;

            if (_chain == null)
            {
                AddChatMessage("Inicia el motor de audio primero (botón ▶).", false);
                return;
            }

            AddChatMessage(input, true);
            TxtPrompt.Clear();
            BtnSend.IsEnabled = false;

            try
            {
                var response = await _aiService.SendPromptAsync(input);
                if (response != null)
                {
                    AddChatMessage(response.Message, false);

                    if (response.Changes != null && response.Changes.Count > 0)
                    {
                        foreach (var change in response.Changes)
                        {
                            // Convertimos lo que mande la IA a mayúsculas para evitar errores
                            string eName = change.EffectName?.ToUpper() ?? "";

                            // A) Activar/Desactivar
                            if (change.Enable.HasValue)
                            {
                                EnableEffect(eName, change.Enable.Value);
                            }

                            // B) Mover perilla
                            if (!string.IsNullOrEmpty(change.ParameterName))
                            {
                                string pName = change.ParameterName.ToUpper();
                                string dictKey = $"{eName}_{pName}";

                                // Buscamos en el diccionario (que ya tiene las llaves en mayúscula)
                                if (_knobVisuals.TryGetValue(dictKey, out var vis))
                                {
                                    // 1. Actualizar audio con los nombres exactos originales del sistema (¡Blindado!)
                                    UpdateProcessor(vis.ExactPedalName, vis.ExactParamName, change.Value);

                                    // 2. Redibujar el control visual en el UI
                                    DrawKnob(vis.Canvas, vis.Accent, change.Value, vis.Min, vis.Max);

                                    // 3. ¡Evitar el efecto liga! Actualiza la variable drag local
                                    vis.UpdateVal(change.Value);
                                }
                            }
                        }
                    }
                }
                else
                {
                    AddChatMessage("No pude procesar la solicitud.", false);
                }
            }
            catch (Exception ex)
            {
                AddChatMessage($"Error: {ex.Message}", false);
            }
            finally
            {
                BtnSend.IsEnabled = true;
            }
        }

        private void AddChatMessage(string text, bool isUser)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(isUser
                    ? Color.FromRgb(0x1E, 0x1E, 0x1E)
                    : Color.FromArgb(0, 0, 0, 0)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };

            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                MaxWidth = 210,
            };

            if (isUser)
            {
                tb.Text = text;
                tb.Foreground = Brushes.White;
            }
            else
            {
                var prefix = new Run("Draw.ia  ") { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF007F")), FontWeight = FontWeights.Bold, FontSize = 9 };
                var body = new Run(text) { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF")) };
                tb.Inlines.Add(prefix);
                tb.Inlines.Add(new LineBreak());
                tb.Inlines.Add(body);
            }

            border.Child = tb;
            ChatPanel.Children.Add(border);
            ChatScroller.ScrollToEnd();
        }

        private static Button MakeTrackBtn(string label, string colorHex)
        {
            return new Button
            {
                Content = label,
                Width = 22,
                Height = 16,
                Margin = new Thickness(0, 0, 4, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#222")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333")),
                FontSize = 9,
                Cursor = Cursors.Hand,
            };
        }

        private void BtnAddTrack_Click(object sender, RoutedEventArgs e)
        {
            var modal = new AddTrackModal();
            modal.Owner = this;
            if (modal.ShowDialog() != true) return;

            switch (modal.TrackType)
            {
                case "Audio": PickAudioFiles(); break;
                case "Live": AddLiveTrack(); break;
                case "MIDI": AddPlaceholderTrack("MIDI Track", "#CC88FF"); break;
                case "Bus": AddPlaceholderTrack("Bus", "#FFD700"); break;
            }
        }

        private void PickAudioFiles()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Seleccionar archivo de audio",
                Filter = "Audio|*.wav;*.mp3;*.aiff;*.flac|Todos|*.*",
                Multiselect = true,
            };
            if (dlg.ShowDialog() != true) return;

            foreach (var file in dlg.FileNames)
            {
                var track = new AudioTrack
                {
                    Name = IO.Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    TrackColor = TrackPalette[_tracks.Count % TrackPalette.Length],
                };
                Task.Run(() =>
                {
                    try
                    {
                        using var reader = new AudioFileReader(file);
                        int channels = reader.WaveFormat.Channels;
                        int total = (int)(reader.Length / (reader.WaveFormat.BitsPerSample / 8));
                        var buffer = new float[Math.Min(total, reader.WaveFormat.SampleRate * 300)];
                        int read = reader.Read(buffer, 0, buffer.Length);
                        float[] mono;
                        if (channels == 2)
                        {
                            mono = new float[read / 2];
                            for (int i = 0; i < mono.Length; i++)
                                mono[i] = (buffer[i * 2] + buffer[i * 2 + 1]) / 2f;
                        }
                        else mono = buffer[..read];
                        track.Samples = mono;
                        track.SampleRate = reader.WaveFormat.SampleRate;
                        Dispatcher.Invoke(() => RefreshTrackWaveform(track));
                    }
                    catch { }
                });
                _tracks.Add(track);
                AddTrackRow(track, _tracks.Count - 1);
                AddChatMessage($"Pista de audio: {track.Name}", false);
            }
        }

        private void AddLiveTrack()
        {
            var track = new AudioTrack
            {
                Name = $"Instrumento {_tracks.Count + 1}",
                TrackColor = TrackPalette[_tracks.Count % TrackPalette.Length],
            };
            _tracks.Add(track);
            AddTrackRow(track, _tracks.Count - 1, isLive: true);
            AddChatMessage($"Pista en vivo agregada: {track.Name}. Inicia el motor (▶) para grabar.", false);
        }

        private void AddPlaceholderTrack(string name, string colorHex)
        {
            var track = new AudioTrack
            {
                Name = $"{name} {_tracks.Count + 1}",
                TrackColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex),
            };
            _tracks.Add(track);
            AddTrackRow(track, _tracks.Count - 1);
            AddChatMessage($"Pista '{track.Name}' agregada.", false);
        }

        private readonly Dictionary<AudioTrack, Canvas> _waveCanvases = new();

        private void RefreshTrackWaveform(AudioTrack track)
        {
            if (_waveCanvases.TryGetValue(track, out var canvas))
                DrawWaveform(canvas, track, track.TrackColor);
        }

        private void BtnSaveSession_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Guardar sesión",
                Filter = "Sesión DAW|*.dawsession|JSON|*.json",
                FileName = "MiSesion",
                DefaultExt = ".dawsession",
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"version\": \"1.0\",");
                sb.AppendLine($"  \"savedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
                sb.AppendLine($"  \"projectName\": \"Plastic Memory DAW\",");
                sb.AppendLine("  \"tracks\": [");

                for (int i = 0; i < _tracks.Count; i++)
                {
                    var t = _tracks[i];
                    var sep = i < _tracks.Count - 1 ? "," : "";
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"name\": \"{EscapeJson(t.Name)}\",");
                    sb.AppendLine($"      \"filePath\": \"{EscapeJson(t.FilePath)}\",");
                    sb.AppendLine($"      \"color\": \"#{t.TrackColor.R:X2}{t.TrackColor.G:X2}{t.TrackColor.B:X2}\"");
                    sb.AppendLine($"    }}{sep}");
                }

                sb.AppendLine("  ],");
                sb.AppendLine("  \"rack\": [");

                for (int i = 0; i < _pedalDefs.Length; i++)
                {
                    var def = _pedalDefs[i];
                    var sep = i < _pedalDefs.Length - 1 ? "," : "";
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"name\": \"{EscapeJson(def.Name)}\",");
                    sb.AppendLine("      \"params\": {");
                    for (int p = 0; p < def.Params.Length; p++)
                    {
                        var param = def.Params[p];
                        var psep = p < def.Params.Length - 1 ? "," : "";
                        sb.AppendLine($"        \"{EscapeJson(param.Key)}\": {param.Default:F4}{psep}");
                    }
                    sb.AppendLine("      }");
                    sb.AppendLine($"    }}{sep}");
                }

                sb.AppendLine("  ]");
                sb.AppendLine("}");

                IO.File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
                SaveToRecentHistory(dlg.FileName);
                AddChatMessage($"Sesión guardada: {IO.Path.GetFileName(dlg.FileName)}", false);
                MessageBox.Show($"Sesión guardada correctamente en:\n{dlg.FileName}",
                                "Guardado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddChatMessage($"Error al guardar: {ex.Message}", false);
                MessageBox.Show($"No se pudo guardar la sesión:\n{ex.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static float[] ConvertToMono(float[] buf, int sampleCount)
        {
            var mono = new float[sampleCount / 2];
            for (int i = 0; i < mono.Length; i++)
                mono[i] = (buf[i * 2] + buf[i * 2 + 1]) / 2f;
            return mono;
        }

        private void LoadSession(string path)
        {
            try
            {
                var json = IO.File.ReadAllText(path);
                int tracksStart = json.IndexOf("\"tracks\"");
                if (tracksStart < 0) { AddChatMessage("Sesión sin pistas.", false); return; }

                int arrStart = json.IndexOf('[', tracksStart);
                int arrEnd = json.IndexOf(']', arrStart);
                if (arrStart < 0 || arrEnd < 0) return;

                string tracksJson = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
                var trackBlocks = tracksJson.Split(new[] { '}' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var block in trackBlocks)
                {
                    string name = ExtractJsonString(block, "name");
                    string filePath = ExtractJsonString(block, "filePath");
                    string colorHex = ExtractJsonString(block, "color");

                    if (string.IsNullOrWhiteSpace(name)) continue;

                    Color color;
                    try { color = (Color)ColorConverter.ConvertFromString(colorHex); }
                    catch { color = TrackPalette[_tracks.Count % TrackPalette.Length]; }

                    var track = new AudioTrack { Name = name, FilePath = filePath, TrackColor = color };
                    if (IO.File.Exists(filePath))
                    {
                        Task.Run(() =>
                        {
                            try
                            {
                                using var reader = new AudioFileReader(filePath);
                                int ch = reader.WaveFormat.Channels;
                                int total = (int)(reader.Length / (reader.WaveFormat.BitsPerSample / 8));
                                var buf = new float[Math.Min(total, reader.WaveFormat.SampleRate * 300)];
                                int read = reader.Read(buf, 0, buf.Length);
                                track.Samples = ch == 2
                                    ? ConvertToMono(buf, read)
                                    : buf[..read];
                                track.SampleRate = reader.WaveFormat.SampleRate;
                                Dispatcher.Invoke(() => RefreshTrackWaveform(track));
                            }
                            catch { }
                        });
                    }
                    _tracks.Add(track);
                    AddTrackRow(track, _tracks.Count - 1);
                }
                AddChatMessage($"Sesión cargada: {IO.Path.GetFileName(path)}", false);
                SaveToRecentHistory(path);
            }
            catch (Exception ex)
            {
                AddChatMessage($"Error al cargar sesión: {ex.Message}", false);
            }
        }

        private static string ExtractJsonString(string block, string key)
        {
            int ki = block.IndexOf($"\"{key}\"");
            if (ki < 0) return "";
            int ci = block.IndexOf(':', ki);
            if (ci < 0) return "";
            int q1 = block.IndexOf('"', ci + 1);
            if (q1 < 0) return "";
            int q2 = block.IndexOf('"', q1 + 1);
            if (q2 < 0) return "";
            return block.Substring(q1 + 1, q2 - q1 - 1).Replace("\\\\", "\\").Replace("\\\"", "\"");
        }

        private static void SaveToRecentHistory(string sessionPath)
        {
            try
            {
                string dir = IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PlasticMemoryDAW");
                IO.Directory.CreateDirectory(dir);
                string histFile = IO.Path.Combine(dir, "recent.txt");

                var lines = IO.File.Exists(histFile)
                    ? new List<string>(IO.File.ReadAllLines(histFile))
                    : new List<string>();

                lines.Remove(sessionPath);
                lines.Insert(0, sessionPath);
                if (lines.Count > 10) lines = lines[..10];
                IO.File.WriteAllLines(histFile, lines);
            }
            catch { }
        }
    }
}
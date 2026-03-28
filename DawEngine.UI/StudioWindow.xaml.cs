using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using IO = System.IO;
using NAudio.Wave;
using DawEngine.Core;
using DawEngine.Core.Ai;

namespace DawEngine.UI
{
    public partial class StudioWindow : Window
    {
        // ── Motor de estudio WASAPI ───────────────────────────────────────────
        private readonly StudioEngine _engine = new();
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };

        // ── Rack de efectos ───────────────────────────────────────────────────
        private readonly Dictionary<string, CheckBox> _pedalLeds = new();

        // ¡EL DICCIONARIO NERVIOSO PARA LA IA! 🧠
        private readonly Dictionary<string, (Canvas Canvas, Color Accent, double Min, double Max, Action<double> SetValue)> _knobVisuals = new();

        // ── IA ────────────────────────────────────────────────────────────────
        private readonly LlmService _ai = new();

        // ── BPM + Tap Tempo ───────────────────────────────────────────────────
        private int _bpm = 120;
        private readonly List<DateTime> _tapTimes = new();

        // ── Arrangement ───────────────────────────────────────────────────────
        private const double MixerPanelW = 160.0;
        private double _pxPerSec = 100.0;

        // ── Usuario ───────────────────────────────────────────────────────────
        private readonly DawUser _user;

        // Paleta de colores para pistas
        private static readonly Color[] Palette =
        {
            (Color)ColorConverter.ConvertFromString("#00B4D8"),
            (Color)ColorConverter.ConvertFromString("#FF006E"),
            (Color)ColorConverter.ConvertFromString("#8338EC"),
            (Color)ColorConverter.ConvertFromString("#06D6A0"),
            (Color)ColorConverter.ConvertFromString("#FFB703"),
            (Color)ColorConverter.ConvertFromString("#FB5607"),
            (Color)ColorConverter.ConvertFromString("#3A86FF"),
        };

        // ─────────────────────────────────────────────────────────────────────
        public StudioWindow(DawUser user)
        {
            InitializeComponent();
            _user = user;
            Title = $"DAWY — Modo Estudio  //  {user.Name}";

            BuildRack();

            // Dibujar regla cuando el canvas esté listo
            RulerCanvas.Loaded += (_, _) => RenderRuler();
            RulerCanvas.SizeChanged += (_, _) => RenderRuler();

            _timer.Tick += Timer_Tick;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _timer.Stop();
            _engine.Dispose();
        }

        // ══════════════════════════════════════════════════════════════════════
        // TRANSPORTE
        // ══════════════════════════════════════════════════════════════════════

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_engine.Tracks.Count == 0)
            {
                AddMsg("No hay pistas cargadas. Agrega una con + Pista.", false);
                return;
            }
            try
            {
                _engine.Play();
                BtnPlay.IsEnabled = false;
                BtnPause.IsEnabled = true;
                BtnStop.IsEnabled = true;
                _timer.Start();
                AddMsg("Reproducción iniciada.", false);
            }
            catch (Exception ex)
            {
                AddMsg($"Error al reproducir: {ex.Message}", false);
            }
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            _engine.Pause();
            _timer.Stop();
            BtnPlay.IsEnabled = true;
            BtnPause.IsEnabled = false;
            AddMsg("Pausado.", false);
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _engine.Stop();
            _timer.Stop();
            BtnPlay.IsEnabled = true;
            BtnPause.IsEnabled = false;
            BtnStop.IsEnabled = false;
            VuL.Height = VuR.Height = 0;
            ResetPlayhead();
        }

        private void BtnRewind_Click(object sender, RoutedEventArgs e)
        {
            bool was = _engine.IsPlaying;
            _engine.Stop();
            _timer.Stop();
            ResetPlayhead();
            if (was) BtnPlay_Click(sender, e);
        }

        private void SldMaster_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            foreach (var t in _engine.Tracks)
                t.Volume = (float)e.NewValue;
        }

        // ── BPM ───────────────────────────────────────────────────────────────

        private void SetBpm(int bpm)
        {
            _bpm = Math.Clamp(bpm, 20, 300);
            TxtBpm.Text = _bpm.ToString();
            RenderRuler(); // redibuja la regla con la nueva métrica
        }

        private void BtnBpmUp_Click(object sender, RoutedEventArgs e) => SetBpm(_bpm + 1);
        private void BtnBpmDown_Click(object sender, RoutedEventArgs e) => SetBpm(_bpm - 1);

        // ── Tap Tempo ─────────────────────────────────────────────────────────

        private void BtnTap_Click(object sender, RoutedEventArgs e)
        {
            var now = DateTime.UtcNow;
            _tapTimes.Add(now);

            // Eliminar taps viejos (más de 3 segundos)
            _tapTimes.RemoveAll(t => (now - t).TotalSeconds > 3.0);

            if (_tapTimes.Count >= 2)
            {
                // Calcular BPM promedio entre los últimos taps
                double totalMs = (now - _tapTimes[0]).TotalMilliseconds;
                double avgMs = totalMs / (_tapTimes.Count - 1);
                int newBpm = (int)Math.Round(60000.0 / avgMs);
                SetBpm(newBpm);
            }
        }

        // ── Click en regla para seek ──────────────────────────────────────────

        private void RulerCanvas_Click(object sender, MouseButtonEventArgs e)
            => SeekTo(Math.Max(0, e.GetPosition(RulerCanvas).X / _pxPerSec));

        private void ProgressBar_Click(object sender, MouseButtonEventArgs e)
        {
            double dur = _engine.Duration;
            if (dur <= 0) return;
            SeekTo(Math.Clamp(e.GetPosition((UIElement)sender).X / 120.0, 0, 1) * dur);
        }

        private void SeekTo(double seconds)
        {
            bool was = _engine.IsPlaying;
            if (was) _engine.Pause();

            foreach (var t in _engine.Tracks) t.Seek(seconds);

            double x = MixerPanelW + seconds * _pxPerSec;
            Canvas.SetLeft(PlayheadLine, x - 1);
            Canvas.SetLeft(PlayheadHead, x - 6);

            double dur = _engine.Duration;
            if (dur > 0) ProgressFill.Width = Math.Clamp(seconds / dur, 0, 1) * 120;

            var ts = TimeSpan.FromSeconds(seconds);
            var rem = TimeSpan.FromSeconds(Math.Max(0, dur - seconds));
            TxtPosition.Text = $"{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}";
            TxtRemaining.Text = $"-{rem.Minutes:D2}:{rem.Seconds:D2}";
            double beat = seconds * _bpm / 60.0;
            TxtBarBeat.Text = $"{(int)(beat / 4) + 1}:{(int)(beat % 4) + 1}";

            TrackScroller.ScrollToHorizontalOffset(Math.Max(0, x - 200));

            if (was) _engine.Resume();
        }

        // ── Exportar WAV ──────────────────────────────────────────────────────

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_engine.Tracks.Count == 0) { AddMsg("Sin pistas que exportar.", false); return; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar mezcla",
                Filter = "WAV|*.wav",
                FileName = "DAWY_Mix",
                DefaultExt = ".wav",
            };
            if (dlg.ShowDialog() != true) return;

            bool was = _engine.IsPlaying;
            if (was) { _engine.Pause(); _timer.Stop(); }
            BtnPlay.IsEnabled = false;
            AddMsg("Exportando mezcla...", false);

            try
            {
                await Task.Run(() => _engine.ExportToWav(dlg.FileName,
                    p => Dispatcher.InvokeAsync(() =>
                        TxtPosition.Text = $"Exportando {p * 100:F0}%")));
                AddMsg($"✓ Exportado: {IO.Path.GetFileName(dlg.FileName)}", false);
            }
            catch (Exception ex) { AddMsg($"Error al exportar: {ex.Message}", false); }
            finally
            {
                BtnPlay.IsEnabled = true;
                ResetPlayhead();
                if (was) { _engine.Resume(); _timer.Start(); }
            }
        }

        // Timer — actualiza posición, playhead y VU meter
        private void Timer_Tick(object? sender, EventArgs e)
        {
            double pos = _engine.Position;
            double dur = _engine.Duration;

            // ── Tiempo transcurrido + restante ────────────────────────────────
            var ts = TimeSpan.FromSeconds(pos);
            var rem = TimeSpan.FromSeconds(Math.Max(0, dur - pos));
            TxtPosition.Text = $"{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}";
            TxtRemaining.Text = $"-{rem.Minutes:D2}:{rem.Seconds:D2}";

            // Compás y beat
            double beat = pos * _bpm / 60.0;
            int bar = (int)(beat / 4) + 1;
            int beatNum = (int)(beat % 4) + 1;
            TxtBarBeat.Text = $"{bar}:{beatNum}";

            // ── Barra de progreso (clickeable) ────────────────────────────────
            if (dur > 0)
            {
                double pct = Math.Clamp(pos / dur, 0, 1);
                ProgressFill.Width = pct * 120;

                // Color según lo que queda
                string col2 = pct < 0.75 ? "#00E5FF" : pct < 0.9 ? "#FFDD00" : "#FF3333";
                ProgressFill.Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(col2));
            }

            // ── Playhead ──────────────────────────────────────────────────────
            double x = MixerPanelW + pos * _pxPerSec;
            Canvas.SetLeft(PlayheadLine, x - 1);
            Canvas.SetLeft(PlayheadHead, x - 6);

            // ── Auto-scroll: mantener el playhead visible ─────────────────────
            double viewportW = TrackScroller.ViewportWidth;
            double scrollOffset = TrackScroller.HorizontalOffset;
            double playheadInView = x - scrollOffset;

            // Si el playhead sale por la derecha, hacer scroll
            if (playheadInView > viewportW - 80)
                TrackScroller.ScrollToHorizontalOffset(x - viewportW * 0.3);
            // Si el playhead sale por la izquierda (rewind manual), ajustar
            else if (playheadInView < MixerPanelW + 20)
                TrackScroller.ScrollToHorizontalOffset(Math.Max(0, x - MixerPanelW - 40));

            // ── VU meter ──────────────────────────────────────────────────────
            double rms = _engine.IsPlaying ? 0.3 + 0.3 * Math.Sin(pos * 8) : 0;
            VuL.Height = Math.Max(0, Math.Min(20, rms * 20));
            VuR.Height = Math.Max(0, Math.Min(20, rms * 18));
            string col = rms < 0.5 ? "#00E5FF" : rms < 0.8 ? "#FFDD00" : "#FF3333";
            VuL.Background = VuR.Background =
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(col));

            // ── Detectar fin ──────────────────────────────────────────────────
            if (_engine.IsPlaying && dur > 0 && pos >= dur)
            {
                _engine.Stop();
                _timer.Stop();
                BtnPlay.IsEnabled = true;
                BtnPause.IsEnabled = false;
                BtnStop.IsEnabled = false;
                ResetPlayhead();
            }
        }

        private void ResetPlayhead()
        {
            Canvas.SetLeft(PlayheadLine, MixerPanelW - 1);
            Canvas.SetLeft(PlayheadHead, MixerPanelW - 6);
            TxtPosition.Text = "00:00.00";
            TxtRemaining.Text = "-00:00";
            TxtBarBeat.Text = "1:1";
            ProgressFill.Width = 0;
            TrackScroller.ScrollToHorizontalOffset(0);
        }

        // ══════════════════════════════════════════════════════════════════════
        // PISTAS
        // ══════════════════════════════════════════════════════════════════════

        private void BtnAddTrack_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Importar audio",
                Filter = "Audio|*.wav;*.mp3;*.aiff;*.flac|Todos|*.*",
                Multiselect = true,
            };
            if (dlg.ShowDialog() != true) return;

            foreach (var file in dlg.FileNames)
                ImportAudioFile(file);
        }

        private void ImportAudioFile(string path)
        {
            var track = _engine.AddTrack(
                IO.Path.GetFileNameWithoutExtension(path), path);

            // Ocultar estado vacío
            EmptyState.Visibility = Visibility.Collapsed;

            // Construir fila de pista
            var color = Palette[(_engine.Tracks.Count - 1) % Palette.Length];
            AddTrackRow(track, color);
            AddMsg($"Pista cargada: {track.Name}", false);
        }

        private void AddTrackRow(StudioTrack track, Color color)
        {
            var brush = new SolidColorBrush(color);

            // Contenedor vertical: fila principal + rack FX expandible
            var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 1) };

            // ── FILA PRINCIPAL ────────────────────────────────────────────────
            var row = new Grid { Height = 72 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Panel izquierdo (mixer)
            var left = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderThickness = new Thickness(0, 0, 1, 1),
            };
            var leftStack = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };

            // Nombre
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            nameRow.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = brush, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            nameRow.Children.Add(new TextBlock
            {
                Text = track.Name,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            });
            leftStack.Children.Add(nameRow);

            // Controles: M S FX vol
            var ctrlRow = new StackPanel { Orientation = Orientation.Horizontal };

            var btnM = MakeSmallBtn("M", "#555");
            btnM.Click += (_, _) =>
            {
                track.IsMuted = !track.IsMuted;
                track.UpdateVolume();
                btnM.Background = track.IsMuted
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3333"))
                    : new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
            };

            var btnS = MakeSmallBtn("S", "#555");
            btnS.Click += (_, _) =>
            {
                track.IsSolo = !track.IsSolo;
                btnS.Background = track.IsSolo
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFDD00"))
                    : new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
            };

            // Botón FX — expande/colapsa el rack de efectos
            Border? fxRackBorder = null; // se asigna abajo
            var btnFx = new Button
            {
                Content = "FX",
                Width = 24,
                Height = 16,
                Margin = new Thickness(3, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                Foreground = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
            };

            // Slider volumen
            var volSlider = new Slider
            {
                Minimum = 0,
                Maximum = 1.5,
                Value = track.Volume,
                Width = 58,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            volSlider.ValueChanged += (_, ev) => { track.Volume = (float)ev.NewValue; track.UpdateVolume(); };

            // Botón quitar pista ✕
            var btnX = MakeSmallBtn("✕", "#333");
            btnX.Margin = new Thickness(3, 0, 0, 0);
            btnX.Click += (_, _) =>
            {
                bool was = _engine.IsPlaying;
                if (was) _engine.Pause();
                _engine.RemoveTrack(track);
                TrackPanel.Children.Remove(outer);
                if (_engine.Tracks.Count == 0) EmptyState.Visibility = Visibility.Visible;
                if (was && _engine.Tracks.Count > 0) _engine.Resume();
            };

            ctrlRow.Children.Add(btnM);
            ctrlRow.Children.Add(btnS);
            ctrlRow.Children.Add(btnFx);
            ctrlRow.Children.Add(volSlider);
            ctrlRow.Children.Add(btnX);
            leftStack.Children.Add(ctrlRow);

            left.Child = leftStack;
            Grid.SetColumn(left, 0);
            row.Children.Add(left);

            // Panel waveform
            var wavePanel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(20, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                ClipToBounds = true,
                AllowDrop = true,
                HorizontalAlignment = HorizontalAlignment.Left, // NO stretch — ancho real
            };
            var waveCanvas = new Canvas { Height = 72, Width = 800 }; // ancho inicial, se ajusta al cargar
            waveCanvas.Children.Add(new TextBlock
            {
                Text = track.Name,
                Foreground = new SolidColorBrush(Color.FromArgb(70, color.R, color.G, color.B)),
                FontSize = 9,
                FontFamily = new FontFamily("Courier New"),
                Margin = new Thickness(8, 4, 0, 0),
            });
            wavePanel.Child = waveCanvas;

            Task.Run(() =>
            {
                try { track.Load(); }
                catch { }
                Dispatcher.Invoke(() =>
                {
                    // Ancho real basado en duración × escala
                    double durPx = track.Duration > 0
                        ? track.Duration * _pxPerSec
                        : 800;
                    waveCanvas.Width = durPx;
                    wavePanel.Width = durPx;

                    // Expandir el grid de pistas para permitir scroll horizontal
                    UpdateTrackGridWidth();

                    DrawWaveform(waveCanvas, track, color);
                    RenderRuler(); // redibuja la regla con la nueva duración total
                });
            });

            // Re-dibujar si cambia el zoom (futuro)
            waveCanvas.SizeChanged += (_, _) => DrawWaveform(waveCanvas, track, color);

            wavePanel.Drop += (_, ev) =>
            {
                if (ev.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])ev.Data.GetData(DataFormats.FileDrop)!;
                    if (files.Length > 0)
                    {
                        track.Dispose();
                        track.FilePath = files[0];
                        track.Name = IO.Path.GetFileNameWithoutExtension(files[0]);
                        Task.Run(() =>
                        {
                            track.Load();
                            Dispatcher.Invoke(() =>
                            {
                                double durPx = track.Duration > 0 ? track.Duration * _pxPerSec : 800;
                                waveCanvas.Width = durPx;
                                wavePanel.Width = durPx;
                                UpdateTrackGridWidth();
                                DrawWaveform(waveCanvas, track, color);
                                RenderRuler();
                            });
                        });
                    }
                }
            };

            Grid.SetColumn(wavePanel, 1);
            row.Children.Add(wavePanel);
            outer.Children.Add(row);

            // ── RACK FX EXPANDIBLE ────────────────────────────────────────────
            fxRackBorder = BuildFxRack(track, color);
            fxRackBorder.Visibility = Visibility.Collapsed;
            outer.Children.Add(fxRackBorder);

            // Toggle FX
            btnFx.Click += (_, _) =>
            {
                bool open = fxRackBorder.Visibility == Visibility.Visible;
                fxRackBorder.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
                btnFx.Background = open
                    ? new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22))
                    : new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B));
            };

            TrackPanel.Children.Add(outer);
        }

        // Calcula el ancho total del arrangement y lo aplica al TrackGrid
        // ── Herramienta activa ────────────────────────────────────────────────
        private enum EditTool { Select, Cut }
        private EditTool _activeTool = EditTool.Select;

        private void BtnToolSelect_Click(object sender, RoutedEventArgs e)
        {
            _activeTool = EditTool.Select;
            SetToolStyle(BtnToolSelect, "#FF007F", "#1A0808");
            SetToolStyle(BtnToolCut, "#555", "#141414");
        }

        private void BtnToolCut_Click(object sender, RoutedEventArgs e)
        {
            _activeTool = EditTool.Cut;
            SetToolStyle(BtnToolCut, "#88FF44", "#0A1808");
            SetToolStyle(BtnToolSelect, "#555", "#141414");
        }

        private static void SetToolStyle(Button btn, string fg, string bg)
        {
            btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg));
            btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
            btn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg));
        }

        // Recarga una pista aplicando los cambios de edición
        private void ReloadTrack(StudioTrack track, Canvas waveCanvas, Color color)
        {
            bool was = _engine.IsPlaying;
            if (was) _engine.Pause();
            track.Dispose();
            track.Load();
            double durPx = Math.Max(1, track.ClipDuration * _pxPerSec);
            waveCanvas.Width = durPx;
            if (waveCanvas.Parent is Border wp) wp.Width = durPx;
            UpdateTrackGridWidth();
            DrawWaveform(waveCanvas, track, color);
            RenderRuler();
            if (was) _engine.Resume();
        }

        private void UpdateTrackGridWidth()
        {
            double maxDur = _engine.Tracks.Count > 0
                ? _engine.Tracks.Max(t => t.Duration)
                : 0;
            double totalW = MixerPanelW + Math.Max(maxDur * _pxPerSec + 200, 800);
            TrackGrid.MinWidth = totalW;
        }

        // ── Mini-rack de efectos por pista ────────────────────────────────────
        private Border BuildFxRack(StudioTrack track, Color color)
        {
            var rack = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x0E)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 8, 10, 8),
            };

            var content = new StackPanel();

            // Header del rack
            var hdr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            hdr.Children.Add(new TextBlock
            {
                Text = "INSERT FX",
                Foreground = new SolidColorBrush(color),
                FontSize = 9,
                FontFamily = new FontFamily("Courier New"),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            });

            // Selector para agregar efecto
            var combo = new ComboBox
            {
                Width = 110,
                Height = 22,
                FontSize = 10,
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            };
            foreach (var name in FxNames)
                combo.Items.Add(name);
            combo.SelectedIndex = 0;

            var btnAdd = new Button
            {
                Content = "+ ADD",
                Height = 22,
                Padding = new Thickness(8, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)),
                Foreground = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 0, 0),
            };

            var slotPanel = new StackPanel { Orientation = Orientation.Horizontal };

            btnAdd.Click += (_, _) =>
            {
                if (combo.SelectedItem is not string fxName) return;
                var proc = CreateFxProcessor(fxName);
                if (proc == null) return;
                track.FxProcessors.Add(proc);
                // Agregar slot visual
                slotPanel.Children.Add(BuildFxSlot(fxName, proc, color, slotPanel, track));
                // Recargar cadena de audio si ya está reproduciendo
                if (_engine.IsPlaying) { _engine.Pause(); _engine.Resume(); }
            };

            hdr.Children.Add(combo);
            hdr.Children.Add(btnAdd);
            content.Children.Add(hdr);
            content.Children.Add(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = slotPanel,
            });

            rack.Child = content;
            return rack;
        }

        // Slot visual de un efecto individual dentro del rack de pista
        private UIElement BuildFxSlot(string fxName, IAudioProcessor proc, Color color,
            StackPanel parent, StudioTrack track)
        {
            var def = _pedalDefs.FirstOrDefault(d => d.Name == fxName);

            var slot = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x15)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 6, 0),
                MinWidth = 80,
            };

            var stack = new StackPanel();

            // Header: LED + nombre + X
            var hdr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };

            // LED bypass
            var led = new CheckBox
            {
                IsChecked = true,
                Width = 10,
                Height = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
            };
            led.Checked += (_, _) => proc.IsEnabled = true;
            led.Unchecked += (_, _) => proc.IsEnabled = false;

            hdr.Children.Add(led);
            hdr.Children.Add(new TextBlock
            {
                Text = fxName,
                Foreground = new SolidColorBrush(color),
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
            });

            // X quitar
            var btnX = new Button
            {
                Content = "✕",
                Width = 14,
                Height = 14,
                FontSize = 7,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                Cursor = Cursors.Hand,
            };
            var captSlot = slot;
            btnX.Click += (_, _) =>
            {
                track.FxProcessors.Remove(proc);
                parent.Children.Remove(captSlot);
                if (_engine.IsPlaying) { _engine.Pause(); _engine.Resume(); }
            };
            hdr.Children.Add(btnX);
            stack.Children.Add(hdr);

            // Knobs de parámetros
            if (def != null)
            {
                var knobRow = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var p in def.Params)
                {
                    double curVal = p.Default;
                    double dragY = 0, dragVal = curVal;
                    bool mouseDown = false, dragging = false;
                    const double DragThreshold = 4.0;

                    var kStack = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
                    var canvas = new Canvas { Width = 36, Height = 36, Cursor = Cursors.SizeNS };
                    DrawFxKnob(canvas, color, curVal, p.Min, p.Max);

                    var cp = p; var cv = curVal;
                    canvas.MouseLeftButtonDown += (_, ev) =>
                    {
                        mouseDown = true; dragY = ev.GetPosition(null).Y;
                        dragVal = curVal; ev.Handled = true;
                    };
                    canvas.MouseMove += (_, ev) =>
                    {
                        if (!mouseDown) return;
                        double cy = ev.GetPosition(null).Y;
                        if (!dragging)
                        {
                            if (Math.Abs(cy - dragY) < DragThreshold) return;
                            dragging = true; dragY = dragY; canvas.CaptureMouse();
                        }
                        double dy = dragY - cy;
                        double nv = Math.Clamp(dragVal + dy / 80.0 * (cp.Max - cp.Min), cp.Min, cp.Max);
                        curVal = nv;
                        proc.UpdateParameter(cp.Key, (float)nv);
                        DrawFxKnob(canvas, color, nv, cp.Min, cp.Max);
                    };
                    canvas.MouseLeftButtonUp += (_, _) =>
                    {
                        mouseDown = false;
                        if (dragging) { dragging = false; canvas.ReleaseMouseCapture(); }
                    };
                    canvas.MouseWheel += (_, ev) =>
                    {
                        double step = (cp.Max - cp.Min) / 100.0;
                        double nv = Math.Clamp(curVal + (ev.Delta > 0 ? step : -step), cp.Min, cp.Max);
                        curVal = nv;
                        proc.UpdateParameter(cp.Key, (float)nv);
                        DrawFxKnob(canvas, color, nv, cp.Min, cp.Max);
                        ev.Handled = true;
                    };

                    kStack.Children.Add(canvas);
                    kStack.Children.Add(new TextBlock
                    {
                        Text = p.Label,
                        FontSize = 7,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    });
                    knobRow.Children.Add(kStack);
                    proc.UpdateParameter(p.Key, (float)p.Default);
                }
                stack.Children.Add(knobRow);
            }

            slot.Child = stack;
            return slot;
        }

        // Knob pequeño para el FX de pista (36px)
        private static void DrawFxKnob(Canvas c, Color accent, double val, double min, double max)
        {
            c.Children.Clear();
            double s = c.Width, cx = s / 2, cy = s / 2, r = s / 2 - 3;

            var ellipse = new Ellipse
            {
                Width = s - 4,
                Height = s - 4,
                Fill = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
                Stroke = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                StrokeThickness = 1,
            };
            Canvas.SetLeft(ellipse, 2);
            Canvas.SetTop(ellipse, 2);
            c.Children.Add(ellipse);

            double t = (val - min) / (max - min);
            double sweep = 270 * t;
            if (sweep > 1)
            {
                double sRad = 135 * Math.PI / 180;
                double eRad = (135 + sweep) * Math.PI / 180;
                var arc = new System.Windows.Shapes.Path
                {
                    Stroke = new SolidColorBrush(accent),
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent,
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
            }
        }

        // Factory de procesadores para el FX de pista
        private static readonly string[] FxNames =
        {
            "GAIN", "GATE", "COMPRESSOR", "HARD CLIP", "OVERDRIVE", "FUZZ",
            "LP FILTER", "HP FILTER", "BAND PASS", "WAH",
            "DELAY", "REVERB", "CHORUS", "PHASER", "TREMOLO",
            "BITCRUSHER", "RING MOD", "PITCH SHIFT", "PAN",
        };

        private static IAudioProcessor? CreateFxProcessor(string name) => name switch
        {
            "GAIN" => new GainProcessor(),
            "GATE" => new NoiseGateProcessor(),
            "COMPRESSOR" => new CompressorProcessor(),
            "HARD CLIP" => new HardClipperProcessor(),
            "OVERDRIVE" => new OverdriveProcessor(),
            "FUZZ" => new FuzzProcessor(),
            "LP FILTER" => new LowPassFilter(),
            "HP FILTER" => new HighPassProcessor(),
            "BAND PASS" => new BandPassProcessor(),
            "WAH" => new WahProcessor(),
            "DELAY" => new DelayProcessor(),
            "REVERB" => new SchroederReverbProcessor(),
            "CHORUS" => new ChorusProcessor(),
            "PHASER" => new PhaserProcessor(),
            "TREMOLO" => new TremoloProcessor(),
            "BITCRUSHER" => new BitcrusherProcessor(),
            "RING MOD" => new RingModulatorProcessor(),
            "PITCH SHIFT" => new PitchShifterProcessor(),
            "PAN" => new PanProcessor(),
            _ => null,
        };

        private void DrawWaveform(Canvas canvas, StudioTrack track, Color color)
        {
            // Limpiar todo excepto la etiqueta de nombre (índice 0)
            for (int i = canvas.Children.Count - 1; i >= 1; i--)
                canvas.Children.RemoveAt(i);

            double w = canvas.Width > 0 ? canvas.Width : canvas.ActualWidth;
            double h = canvas.Height;
            if (w <= 0 || h <= 0) return;

            double mid = h / 2;
            double durPx = w; // el canvas ya tiene el ancho de la duración real
            var brush = new SolidColorBrush(Color.FromArgb(160, color.R, color.G, color.B));

            // ── 1. FONDO del clip ─────────────────────────────────────────────
            canvas.Children.Add(new Rectangle
            {
                Width = w,
                Height = h,
                Fill = new SolidColorBrush(Color.FromArgb(25, color.R, color.G, color.B)),
            });

            // ── 2. FADE IN overlay ────────────────────────────────────────────
            if (track.FadeIn > 0)
            {
                double fadePx = Math.Min(track.FadeIn * _pxPerSec, w);
                var fadeRect = new Rectangle
                {
                    Width = fadePx,
                    Height = h,
                    Fill = new LinearGradientBrush(
                        Color.FromArgb(80, 0, 0, 0),
                        Color.FromArgb(0, 0, 0, 0),
                        0),
                };
                canvas.Children.Add(fadeRect);
            }

            // ── 3. FADE OUT overlay ───────────────────────────────────────────
            if (track.FadeOut > 0)
            {
                double fadePx = Math.Min(track.FadeOut * _pxPerSec, w);
                var fadeRect = new Rectangle
                {
                    Width = fadePx,
                    Height = h,
                    Fill = new LinearGradientBrush(
                        Color.FromArgb(0, 0, 0, 0),
                        Color.FromArgb(80, 0, 0, 0),
                        0),
                };
                Canvas.SetLeft(fadeRect, w - fadePx);
                canvas.Children.Add(fadeRect);
            }

            // ── 4. WAVEFORM ───────────────────────────────────────────────────
            if (track.WaveformPeaks != null && track.WaveformPeaks.Length > 0)
            {
                // Calcular qué porción de los peaks mostrar (según trim)
                double rawDur = track.Duration;
                double trimStart = track.TrimStart;
                double trimEnd = track.TrimEnd > 0 ? track.TrimEnd : rawDur;
                double clipDur = trimEnd - trimStart;
                int totalPeaks = track.WaveformPeaks.Length;
                int startPeak = rawDur > 0 ? (int)(trimStart / rawDur * totalPeaks) : 0;
                int endPeak = rawDur > 0 ? (int)(trimEnd / rawDur * totalPeaks) : totalPeaks;
                int peakCount = Math.Max(1, endPeak - startPeak);
                double pxPerPeak = w / peakCount;

                for (int p = 0; p < peakCount; p++)
                {
                    int pi = Math.Clamp(startPeak + p, 0, totalPeaks - 1);
                    double amp = track.WaveformPeaks[pi];
                    double bh = Math.Max(1.5, amp * (mid - 4) * 2);
                    var bar = new Rectangle
                    {
                        Width = Math.Max(1, pxPerPeak - 0.5),
                        Height = bh,
                        Fill = brush,
                    };
                    Canvas.SetLeft(bar, p * pxPerPeak);
                    Canvas.SetTop(bar, mid - bh / 2);
                    canvas.Children.Add(bar);
                }
            }
            else
            {
                var rng = new Random(track.Name.GetHashCode());
                for (int px = 0; px < (int)w; px += 2)
                {
                    double env = Math.Sin(px / w * Math.PI);
                    double v = (rng.NextDouble() * 2 - 1) * 0.7 * env;
                    double bh = Math.Max(2, Math.Abs(v) * (mid - 4) * 2);
                    var bar = new Rectangle { Width = 1.5, Height = bh, Fill = brush };
                    Canvas.SetLeft(bar, px); Canvas.SetTop(bar, mid - bh / 2);
                    canvas.Children.Add(bar);
                }
            }

            // ── 5. BORDE superior e inferior del clip ─────────────────────────
            canvas.Children.Add(new Rectangle
            {
                Width = w,
                Height = 1,
                Fill = new SolidColorBrush(Color.FromArgb(100, color.R, color.G, color.B)),
            });
            var bottomLine = new Rectangle
            {
                Width = w,
                Height = 1,
                Fill = new SolidColorBrush(Color.FromArgb(100, color.R, color.G, color.B)),
            };
            Canvas.SetTop(bottomLine, h - 1);
            canvas.Children.Add(bottomLine);

            // ── 6. HANDLES visuales ───────────────────────────────────────────
            AddClipHandles(canvas, track, color, w, h);
        }

        // Agrega los handles interactivos sobre el clip
        private void AddClipHandles(Canvas canvas, StudioTrack track, Color color, double w, double h)
        {
            const double handleW = 10;
            const double fadeTriW = 20;

            // ── Handle TRIM IZQUIERDO (borde izq, resize cursor) ──────────────
            var trimL = new Border
            {
                Width = handleW,
                Height = h,
                Background = new SolidColorBrush(Color.FromArgb(160, color.R, color.G, color.B)),
                Cursor = Cursors.SizeWE,
                ToolTip = "Arrastrar: recortar inicio",
            };
            Canvas.SetLeft(trimL, 0);
            AddTrimHandle(trimL, canvas, track, color, isLeft: true);
            canvas.Children.Add(trimL);

            // ── Handle TRIM DERECHO ───────────────────────────────────────────
            var trimR = new Border
            {
                Width = handleW,
                Height = h,
                Background = new SolidColorBrush(Color.FromArgb(160, color.R, color.G, color.B)),
                Cursor = Cursors.SizeWE,
                ToolTip = "Arrastrar: recortar final",
            };
            Canvas.SetLeft(trimR, w - handleW);
            AddTrimHandle(trimR, canvas, track, color, isLeft: false);
            canvas.Children.Add(trimR);

            // ── Handle MOVER (zona central, cursor mano) ──────────────────────
            var moveHandle = new Border
            {
                Width = Math.Max(0, w - handleW * 2 - fadeTriW * 2),
                Height = 14,
                Background = new SolidColorBrush(Color.FromArgb(50, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Cursor = _activeTool == EditTool.Cut ? Cursors.Cross : Cursors.SizeAll,
                ToolTip = _activeTool == EditTool.Cut ? "Clic: dividir clip aquí" : "Arrastrar: mover clip",
            };
            Canvas.SetLeft(moveHandle, handleW + fadeTriW);
            Canvas.SetTop(moveHandle, 0);
            AddMoveHandle(moveHandle, canvas, track, color, w);
            canvas.Children.Add(moveHandle);

            // ── Triángulo FADE IN ─────────────────────────────────────────────
            var fiTri = new Polygon
            {
                Points = new PointCollection { new(0, 0), new(fadeTriW, 0), new(0, h) },
                Fill = new SolidColorBrush(Color.FromArgb(120, color.R, color.G, color.B)),
                Cursor = Cursors.SizeNWSE,
                ToolTip = "Arrastrar: fade in",
            };
            Canvas.SetLeft(fiTri, handleW);
            AddFadeHandle(fiTri, canvas, track, color, w, isFadeIn: true);
            canvas.Children.Add(fiTri);

            // ── Triángulo FADE OUT ────────────────────────────────────────────
            var foTri = new Polygon
            {
                Points = new PointCollection { new(fadeTriW, 0), new(fadeTriW, h), new(0, 0) },
                Fill = new SolidColorBrush(Color.FromArgb(120, color.R, color.G, color.B)),
                Cursor = Cursors.SizeNESW,
                ToolTip = "Arrastrar: fade out",
            };
            Canvas.SetLeft(foTri, w - handleW - fadeTriW);
            AddFadeHandle(foTri, canvas, track, color, w, isFadeIn: false);
            canvas.Children.Add(foTri);
        }

        // Handle de trim izquierdo / derecho
        private void AddTrimHandle(UIElement handle, Canvas canvas, StudioTrack track, Color color, bool isLeft)
        {
            double startX = 0, startTrim = 0;
            bool dragging = false, mouseDown = false;
            const double threshold = 3;

            handle.MouseLeftButtonDown += (_, e) =>
            {
                mouseDown = true;
                startX = e.GetPosition(null).X;
                startTrim = isLeft ? track.TrimStart : (track.TrimEnd > 0 ? track.TrimEnd : track.Duration);
                e.Handled = true;
            };
            handle.MouseMove += (_, e) =>
            {
                if (!mouseDown) return;
                double dx = e.GetPosition(null).X - startX;
                if (!dragging) { if (Math.Abs(dx) < threshold) return; dragging = true; handle.CaptureMouse(); }

                double dSec = dx / _pxPerSec;
                if (isLeft)
                {
                    double newStart = Math.Clamp(startTrim + dSec, 0,
                        track.TrimEnd > 0 ? track.TrimEnd - 0.1 : track.Duration - 0.1);
                    track.TrimStart = newStart;
                }
                else
                {
                    double newEnd = Math.Clamp(startTrim + dSec, track.TrimStart + 0.1, track.Duration);
                    track.TrimEnd = newEnd;
                }

                // Actualizar visual en tiempo real
                double clipDurPx = track.ClipDuration * _pxPerSec;
                canvas.Width = clipDurPx;
                if (canvas.Parent is Border wp) wp.Width = clipDurPx;
                DrawWaveform(canvas, track, color);
            };
            handle.MouseLeftButtonUp += (_, _) =>
            {
                mouseDown = false;
                if (dragging)
                {
                    dragging = false;
                    handle.ReleaseMouseCapture();
                    ReloadTrack(track, canvas, color);
                }
            };
        }

        // Handle de mover clip (también maneja la herramienta cortar)
        private void AddMoveHandle(UIElement handle, Canvas canvas, StudioTrack track, Color color, double clipW)
        {
            double startX = 0;
            bool dragging = false, mouseDown = false;
            const double threshold = 4;

            handle.MouseLeftButtonDown += (_, e) =>
            {
                if (_activeTool == EditTool.Cut)
                {
                    // Dividir el clip en la posición del clic
                    double relX = e.GetPosition(canvas).X;
                    double seconds = relX / _pxPerSec + track.TrimStart;
                    CutClip(track, canvas, color, seconds);
                    e.Handled = true;
                    return;
                }
                mouseDown = true;
                startX = e.GetPosition(null).X;
                e.Handled = true;
            };
            handle.MouseMove += (_, e) =>
            {
                if (!mouseDown || _activeTool == EditTool.Cut) return;
                double dx = e.GetPosition(null).X - startX;
                if (!dragging) { if (Math.Abs(dx) < threshold) return; dragging = true; handle.CaptureMouse(); }

                // Mover offset del clip en el timeline
                double newOffset = Math.Max(0, track.ClipOffset + dx / _pxPerSec);
                track.ClipOffset = newOffset;
                startX = e.GetPosition(null).X;

                // Mover visualmente la columna waveform en el grid
                if (canvas.Parent is Border wp && wp.Parent is Grid row)
                    Canvas.SetLeft(wp, 0); // el offset se maneja con margin/transform en futuro
            };
            handle.MouseLeftButtonUp += (_, _) =>
            {
                mouseDown = false;
                if (dragging) { dragging = false; handle.ReleaseMouseCapture(); }
            };
        }

        // Handle de fade in / fade out
        private void AddFadeHandle(UIElement handle, Canvas canvas, StudioTrack track, Color color, double clipW, bool isFadeIn)
        {
            double startX = 0, startFade = 0;
            bool dragging = false, mouseDown = false;
            const double threshold = 3;

            handle.MouseLeftButtonDown += (_, e) =>
            {
                mouseDown = true;
                startX = e.GetPosition(null).X;
                startFade = isFadeIn ? track.FadeIn : track.FadeOut;
                e.Handled = true;
            };
            handle.MouseMove += (_, e) =>
            {
                if (!mouseDown) return;
                double dx = e.GetPosition(null).X - startX;
                if (!dragging) { if (Math.Abs(dx) < threshold) return; dragging = true; handle.CaptureMouse(); }

                double dSec = (isFadeIn ? dx : -dx) / _pxPerSec;
                double maxFade = track.ClipDuration / 2;
                double newFade = Math.Clamp(startFade + dSec, 0, maxFade);

                if (isFadeIn) track.FadeIn = newFade;
                else track.FadeOut = newFade;

                DrawWaveform(canvas, track, color); // preview en tiempo real
            };
            handle.MouseLeftButtonUp += (_, _) =>
            {
                mouseDown = false;
                if (dragging)
                {
                    dragging = false;
                    handle.ReleaseMouseCapture();
                    ReloadTrack(track, canvas, color);
                }
            };
        }

        // Dividir clip en dos (herramienta cortar)
        private void CutClip(StudioTrack original, Canvas canvas, Color color, double cutPoint)
        {
            if (cutPoint <= original.TrimStart + 0.1 || cutPoint >= (original.TrimEnd > 0 ? original.TrimEnd : original.Duration) - 0.1)
                return;

            double origTrimEnd = original.TrimEnd > 0 ? original.TrimEnd : original.Duration;

            // Primera mitad — ajustar trim end del original
            original.TrimEnd = cutPoint;
            ReloadTrack(original, canvas, color);

            // Segunda mitad — nueva pista con trim start
            var newTrack = _engine.AddTrack(original.Name + " [2]", original.FilePath);
            newTrack.TrimStart = cutPoint;
            newTrack.TrimEnd = origTrimEnd;
            newTrack.ClipOffset = original.ClipOffset + (cutPoint - original.TrimStart);
            newTrack.Volume = original.Volume;

            EmptyState.Visibility = Visibility.Collapsed;
            var newColor = Palette[(_engine.Tracks.Count - 1) % Palette.Length];
            AddTrackRow(newTrack, newColor);
            AddMsg($"Clip dividido en '{original.Name}' y '{newTrack.Name}'", false);
        }

        // ══════════════════════════════════════════════════════════════════════
        // RACK DE EFECTOS (mismo sistema que MainWindow)
        // ══════════════════════════════════════════════════════════════════════

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
            new("DELAY",       "#FFDD00", new[]{ new PedalParam("TIME","Time",50,1000,350), new PedalParam("FDBK","Feedback",0,0.95,0.4), new PedalParam("MIX","Mix",0,1,0.5) }),
            new("REVERB",      "#88AAFF", new[]{ new PedalParam("ROOM","RoomSize",0.1,0.98,0.8), new PedalParam("MIX","Mix",0,1,0.4) }),
        };

        private void BuildRack()
        {
            RackPanel.Children.Clear();
            foreach (var def in _pedalDefs)
            {
                var pedalColor = (Color)ColorConverter.ConvertFromString(def.ColorHex);
                var pedalBrush = new SolidColorBrush(pedalColor);

                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x15)),
                    CornerRadius = new CornerRadius(8),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(50, pedalColor.R, pedalColor.G, pedalColor.B)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 8, 0),
                    Padding = new Thickness(10, 7, 10, 7),
                    Width = 85 + def.Params.Length * 38,
                };

                var stack = new StackPanel();

                var hdr = new Grid();
                hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var led = new CheckBox
                {
                    IsChecked = false,
                    Margin = new Thickness(0, 0, 5, 0),
                    Style = (Style?)TryFindResource("LedToggle"),
                };
                _pedalLeds[def.Name.ToUpper()] = led;
                Grid.SetColumn(led, 0);

                var lbl = new TextBlock
                {
                    Text = def.Name,
                    Foreground = pedalBrush,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(lbl, 1);

                var pwr = new Button
                {
                    Content = "⏻",
                    Width = 20,
                    Height = 20,
                    Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                    BorderThickness = new Thickness(1),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    FontSize = 10,
                    Cursor = Cursors.Hand,
                };
                var capturedLed = led;
                pwr.Click += (_, _) => capturedLed.IsChecked = !capturedLed.IsChecked;
                Grid.SetColumn(pwr, 2);

                hdr.Children.Add(led);
                hdr.Children.Add(lbl);
                hdr.Children.Add(pwr);
                stack.Children.Add(hdr);
                stack.Children.Add(new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromArgb(30, pedalColor.R, pedalColor.G, pedalColor.B)),
                    Margin = new Thickness(0, 5, 0, 6),
                });

                var knobRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

                foreach (var param in def.Params)
                {
                    var kStack = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
                    var kCanvas = new Canvas { Width = 40, Height = 40, Cursor = Cursors.SizeNS };
                    DrawKnob(kCanvas, pedalColor, param.Default, param.Min, param.Max);

                    double curVal = param.Default, dragY = 0, dragVal = param.Default;
                    bool dragging = false;
                    var cc = kCanvas; var cp = param; var cd = def;

                    // ⚡ ¡ENLAZAMOS LA PERILLA AL CEREBRO DE LA IA! ⚡
                    _knobVisuals[$"{def.Name.ToUpper()}_{param.Key.ToUpper()}"] = (kCanvas, pedalColor, param.Min, param.Max, (v) => curVal = v);

                    kCanvas.MouseLeftButtonDown += (_, ev) => { dragging = true; dragY = ev.GetPosition(null).Y; dragVal = curVal; cc.CaptureMouse(); };
                    kCanvas.MouseMove += (_, ev) =>
                    {
                        if (!dragging) return;
                        double dy = dragY - ev.GetPosition(null).Y;
                        double nv = Math.Clamp(dragVal + dy / 100.0 * (cp.Max - cp.Min), cp.Min, cp.Max);
                        curVal = nv;
                        DrawKnob(cc, pedalColor, nv, cp.Min, cp.Max);
                    };
                    kCanvas.MouseLeftButtonUp += (_, _) => { dragging = false; cc.ReleaseMouseCapture(); };
                    kCanvas.MouseWheel += (_, ev) =>
                    {
                        double step = (cp.Max - cp.Min) / 100.0;
                        double nv = Math.Clamp(curVal + (ev.Delta > 0 ? step : -step), cp.Min, cp.Max);
                        curVal = nv;
                        DrawKnob(cc, pedalColor, nv, cp.Min, cp.Max);
                    };

                    kStack.Children.Add(kCanvas);
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
                border.Child = stack;
                RackPanel.Children.Add(border);
            }
        }

        private static void DrawKnob(Canvas c, Color accent, double val, double min, double max)
        {
            c.Children.Clear();
            double s = c.Width, cx = s / 2, cy = s / 2, r = s / 2 - 4;
            c.Children.Add(new Ellipse
            {
                Width = s - 4,
                Height = s - 4,
                Fill = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                Stroke = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                StrokeThickness = 1,
            });
            Canvas.SetLeft(c.Children[^1], 2);
            Canvas.SetTop(c.Children[^1], 2);

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
        // DRAW.IA CHAT (¡NUEVO CEREBRO VISUAL!)
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

                    if (resp.Changes != null && resp.Changes.Count > 0)
                    {
                        foreach (var change in resp.Changes)
                        {
                            string eName = change.EffectName?.ToUpper() ?? "";

                            // A) Activar/Desactivar el LED Visual
                            if (change.Enable.HasValue && _pedalLeds.TryGetValue(eName, out var led))
                            {
                                led.IsChecked = change.Enable.Value;
                            }

                            // B) Mover la perilla en el Rack Maestro
                            if (!string.IsNullOrEmpty(change.ParameterName))
                            {
                                string dictKey = $"{eName}_{change.ParameterName.ToUpper()}";
                                if (_knobVisuals.TryGetValue(dictKey, out var vis))
                                {
                                    // 1. Redibujar el control visual en el UI
                                    DrawKnob(vis.Canvas, vis.Accent, change.Value, vis.Min, vis.Max);

                                    // 2. ¡Evitar el efecto liga! Actualiza la variable drag local
                                    vis.SetValue(change.Value);
                                }
                            }
                        }
                    }
                }
                else { AddMsg("Sin respuesta de Draw.ia.", false); }
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
                Margin = new Thickness(0, 0, 0, 6),
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
        // GUARDAR SESIÓN
        // ══════════════════════════════════════════════════════════════════════

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Guardar sesión de estudio",
                Filter = "Sesión DAWY|*.dawsession",
                FileName = "MiSesionEstudio",
                DefaultExt = ".dawsession",
            };
            if (dlg.ShowDialog() != true) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"mode\": \"Studio\",");
            sb.AppendLine($"  \"savedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
            sb.AppendLine($"  \"user\": \"{_user.Name}\",");
            sb.AppendLine("  \"tracks\": [");
            var tracks = _engine.Tracks;
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                var sep = i < tracks.Count - 1 ? "," : "";
                sb.AppendLine($"    {{ \"name\": \"{Esc(t.Name)}\", \"file\": \"{Esc(t.FilePath)}\", \"volume\": {t.Volume:F2}, \"pan\": {t.Pan:F2} }}{sep}");
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            IO.File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
            AddMsg($"Sesión guardada: {IO.Path.GetFileName(dlg.FileName)}", false);
        }

        private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // ══════════════════════════════════════════════════════════════════════
        // REGLA DE TIEMPO
        // ══════════════════════════════════════════════════════════════════════

        private void RenderRuler()
        {
            RulerCanvas.Children.Clear();
            double w = RulerCanvas.ActualWidth;
            if (w <= 0) return;

            double secPerBeat = 60.0 / _bpm;
            double pxPerBeat = secPerBeat * _pxPerSec;
            double totalSec = Math.Max(w / _pxPerSec, _engine.Duration + 8);
            int totalBeats = (int)(totalSec / secPerBeat) + 1;

            for (int b = 0; b <= totalBeats; b++)
            {
                double x = b * pxPerBeat;
                if (x > w + 10) break;

                bool isBar = b % 4 == 0;
                int bar = b / 4 + 1;
                int beat = b % 4 + 1;

                // Línea vertical
                RulerCanvas.Children.Add(new Line
                {
                    X1 = x,
                    Y1 = isBar ? 0 : 10,
                    X2 = x,
                    Y2 = 28,
                    Stroke = new SolidColorBrush(isBar
                        ? Color.FromRgb(0x33, 0x33, 0x33)
                        : Color.FromRgb(0x1E, 0x1E, 0x1E)),
                    StrokeThickness = isBar ? 1 : 0.5,
                });

                // Etiqueta de compás
                if (isBar)
                {
                    var lbl = new TextBlock
                    {
                        Text = $"{bar}",
                        Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                        FontSize = 9,
                        FontFamily = new FontFamily("Courier New"),
                        FontWeight = FontWeights.Bold,
                    };
                    Canvas.SetLeft(lbl, x + 3);
                    Canvas.SetTop(lbl, 2);
                    RulerCanvas.Children.Add(lbl);
                }
                else if (pxPerBeat > 20) // solo mostrar beats si hay espacio
                {
                    var lbl = new TextBlock
                    {
                        Text = $".{beat}",
                        Foreground = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                        FontSize = 8,
                        FontFamily = new FontFamily("Courier New"),
                    };
                    Canvas.SetLeft(lbl, x + 2);
                    Canvas.SetTop(lbl, 8);
                    RulerCanvas.Children.Add(lbl);
                }
            }

            // Marcar segundos también si hay espacio
            if (_pxPerSec > 60)
            {
                double dur = Math.Max(w / _pxPerSec, _engine.Duration + 4);
                for (int s = 1; s <= (int)dur; s++)
                {
                    double x = s * _pxPerSec;
                    if (x > w) break;
                    var ts = TimeSpan.FromSeconds(s);
                    var lbl = new TextBlock
                    {
                        Text = $"{ts.Minutes}:{ts.Seconds:D2}",
                        Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                        FontSize = 8,
                        FontFamily = new FontFamily("Courier New"),
                    };
                    Canvas.SetLeft(lbl, x + 2);
                    Canvas.SetTop(lbl, 17);
                    RulerCanvas.Children.Add(lbl);
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static Button MakeSmallBtn(string label, string colorHex) => new()
        {
            Content = label,
            Width = 22,
            Height = 16,
            Margin = new Thickness(0, 0, 4, 0),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#222")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333")),
            BorderThickness = new Thickness(1),
            FontSize = 9,
            Cursor = Cursors.Hand,
        };
    }
}
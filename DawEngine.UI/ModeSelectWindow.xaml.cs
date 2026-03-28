using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DawEngine.UI
{
    public enum DawMode { Live, Studio }

    public partial class ModeSelectWindow : Window
    {
        public DawMode       SelectedMode       { get; private set; }
        public string        SelectedInstrument { get; private set; } = "Guitarra eléctrica";

        public ModeSelectWindow(DawUser user)
        {
            InitializeComponent();
            TxtUserGreet.Text = user.IsGuest
                ? "Bienvenido, Invitado"
                : $"Bienvenido, {user.Name}";
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        // ── Selección de modo ─────────────────────────────────────────────────

        private void CardLive_Click(object sender, MouseButtonEventArgs e)
        {
            SelectedMode             = DawMode.Live;
            PageMode.Visibility      = Visibility.Collapsed;
            PageInstrument.Visibility= Visibility.Visible;
            BtnBackMode.Visibility   = Visibility.Visible;
        }

        private void CardStudio_Click(object sender, MouseButtonEventArgs e)
        {
            SelectedMode = DawMode.Studio;
            DialogResult = true;
        }

        // ── Selección de instrumento ──────────────────────────────────────────

        private static readonly string[] ChipNames =
            { "ChipGuitar", "ChipBass", "ChipVoice", "ChipKeys", "ChipDrums" };

        private void Chip_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border chip) return;

            // Reset color de chips
            foreach (var name in ChipNames)
            {
                if (FindName(name) is Border b)
                {
                    b.Background   = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
                    b.BorderBrush  = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
                }
            }

            // Marcar seleccionado
            chip.Background  = new SolidColorBrush(Color.FromRgb(0x1A, 0x08, 0x08));
            chip.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x7F));

            SelectedInstrument      = chip.Tag?.ToString() ?? "Guitarra eléctrica";
            BtnEnterLive.IsEnabled  = true;
            BtnEnterLive.Opacity    = 1.0;
        }

        private void BtnEnterLive_Click(object sender, RoutedEventArgs e)
            => DialogResult = true;

        private void BtnBackMode_Click(object sender, RoutedEventArgs e)
        {
            PageMode.Visibility       = Visibility.Visible;
            PageInstrument.Visibility = Visibility.Collapsed;
            BtnBackMode.Visibility    = Visibility.Collapsed;
        }
    }
}

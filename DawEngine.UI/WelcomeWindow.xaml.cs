using System.Windows;
using System.Windows.Input;

namespace DawEngine.UI
{
    public partial class WelcomeWindow : Window
    {
        // Qué plantilla eligió el usuario
        public string SelectedTemplate { get; private set; } = "Empty";
        public string? SessionToOpen   { get; private set; } = null;

        public WelcomeWindow()
        {
            InitializeComponent();
            LoadRecentProjects();
        }

        // Arrastrar ventana sin bordes
        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void BtnNewEmpty_Click(object sender, RoutedEventArgs e)
        {
            SelectedTemplate = "Empty";
            DialogResult = true;
        }

        private void BtnTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn)
                SelectedTemplate = btn.Tag?.ToString() ?? "Empty";
            DialogResult = true;
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title  = "Abrir sesión de Plastic Memory DAW",
                Filter = "Sesión DAW|*.dawsession|JSON|*.json|Todos|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                SessionToOpen    = dlg.FileName;
                SelectedTemplate = "Open";
                DialogResult = true;
            }
        }

        private void LoadRecentProjects()
        {
            // Carga el historial de sesiones recientes desde AppData
            string historyPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "PlasticMemoryDAW", "recent.txt");

            if (!System.IO.File.Exists(historyPath)) return;

            var lines = System.IO.File.ReadAllLines(historyPath);
            if (lines.Length == 0) return;

            // Limpiar placeholder
            RecentPanel.Children.Clear();

            foreach (var line in lines)
            {
                if (!System.IO.File.Exists(line)) continue;

                var info     = new System.IO.FileInfo(line);
                var itemBtn  = new System.Windows.Controls.Button { Style = (System.Windows.Style)FindResource("RecentItem") };
                var stack    = new System.Windows.Controls.StackPanel();
                stack.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text       = System.IO.Path.GetFileNameWithoutExtension(line),
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize   = 12,
                });
                stack.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text       = $"{System.IO.Path.GetDirectoryName(line)}  ·  {info.LastWriteTime:dd/MM/yyyy HH:mm}",
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44)),
                    FontSize   = 9,
                    Margin     = new System.Windows.Thickness(0, 3, 0, 0),
                });
                itemBtn.Content = stack;
                var capturedLine = line;
                itemBtn.Click += (_, _) =>
                {
                    SessionToOpen    = capturedLine;
                    SelectedTemplate = "Open";
                    DialogResult = true;
                };
                RecentPanel.Children.Add(itemBtn);
            }
        }
    }
}

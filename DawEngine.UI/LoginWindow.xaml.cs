using System.IO;
using System.Windows;
using System.Windows.Input;

namespace DawEngine.UI
{
    public class DawUser
    {
        public string Name     { get; set; } = "Invitado";
        public string Email    { get; set; } = "";
        public bool   IsGuest  { get; set; } = true;
    }

    public partial class LoginWindow : Window
    {
        public DawUser? LoggedUser { get; private set; }

        // Archivo local de usuarios (sin servidor — suficiente para tesis)
        private static readonly string UsersFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DAWY", "users.txt");

        public LoginWindow()
        {
            InitializeComponent();
            EnsureUsersFile();
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        // ── Tabs ──────────────────────────────────────────────────────────────
        private void TabLogin_Click(object sender, RoutedEventArgs e)
        {
            PanelLogin.Visibility    = Visibility.Visible;
            PanelRegister.Visibility = Visibility.Collapsed;
            TabLogin.BorderBrush     = System.Windows.Media.Brushes.Transparent;
            TabLogin.Foreground      = System.Windows.Media.Brushes.White;
            TabLogin.BorderThickness = new Thickness(0, 0, 0, 2);
            TabRegister.Foreground   = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#444"));
            TabRegister.BorderThickness = new Thickness(0, 0, 0, 1);
        }

        private void TabRegister_Click(object sender, RoutedEventArgs e)
        {
            PanelLogin.Visibility    = Visibility.Collapsed;
            PanelRegister.Visibility = Visibility.Visible;
            TabRegister.Foreground   = System.Windows.Media.Brushes.White;
            TabRegister.BorderThickness = new Thickness(0, 0, 0, 2);
            TabLogin.Foreground      = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#444"));
            TabLogin.BorderThickness = new Thickness(0, 0, 0, 1);
        }

        // ── Login ─────────────────────────────────────────────────────────────
        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string email    = TxtEmail.Text.Trim();
            string password = TxtPassword.Password;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ShowError("Completa todos los campos.");
                return;
            }

            var user = FindUser(email, password);
            if (user == null)
            {
                ShowError("Correo o contraseña incorrectos.");
                return;
            }

            LoggedUser   = user;
            DialogResult = true;
        }

        // ── Registro ──────────────────────────────────────────────────────────
        private void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            string name     = TxtName.Text.Trim();
            string email    = TxtEmailReg.Text.Trim();
            string password = TxtPasswordReg.Password;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ShowError("Completa todos los campos.");
                return;
            }
            if (password.Length < 6)
            {
                ShowError("La contraseña debe tener al menos 6 caracteres.");
                return;
            }

            SaveUser(name, email, password);
            LoggedUser   = new DawUser { Name = name, Email = email, IsGuest = false };
            DialogResult = true;
        }

        // ── Invitado ──────────────────────────────────────────────────────────
        private void BtnGuest_Click(object sender, RoutedEventArgs e)
        {
            LoggedUser   = new DawUser { Name = "Invitado", IsGuest = true };
            DialogResult = true;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void EnsureUsersFile()
        {
            string dir = Path.GetDirectoryName(UsersFile)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (!File.Exists(UsersFile)) File.WriteAllText(UsersFile, "");
        }

        private static DawUser? FindUser(string email, string password)
        {
            if (!File.Exists(UsersFile)) return null;
            foreach (var line in File.ReadAllLines(UsersFile))
            {
                var parts = line.Split('|');
                if (parts.Length < 3) continue;
                if (parts[1] == email && parts[2] == Hash(password))
                    return new DawUser { Name = parts[0], Email = parts[1], IsGuest = false };
            }
            return null;
        }

        private static void SaveUser(string name, string email, string password)
        {
            File.AppendAllText(UsersFile, $"{name}|{email}|{Hash(password)}\n");
        }

        // Hash simple (SHA256) — suficiente para una app local de tesis
        private static string Hash(string input)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return System.Convert.ToHexString(bytes);
        }

        private void ShowError(string msg)
            => MessageBox.Show(msg, "DAWY", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}

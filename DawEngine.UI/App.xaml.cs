using System.Windows;

namespace DawEngine.UI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // CRÍTICO: sin esto WPF mata la app al cerrar cualquier ventana de diálogo
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                RunFlow();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error de inicio: {ex.Message}\n\n{ex.StackTrace}",
                    "DAWY — Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void RunFlow()
        {
            // ── 1. LOGIN ──────────────────────────────────────────────────────
            var login = new LoginWindow();
            bool? loginResult = login.ShowDialog();

            if (loginResult != true)
            {
                Shutdown();
                return;
            }

            var user = login.LoggedUser ?? new DawUser { Name = "Invitado", IsGuest = true };

            // ── 2. SELECCIÓN DE MODO ──────────────────────────────────────────
            var modeWin = new ModeSelectWindow(user);
            bool? modeResult = modeWin.ShowDialog();

            if (modeResult != true)
            {
                Shutdown();
                return;
            }

            // ── 3. ABRIR EL DAW CORRECTO ──────────────────────────────────────
            Window dawWindow;

            if (modeWin.SelectedMode == DawMode.Studio)
            {
                dawWindow = new StudioWindow(user);
            }
            else
            {
                // Modo En Vivo → LiveWindow con instrumento seleccionado
                dawWindow = new LiveWindow(user, modeWin.SelectedInstrument);
            }

            // Ahora sí: cerrar cuando se cierre la ventana principal
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow   = dawWindow;
            dawWindow.Show();
        }
    }
}

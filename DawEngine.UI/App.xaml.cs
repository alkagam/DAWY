using System.Windows;

namespace DawEngine.UI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Evitar que WPF cierre la app al cerrar la ventana de bienvenida
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var welcome = new WelcomeWindow();
            bool? result = welcome.ShowDialog();

            if (result == true)
            {
                var main = new MainWindow(welcome.SelectedTemplate, welcome.SessionToOpen);
                // Ahora sí, cerramos cuando se cierre la ventana principal
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                MainWindow   = main;
                main.Show();
            }
            else
            {
                Shutdown();
            }
        }
    }
}

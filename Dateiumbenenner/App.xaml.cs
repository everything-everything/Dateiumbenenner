namespace Dateiumbenenner
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);
            // Die Erkennung nutzt weit mehr als die standardmäßig 15 zwischengespeicherten Regex-Muster – sonst werden sie ständig neu übersetzt
            System.Text.RegularExpressions.Regex.CacheSize = 512;
            ThemeManager.Initialize();
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            if (Engine.SecureStorage.IsEnabled && new PasswordDialog(PasswordDialog.Mode.Login).ShowDialog() != true)
            {
                Shutdown();
                return;
            }
            var main = new MainWindow();
            MainWindow = main;
            ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
            main.Show();
        }
    }
}

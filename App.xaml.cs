using System.Windows;

namespace AI.KB.Assistant
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var win = new Views.MainWindow();
            win.Show();
        }
    }
}

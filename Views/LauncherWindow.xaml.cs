using System.Windows;

namespace AI.KB.Assistant.Views
{
    public partial class LauncherWindow : Window
    {
        public LauncherWindow() { InitializeComponent(); }

        private MainWindow ResolveMain()
            => (this.Owner as MainWindow) ?? Application.Current.MainWindow as MainWindow ?? new MainWindow();

        // XAML 綁定事件（先做相容空殼，避免紅字；之後可接到實作）
        private void BtnAutoClassify_Click(object sender, RoutedEventArgs e)
        {
            ResolveMain().Activate();
            Close();
        }

        private void BtnOpenInbox_Click(object sender, RoutedEventArgs e)
        {
            ResolveMain().Activate();
            Close();
        }

        private void BtnOpenDetailed_Click(object sender, RoutedEventArgs e)
        {
            ResolveMain().Activate();
            Close();
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var w = new SettingsWindow { Owner = ResolveMain() };
            w.ShowDialog();
        }
    }
}

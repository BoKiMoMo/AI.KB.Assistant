using System.Windows;

namespace AI.KB.Assistant.Views
{
    public partial class HelpWindow : Window
    {
        public HelpWindow()
        {
            InitializeComponent();
        }

        private void GoSection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tag)
            {
                var target = ContentPanel.FindName(tag) as FrameworkElement;
                if (target != null)
                {
                    target.BringIntoView();
                }
            }
        }
    }
}

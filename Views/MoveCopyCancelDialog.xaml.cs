using System.Windows;

namespace AI.KB.Assistant.Views
{
    /// <summary>
    /// [V20.12] 流程優化：
    /// 取代 MessageBox.Show(YesNoCancel)，提供明確的「移動/複製/取消」選項。
    /// </summary>
    public partial class MoveCopyCancelDialog : Window
    {
        /// <summary>
        /// 回傳使用者的選擇: "Move", "Copy", or "Cancel"
        /// </summary>
        public string SelectedAction { get; private set; } = "Cancel"; // 預設為取消

        public MoveCopyCancelDialog(string prompt)
        {
            InitializeComponent();
            TxtPrompt.Text = prompt;
        }

        private void BtnMove_Click(object sender, RoutedEventArgs e)
        {
            SelectedAction = "Move";
            this.DialogResult = true;
            this.Close();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            SelectedAction = "Copy";
            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedAction = "Cancel";
            this.DialogResult = false;
            this.Close();
        }
    }
}
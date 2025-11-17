using System;
using System.Windows;

namespace AI.KB.Assistant.Views
{
    /// <summary>
    /// V20.0 [cite:"Views/InputDialog.xaml.cs"] (新增)
    /// 「重新命名」[cite:"Views/MainWindow.xaml.cs (V20.1 最終版) (line 996)"] 和「新增資料夾」[cite:"Views/MainWindow.xaml.cs (V20.1 最終版) (line 911)"] 功能所必需的輔助 UI 邏輯。
    /// 包含 3 個參數的建構函式 [cite:"Views/InputDialog.xaml.cs (V20.0 完整版) (line 17)"] 和 'InputText' [cite:"Views/InputDialog.xaml.cs (V20.0 完整版) (line 33)"] 屬性。
    /// </summary>
    public partial class InputDialog : Window
    {
        /// <summary>
        /// [V20.0] 修正 CS1729 [cite:"image_78b064.png"]：實作 3 個參數的建構函式
        /// </summary>
        public InputDialog(string title, string prompt, string defaultText = "")
        {
            InitializeComponent();
            Title = title;

            // [V20.0] 修正 CS0103 [cite:"image_78b064.png"]：存取 'PromptLabel' [cite:"Views/InputDialog.xaml (V20.0 完整版) (line 19)"]
            PromptLabel.Text = prompt;

            // [V20.0] 修正 CS0103 [cite:"image_78b064.png"]：存取 'InputTextBox' [cite:"Views/InputDialog.xaml (V20.0 完整版) (line 23)"]
            InputTextBox.Text = defaultText;
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        }

        /// <summary>
        /// [V20.0] 修正 CS1061 [cite:"image_78b064.png"]：實作 'InputText' 屬性
        /// </summary>
        public string InputText { get; private set; } = string.Empty;

        /// <summary>
        /// [V20.0] 修正 CS1061 [cite:"image_78b064.png"]：實作 'OkButton_Click' 事件處理常式
        /// </summary>
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            InputText = InputTextBox.Text;
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// [V20.0] 修正 CS1061 [cite:"image_78b064.png"]：實作 'CancelButton_Click' 事件處理常式
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
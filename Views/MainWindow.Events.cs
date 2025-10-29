using System.Windows;
using System.Windows.Controls;

namespace AI.KB.Assistant.Views
{
    // 這是補強檔：僅補齊缺少的事件，避免覆蓋你原本的 MainWindow.xaml.cs 邏輯
    public partial class MainWindow : Window
    {
        private void BtnSearchProject_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 實作「搜尋專案」行為
        }

        private void BtnLockProject_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 實作「鎖定/解除鎖定專案」行為
        }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // TODO: 分頁切換時的 UI/資料刷新
        }

        private void BtnGenTags_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 產生標籤（可呼叫 LlmService 建議）
        }

        private void BtnSummarize_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 檔案摘要（右欄）
        }

        private void BtnAnalyzeConfidence_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 信心分析（右欄）
        }
    }
}

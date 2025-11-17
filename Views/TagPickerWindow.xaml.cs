using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AI.KB.Assistant.Views
{
    /// <summary>
    /// [V20.2] 標籤選取器對話框 (修正 Null 警告)
    /// </summary>
    public partial class TagPickerWindow : Window
    {
        // 儲存所有已知的標籤，用於防止重複新增
        private readonly HashSet<string> _allTags;

        // 公開屬性，用於回傳最終選取的標籤
        public List<string> SelectedTags { get; private set; } = new List<string>();

        /// <summary>
        /// 建構函式
        /// </summary>
        /// <param name="allKnownTags">資料庫中所有已知的標籤</param>
        /// <param name="currentSelectedTags">目前選取項目已有的標籤</param>
        public TagPickerWindow(IEnumerable<string> allKnownTags, IEnumerable<string> currentSelectedTags)
        {
            InitializeComponent();

            // 確保標籤比較不區分大小寫
            _allTags = new HashSet<string>(allKnownTags, StringComparer.OrdinalIgnoreCase);
            var currentTagsSet = new HashSet<string>(currentSelectedTags, StringComparer.OrdinalIgnoreCase);

            // 確保「目前」的標籤也包含在「所有」標籤列表中
            _allTags.UnionWith(currentTagsSet);

            PopulateTags(currentTagsSet);
        }

        /// <summary>
        /// 將所有標籤填入 WrapPanel
        /// </summary>
        private void PopulateTags(HashSet<string> currentTagsSet)
        {
            TagPanel.Children.Clear();

            // 依字母順序排序
            foreach (var tag in _allTags.OrderBy(t => t))
            {
                var chk = new CheckBox
                {
                    Content = tag,
                    IsChecked = currentTagsSet.Contains(tag),
                    Style = (Style)FindResource("TagCheckBox") // 套用 XAML 中定義的樣式
                };
                TagPanel.Children.Add(chk);
            }
        }

        /// <summary>
        /// 處理「新增標籤」按鈕
        /// </summary>
        private void BtnAddNewTag_Click(object sender, RoutedEventArgs e)
        {
            var newTag = TxtNewTag.Text.Trim();

            // 檢查標籤是否有效且尚未存在
            if (!string.IsNullOrWhiteSpace(newTag) && _allTags.Add(newTag))
            {
                // 標籤成功加入 HashSet
                var chk = new CheckBox
                {
                    Content = newTag,
                    IsChecked = true, // 新增的標籤預設為勾選
                    Style = (Style)FindResource("TagCheckBox")
                };
                TagPanel.Children.Add(chk);

                TxtNewTag.Clear();
            }
            else if (!string.IsNullOrWhiteSpace(newTag))
            {
                // 標籤已存在，自動勾選它
                // [V20.2] (Fix CS8602) 使用更安全的查詢 (c.Content as string)
                var existingChk = TagPanel.Children.OfType<CheckBox>()
                    .FirstOrDefault(c => (c.Content as string)?.Equals(newTag, StringComparison.OrdinalIgnoreCase) == true);

                if (existingChk != null)
                {
                    existingChk.IsChecked = true;
                }
                TxtNewTag.Clear();
            }
        }

        /// <summary>
        /// 處理「確定」
        /// </summary>
        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // 收集所有被勾選的標籤
            // [V20.2] (Fix CS8619) 強制轉換為 (string)，因為我們確定 Content 總是 string
            SelectedTags = TagPanel.Children.OfType<CheckBox>()
                .Where(chk => chk.IsChecked == true)
                .Select(chk => (string)chk.Content)
                .ToList();

            this.DialogResult = true;
            this.Close();
        }

        /// <summary>
        /// 處理「取消」
        /// </summary>
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
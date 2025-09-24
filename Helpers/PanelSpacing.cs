using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AI.KB.Assistant.Helpers
{
    /// <summary>
    /// 在 WPF 中為 StackPanel 提供「Spacing」的附加屬性（模擬 WinUI/UWP 的 Spacing 行為）
    /// 會自動為除最後一個子元素外，設定元素間距（水平時設 Right，垂直時設 Bottom）。
    /// </summary>
    public static class PanelSpacing
    {
        public static double GetSpacing(DependencyObject obj) => (double)obj.GetValue(SpacingProperty);
        public static void SetSpacing(DependencyObject obj, double value) => obj.SetValue(SpacingProperty, value);

        public static readonly DependencyProperty SpacingProperty =
            DependencyProperty.RegisterAttached(
                "Spacing",
                typeof(double),
                typeof(PanelSpacing),
                new PropertyMetadata(0d, OnSpacingChanged));

        private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not StackPanel panel) return;

            // 先移除先前的事件，避免重複註冊
            panel.Loaded -= PanelOnLoaded;
            panel.SizeChanged -= PanelOnSizeChanged;

            if ((double)e.NewValue > 0)
            {
                panel.Loaded += PanelOnLoaded;
                panel.SizeChanged += PanelOnSizeChanged;
            }

            // 立即套用一次（若已載入）
            if (panel.IsLoaded) Apply(panel);
        }

        private static void PanelOnLoaded(object? sender, RoutedEventArgs e)
        {
            if (sender is StackPanel p) Apply(p);
        }

        private static void PanelOnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (sender is StackPanel p) Apply(p);
        }

        private static void Apply(StackPanel panel)
        {
            var spacing = GetSpacing(panel);
            var children = panel.Children.OfType<FrameworkElement>().ToList();
            if (children.Count == 0) return;

            for (int i = 0; i < children.Count; i++)
            {
                var fe = children[i];
                var m = fe.Margin;

                if (panel.Orientation == Orientation.Horizontal)
                {
                    // 水平排列：用 Right 當作間距
                    m.Right = (i == children.Count - 1) ? 0 : spacing;
                    // 不改變 Top/Bottom/Left，避免覆蓋原本 Margin
                }
                else
                {
                    // 垂直排列：用 Bottom 當作間距
                    m.Bottom = (i == children.Count - 1) ? 0 : spacing;
                }
                fe.Margin = m;
            }
        }
    }
}

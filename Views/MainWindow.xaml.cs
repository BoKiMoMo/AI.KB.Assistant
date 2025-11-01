using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;

namespace AI.KB.Assistant.Views
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<Item> FileListItems { get; } = new();
        public bool IsPreviewMode { get; private set; } = false;

        // Converters (給 XAML 用 x:Static)
        public static readonly IValueConverter FileNameConverterInstance = new FileNameConverter();
        public static readonly IValueConverter FileExtConverterInstance = new FileExtConverter();
        public static readonly IValueConverter TagCheckedConverterInstance = new TagCheckedConverter();
        public static readonly IValueConverter StatusToLabelConverterInstance = new StatusToLabelConverter();
        public static readonly IMultiValueConverter StatusToBrushConverterInstance = new StatusToBrushConverter();

        // 服務從 Application.Resources 取得（已由 App.xaml.cs 註冊）
        private T? Get<T>(string key) where T : class => Application.Current?.Resources[key] as T;
        private DbService? Db => Get<DbService>("Db");
        private IntakeService? Intake => Get<IntakeService>("Intake");
        private RoutingService? Router => Get<RoutingService>("Router");

        // 排序狀態
        private string _sortKey = "updated";
        private ListSortDirection _sortDir = ListSortDirection.Descending;

        public MainWindow()
        {
            InitializeComponent();

            // 綁定資料來源
            FileList.ItemsSource = FileListItems;
            var view = (ListCollectionView)CollectionViewSource.GetDefaultView(FileListItems);
            ApplySort(view, _sortKey, _sortDir);

            // 設定變更 → 重載/套路由
            ConfigService.ConfigChanged += (_, cfg) =>
            {
                try { Router?.ApplyConfig(cfg); } catch { }
                _ = RefreshFromDbAsync();
            };

            Loaded += async (_, __) => await RefreshFromDbAsync();
        }

        // 重新讀 DB → ObservableCollection
        private async System.Threading.Tasks.Task RefreshFromDbAsync()
        {
            try
            {
                if (Db == null) return;
                var all = await Db.ListRecentAsync(1000);
                FileListItems.Clear();
                foreach (var it in all)
                {
                    if (string.IsNullOrWhiteSpace(it.ProposedPath) && Router != null)
                        it.ProposedPath = Router.PreviewDestPath(it.Path);
                    FileListItems.Add(it);
                }

                var view = (ListCollectionView)CollectionViewSource.GetDefaultView(FileListItems);
                ApplySort(view, _sortKey, _sortDir);
                UpdatePathHeader();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "重新整理失敗");
            }
        }

        // ===== Toolbar =====
        private async void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Intake == null) { MessageBox.Show("服務尚未初始化（Intake）。"); return; }
                if (Router == null) { MessageBox.Show("服務尚未初始化（Router）。"); return; }

                var dlg = new OpenFileDialog { Title = "選擇要加入的檔案", Multiselect = true, CheckFileExists = true };
                if (dlg.ShowDialog(this) != true) return;

                var added = await Intake.IntakeFilesAsync(dlg.FileNames);

                foreach (var it in added.Where(a => a != null))
                {
                    it.ProposedPath = Router.PreviewDestPath(it.Path);
                    it.Status ??= "intaked";
                    FileListItems.Insert(0, it);
                }

                await RefreshFromDbAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "加入檔案失敗");
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await RefreshFromDbAsync();

        private async void BtnCommit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Router == null || Db == null) { MessageBox.Show("服務尚未初始化（Router / Db）。"); return; }
                var selected = GetSelectedItems();
                if (selected.Length == 0) { MessageBox.Show("請先選取要提交的項目。"); return; }

                int ok = 0;
                foreach (var it in selected)
                {
                    if (string.IsNullOrWhiteSpace(it.ProposedPath))
                        it.ProposedPath = Router.PreviewDestPath(it.Path);

                    var final = Router.Commit(it);
                    if (!string.IsNullOrWhiteSpace(final))
                    {
                        it.Status = "committed";
                        it.ProposedPath = final;
                        it.UpdatedAt = DateTime.UtcNow;
                        ok++;
                    }
                }
                if (ok > 0) await Db.UpdateItemsAsync(selected);
                await RefreshFromDbAsync();
                MessageBox.Show($"完成提交：{ok} / {selected.Length}");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "提交失敗"); }
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new SettingsWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                var ok = win.ShowDialog();
                if (ok == true) ConfigService.Load();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "開啟設定失敗"); }
        }

        // 檢視分類下拉
        private void CmbPathView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem it)
                {
                    var tag = (it.Tag as string) ?? "actual";
                    IsPreviewMode = string.Equals(tag, "pred", StringComparison.OrdinalIgnoreCase);
                    UpdatePathHeader();
                    CollectionViewSource.GetDefaultView(FileListItems)?.Refresh();
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "切換檢視失敗"); }
        }

        private void UpdatePathHeader()
        {
            if (HdrPath != null)
                HdrPath.Content = IsPreviewMode ? "預測路徑" : "路徑";
        }

        // 標籤膠囊
        private async void TagPill_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Db == null) return;
                if (sender is not ToggleButton btn) return;
                if (FindAncestor<ListViewItem>(btn) is not ListViewItem lvi) return;
                if (lvi.Content is not Item it) return;

                var token = (btn.Content as string)?.Trim() switch
                {
                    "最愛" => "favorite",
                    "代辦" => "todo",
                    "進行" => "doing",
                    _ => ""
                };
                if (string.IsNullOrWhiteSpace(token)) return;

                var tags = it.Tags ??= new List<string>();
                var has = tags.Any(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase));

                if (btn.IsChecked == true && !has) tags.Add(token);
                if (btn.IsChecked == false && has) tags.RemoveAll(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase));

                it.UpdatedAt = DateTime.UtcNow;
                await Db.UpdateItemsAsync(new[] { it });

                CollectionViewSource.GetDefaultView(FileListItems)?.Refresh();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "更新標籤失敗"); }
        }

        // 欄位排序
        private void ListHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not GridViewColumnHeader h) return;
            var key = (h.Tag as string) ?? "";
            if (string.IsNullOrEmpty(key)) return;

            _sortDir = (_sortKey == key && _sortDir == ListSortDirection.Ascending)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
            _sortKey = key;

            var view = (ListCollectionView)CollectionViewSource.GetDefaultView(FileListItems);
            ApplySort(view, _sortKey, _sortDir);
        }

        private void ApplySort(ListCollectionView view, string key, ListSortDirection dir)
        {
            if (view == null) return;
            view.CustomSort = key switch
            {
                "name" => new NameComparer(dir),
                "ext" => new CategoryComparer(dir, Router),
                "status" => new StatusComparer(dir),
                "updated" => new UpdatedComparer(dir),
                _ => new UpdatedComparer(dir)
            };
            view.Refresh();
        }

        // 雙擊開檔
        private void List_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if ((sender as ListView)?.SelectedItem is not Item it) return;
                if (File.Exists(it.Path))
                    Process.Start(new ProcessStartInfo { FileName = it.Path, UseShellExecute = true });
                else
                    MessageBox.Show("找不到檔案。");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "開啟檔案失敗"); }
        }

        // Utils
        private Item[] GetSelectedItems()
            => FileList.SelectedItems.Cast<Item>().ToArray();

        private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            var cur = start;
            while (cur != null)
            {
                if (cur is T t) return t;
                cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
            }
            return null;
        }

        // ===== Converters & Comparers =====
        private sealed class FileNameConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => value is string p ? Path.GetFileName(p) : "";
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
        }

        private sealed class FileExtConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                var ext = value is string p ? Path.GetExtension(p) : "";
                return string.IsNullOrEmpty(ext) ? "" : ext.ToLowerInvariant();
            }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
        }

        private sealed class TagCheckedConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value is not Item it) return false;
                var token = (parameter as string) ?? "";
                return it.Tags != null && it.Tags.Any(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase));
            }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
        }

        private sealed class StatusToLabelConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                var s = (value as string)?.ToLowerInvariant() ?? "";
                return s switch
                {
                    "committed" => "已提交",
                    "error" => "錯誤",
                    "" or null => "未處理",
                    "intaked" => "未處理",
                    _ when s.StartsWith("stage") => "暫存",
                    _ => s
                };
            }
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
        }

        private sealed class StatusToBrushConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                var status = (values[0] as string)?.ToLowerInvariant() ?? "";
                var win = values[1] as Window;
                Brush pick(string key) => (Brush)win!.FindResource(key);
                return status switch
                {
                    "committed" => pick("StatusBrush_Commit"),
                    "error" => pick("StatusBrush_Error"),
                    "" or null => pick("StatusBrush_Unset"),
                    "intaked" => pick("StatusBrush_Unset"),
                    _ when status.StartsWith("stage") => pick("StatusBrush_Stage"),
                    _ => pick("StatusBrush_Unset")
                };
            }
            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
                => throw new NotSupportedException();
        }

        private sealed class NameComparer : IComparer
        {
            private readonly ListSortDirection _dir;
            public NameComparer(ListSortDirection dir) => _dir = dir;
            public int Compare(object? x, object? y)
            {
                var a = Path.GetFileName((x as Item)?.Path ?? "");
                var b = Path.GetFileName((y as Item)?.Path ?? "");
                var r = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                return _dir == ListSortDirection.Ascending ? r : -r;
            }
        }

        private sealed class CategoryComparer : IComparer
        {
            private readonly ListSortDirection _dir;
            private readonly RoutingService? _router;
            public CategoryComparer(ListSortDirection dir, RoutingService? router) { _dir = dir; _router = router; }
            public int Compare(object? x, object? y)
            {
                var ix = x as Item; var iy = y as Item;
                string cat(Item? it)
                {
                    if (it == null) return "";
                    if (!string.IsNullOrWhiteSpace(it.Category)) return it.Category!.ToLowerInvariant();
                    var ext = Path.GetExtension(it.Path)?.ToLowerInvariant() ?? "";
                    try { return _router?.MapExtensionToCategory(ext) ?? ext; } catch { return ext; }
                }
                string name(Item? it) => Path.GetFileName(it?.Path ?? "");
                var cx = cat(ix); var cy = cat(iy);
                var r = string.Compare(cx, cy, StringComparison.OrdinalIgnoreCase);
                if (r == 0) r = string.Compare(name(ix), name(iy), StringComparison.OrdinalIgnoreCase);
                return _dir == ListSortDirection.Ascending ? r : -r;
            }
        }

        private sealed class StatusComparer : IComparer
        {
            private readonly ListSortDirection _dir;
            public StatusComparer(ListSortDirection dir) => _dir = dir;
            private static int Weight(string? s)
            {
                var v = (s ?? "").ToLowerInvariant();
                return v switch
                {
                    "error" => 0,
                    "" or null => 1,
                    "intaked" => 1,
                    _ when v.StartsWith("stage") => 2,
                    "committed" => 3,
                    _ => 1
                };
            }
            public int Compare(object? x, object? y)
            {
                var a = Weight((x as Item)?.Status);
                var b = Weight((y as Item)?.Status);
                var r = a.CompareTo(b);
                return _dir == ListSortDirection.Ascending ? r : -r;
            }
        }

        private sealed class UpdatedComparer : IComparer
        {
            private readonly ListSortDirection _dir;
            public UpdatedComparer(ListSortDirection dir) => _dir = dir;
            public int Compare(object? x, object? y)
            {
                var a = (x as Item)?.UpdatedAt ?? DateTime.MinValue;
                var b = (y as Item)?.UpdatedAt ?? DateTime.MinValue;
                var r = a.CompareTo(b);
                return _dir == ListSortDirection.Ascending ? r : -r;
            }
        }
    }
}

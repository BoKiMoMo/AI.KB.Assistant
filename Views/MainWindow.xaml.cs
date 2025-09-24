using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    public partial class MainWindow : Window
    {
        private readonly string _configPath = "config.json";
        private AppConfig _cfg;
        private readonly ObservableCollection<Item> _items = new();
        private readonly DbService _db;

        public MainWindow()
        {
            InitializeComponent();
            _cfg = ConfigService.Load(_configPath);
            _db = new DbService(_cfg.DbPath);
            FileList.ItemsSource = _items;

            LoadRecent();
            AppendLog("應用程式啟動完成。");
        }

        private void LoadRecent()
        {
            _items.Clear();
            foreach (var it in _db.Recent(100))
                _items.Add(it);
        }

        #region Drag & Drop
        private void Window_DragEnter(object sender, DragEventArgs e)
            => e.Effects = DragDropEffects.Copy;

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

            foreach (var f in files.Where(File.Exists))
            {
                await ProcessOneAsync(f);
            }
        }

        private async Task ProcessOneAsync(string path)
        {
            await Task.Yield();
            var fi = new FileInfo(path);

            var item = new Item
            {
                Path = fi.FullName,
                Filename = fi.Name,
                Category = "預設",
                FileType = fi.Extension.Trim('.').ToUpperInvariant(),
                Confidence = 0.8,
                CreatedTs = DateTimeOffset.Now.ToUnixTimeSeconds(),
                Status = "pending",
                Tags = ""
            };

            _db.Add(item);
            _items.Insert(0, item);
            AppendLog($"加入：{item.Filename}");
        }
        #endregion

        #region Buttons
        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            var q = (SearchBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(q)) return;

            var res = _db.Search(q).ToList();
            FileList.ItemsSource = res;
            AppendLog($"搜尋「{q}」→ {res.Count} 筆");
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            FileList.ItemsSource = _items;
            SearchBox.Text = "";
            AppendLog("清除搜尋。");
        }

        private void BtnExecuteMove_Click(object sender, RoutedEventArgs e)
        {
            foreach (var it in _items.ToList())
            {
                var target = RoutingService.GetTargetPath(_cfg, it);
                if (_cfg.DryRun)
                {
                    AppendLog($"DryRun：{it.Filename} → {target}");
                }
                else
                {
                    try
                    {
                        File.Copy(it.Path, target, overwrite: _cfg.OverwritePolicy == "replace");
                        AppendLog($"搬移：{it.Filename} → {target}");
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"搬移失敗：{ex.Message}");
                    }
                }
            }
        }

        private void BtnCancelQueue_Click(object sender, RoutedEventArgs e)
            => AppendLog("取消佇列（尚未實作）。");

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
            => AppendLog("復原尚未實作。");

        private void BtnRedo_Click(object sender, RoutedEventArgs e)
            => AppendLog("重做尚未實作。");

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow(_configPath, _cfg) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _cfg = ConfigService.Load(_configPath);
                AppendLog("設定已更新。");
            }
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new HelpWindow { Owner = this };
            dlg.ShowDialog();
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
            => TxtLog.Clear();
        #endregion

        #region FileList
        private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = FileList.SelectedItem as Item;
            SelectionInfo.Text = item != null ? $"已選：{item.Filename}" : "";
            ShowPreview(item);
        }

        private void ShowPreview(Item? it)
        {
            PreviewHeader.Text = it?.Filename ?? "(未選取)";
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewText.Visibility = Visibility.Collapsed;

            if (it == null || !File.Exists(it.Path)) return;

            var ext = (Path.GetExtension(it.Path) ?? "").ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif")
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(it.Path);
                    bmp.DecodePixelWidth = 320;
                    bmp.EndInit();
                    PreviewImage.Source = bmp;
                    PreviewImage.Visibility = Visibility.Visible;
                }
                catch { }
            }
            else if (ext is ".txt" or ".md" or ".csv")
            {
                try
                {
                    var text = File.ReadAllText(it.Path);
                    PreviewText.Text = text.Length > 5000 ? text[..5000] + "\r\n..." : text;
                    PreviewText.Visibility = Visibility.Visible;
                }
                catch { }
            }
        }
        #endregion

        #region PathTree
        private void PathTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
            => AppendLog("路徑樹選取改變。");

        private void PathNode_Checked(object sender, RoutedEventArgs e)
            => AppendLog("路徑勾選。");

        private void PathNode_Unchecked(object sender, RoutedEventArgs e)
            => AppendLog("路徑取消勾選。");
        #endregion

        #region Log
        private void AppendLog(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
            TxtLog.ScrollToEnd();
        }
        #endregion
    }
}

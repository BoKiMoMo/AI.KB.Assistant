using System;
using System.IO;
using System.ComponentModel;
using AI.KB.Assistant.Models;
using System.Collections.Generic;
using System.Windows.Media; // [V19.0] 
using AI.KB.Assistant.Common; // [V19.0] 
using AI.KB.Assistant.Services; // [V20.0] 
using System.Windows; // [V20.0] 

namespace AI.KB.Assistant.Views
{
    /// <summary>
    /// V20.0 (功能實作版)
    /// 1. (V19.0) 包含 P1/P2/P3 修正。
    /// 2. [V20.0] 新增 `Category` 屬性 [cite:"Models/UiRow.cs (V20.0 功能完整版) (line 110)"]，以滿足測試清單 [cite:"V19_1_Final_Test_Plan.md"] 需求。
    /// 3. [V20.0] 修改建構函式 `UiRow(Item it)` [cite:"Models/UiRow.cs (V20.0 功能完整版) (line 27)"] 為 `UiRow(Item it, string category)` [cite:"Models/UiRow.cs (V20.0 功能完整版) (line 27)"]。
    /// </summary>
    public sealed class UiRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public Item Item { get; }

        /// <summary>
        /// [V20.0] 建構函式更新
        /// </summary>
        public UiRow(Item it, string category)
        {
            Item = it;

            FileName = Path.GetFileName(it.Path ?? string.Empty);
            Ext = (Path.GetExtension(it.Path ?? string.Empty) ?? "").Trim('.').ToLowerInvariant();

            // [V19.0 P3]
            FileIcon = IconHelper.GetIcon(it.Path);

            // [V20.0] 從 MainWindow [cite:"Views/MainWindow.xaml.cs (V20.0 功能完整版) (line 209)"] 接收計算好的類別
            Category = category;

            _project = it.Project ?? "";
            _tags = it.Tags == null ? "" : string.Join(",", it.Tags);
            SourcePath = it.Path ?? "";
            _destPath = it.ProposedPath ?? "";
            CreatedAt = it.Timestamp ?? it.UpdatedAt;
            _status = string.IsNullOrWhiteSpace(it.Status) ? "intaked" : it.Status!;
        }

        public string FileName { get; }
        public string Ext { get; }

        private string _project;
        public string Project
        {
            get => _project;
            set { _project = value; OnPropertyChanged(nameof(Project)); }
        }

        private string _tags;
        public string Tags
        {
            get => _tags;
            set { _tags = value; OnPropertyChanged(nameof(Tags)); }
        }

        public string SourcePath { get; }

        private string _destPath;
        public string DestPath
        {
            get => _destPath;
            set { _destPath = value; OnPropertyChanged(nameof(DestPath)); }
        }

        public DateTime CreatedAt { get; }

        private string _status;
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); }
        }

        /// <summary>
        /// [V19.0 P3]
        /// </summary>
        public ImageSource? FileIcon { get; }

        /// <summary>
        /// [V20.0 新增] (滿足測試清單 [cite:"V19_1_Final_Test_Plan.md"] 需求)
        /// </summary>
        public string Category { get; }
    }
}
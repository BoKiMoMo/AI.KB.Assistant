using System;
using System.IO;
using System.ComponentModel;
using AI.KB.Assistant.Models;
using System.Collections.Generic;
using System.Windows.Media; // [V19.0 新增 P3] 
using AI.KB.Assistant.Common; // [V19.0 新增 P3] 

namespace AI.KB.Assistant.Views
{
    /// <summary>
    /// V19.0 (V18.0 回滾 P2 + V19.0 P3)
    /// 1. (V9.1) 修正 CS8618
    /// 2. [V19.0 回滾 P2] 移除 V18.0 [cite: `Models/UiRow.cs (V18.0)`] 的 'IsFolder' [cite: `Models/UiRow.cs (V18.0)` Line 96] 屬性 (V17.1 P2 需求)。
    /// 3. [V19.0 回滾 P2] 'Status' [Line 51] 回滾 V18.0 [cite: `Models/UiRow.cs (V18.0)` Line 51] "Folder" 邏輯。
    /// 4. [V19.0 回滾 P2] 'Ext' [Line 57] 回滾 V18.0 [cite: `Models/UiRow.cs (V18.0)` Line 57] "Folder" 邏輯。
    /// 5. [V19.0 新增 P3] 新增 'FileIcon' [Line 103] 屬性 (V18.1 P3 需求)，並在建構函式 [Line 32] 中呼叫 V19.0 [cite: `Common/IconHelper.cs (V19.0)`] 'IconHelper.GetIcon()'。
    /// </summary>
    public sealed class UiRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public Item Item { get; }

        public UiRow(Item it)
        {
            Item = it;

            // [V19.0 回滾 P2] 移除 V18.0 [cite: `Models/UiRow.cs (V18.0)` Line 29] 'IsFolder'

            FileName = Path.GetFileName(it.Path ?? string.Empty);

            // [V19.0 回滾 P2] V18.0 [cite: `Models/UiRow.cs (V18.0)` Line 32] 'Ext'
            Ext = (Path.GetExtension(it.Path ?? string.Empty) ?? "").Trim('.').ToLowerInvariant();

            // [V19.0 新增 P3] 呼叫 V19.0 [cite: `Common/IconHelper.cs (V19.0)`] IconHelper [cite: `Common/IconHelper.cs (V19.0)`]
            FileIcon = IconHelper.GetIcon(it.Path);

            // (V9.1)
            _project = it.Project ?? "";
            _tags = it.Tags == null ? "" : string.Join(",", it.Tags);

            SourcePath = it.Path ?? "";
            _destPath = it.ProposedPath ?? "";
            CreatedAt = it.Timestamp ?? it.UpdatedAt;

            // [V19.0 回滾 P2] V18.0 [cite: `Models/UiRow.cs (V18.0)` Line 51] 'Status'
            _status = string.IsNullOrWhiteSpace(it.Status) ? "intaked" : it.Status!;
        }

        public string FileName { get; }
        public string Ext { get; }

        // (V9.1)
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

        // [V19.0 回滾 P2] 移除 V18.0 [cite: `Models/UiRow.cs (V18.0)` Line 96] 'IsFolder'
        // public bool IsFolder { get; }

        /// <summary>
        /// [V19.0 新增 P3] (V18.1 P3 需求)
        /// </summary>
        public ImageSource? FileIcon { get; }
    }
}
using System;
using System.IO;
using System.ComponentModel;
using AI.KB.Assistant.Models;
using System.Collections.Generic; // V7.33.1 修正：確保 List<string> 被引用

namespace AI.KB.Assistant.Views
{
    /// <summary>
    /// V7.5 重構：
    /// 用於 ListView 資料繫結的 UI 資料模型。
    /// (注意：保持在 Views 命名空間以便 XAML 存取，或稍後調整 XAML)
    /// </summary>
    public sealed class UiRow : INotifyPropertyChanged
    {
        // V7.5 修正：為了讓 DestPath 和 Status 在 Commit 後能更新 UI，
        // 我們需要實現 INotifyPropertyChanged。
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public Item Item { get; }

        public UiRow(Item it)
        {
            Item = it;
            FileName = Path.GetFileName(it.Path ?? string.Empty);
            Ext = (Path.GetExtension(it.Path ?? string.Empty) ?? "").Trim('.').ToLowerInvariant();

            // V7.33.3 修正：確保 Project 和 Tags 被正確初始化 (解決 CS8618)
            Project = it.Project ?? "";
            Tags = it.Tags == null ? "" : string.Join(",", it.Tags);

            SourcePath = it.Path ?? "";
            _destPath = it.ProposedPath ?? ""; // 使用後備欄位
            CreatedAt = it.Timestamp ?? it.UpdatedAt;
            _status = string.IsNullOrWhiteSpace(it.Status) ? "intaked" : it.Status!; // 使用後備欄位
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
        /// <summary>
        /// V7.33 註：此欄位在 UI (XAML) 上已加回，
        /// 並且在後端邏輯 (ApplyListFilters, ModifyTagsAsync) 中仍在使用。
        /// </summary>
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
    }
}
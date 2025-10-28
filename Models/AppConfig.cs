using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace AI.KB.Assistant.Models
{
    // ======================== ENUM ========================
    public enum MoveMode { Move, Copy }
    public enum OverwritePolicy { Replace, Rename, Skip }
    public enum StartupMode { Simple = 0, Detailed = 1 }

    // ======================== 主設定 ========================
    public sealed class AppConfig
    {
        public AppSection App { get; set; } = new();
        public ImportSection Import { get; set; } = new();
        public ClassificationSection Classification { get; set; } = new();
        public ThemeSection ThemeColors { get; set; } = new();

        // 為相容新版結構
        public RoutingSection? Routing { get; set; }
        public DatabaseSection? Database { get; set; }
    }

    // ======================== 各區段 ========================

    public sealed class AppSection
    {
        public string? RootDir { get; set; }
        public string? DbPath { get; set; }
        public StartupMode? StartupUIMode { get; set; } = StartupMode.Simple;
        public string? ProjectLock { get; set; }
    }

    public sealed class ImportSection
    {
        public string? HotFolderPath { get; set; }
        public bool IncludeSubdir { get; set; } = true;

        public MoveMode MoveMode { get; set; } = MoveMode.Move;

        [JsonIgnore]
        public bool CopyInsteadOfMove
        {
            get => MoveMode == MoveMode.Copy;
            set => MoveMode = value ? MoveMode.Copy : MoveMode.Move;
        }

        public OverwritePolicy OverwritePolicy { get; set; } = OverwritePolicy.Rename;
        public string[] BlacklistExts { get; set; } = Array.Empty<string>();
        public string[] BlacklistFolderNames { get; set; } = Array.Empty<string>();
        public Dictionary<string, string[]> ExtGroups { get; set; } = new();
        [JsonIgnore] public Dictionary<string, string[]> ExtGroupsCache { get; set; } = new();

        public void RebuildExtGroupsCache()
        {
            ExtGroupsCache.Clear();
            foreach (var kv in ExtGroups)
            {
                if (kv.Value == null || kv.Value.Length == 0) continue;
                ExtGroupsCache[kv.Key] = kv.Value
                    .Select(v => v.Trim().TrimStart('.').ToLowerInvariant())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct().ToArray();
            }
        }
    }

    public sealed class ClassificationSection
    {
        public double ConfidenceThreshold { get; set; } = 0.75;
    }

    public sealed class ThemeSection
    {
        public string Background { get; set; } = "#FFFFFF";
        public string Panel { get; set; } = "#F7F7F7";
        public string Border { get; set; } = "#D0D0D0";
        public string Text { get; set; } = "#1F1F1F";
        public string TextMuted { get; set; } = "#6F6F6F";
        public string Primary { get; set; } = "#106EBE";
        public string PrimaryHover { get; set; } = "#2B7FD0";
        public string Success { get; set; } = "#4CAF50";
        public string Warning { get; set; } = "#FFB300";
        public string Error { get; set; } = "#E74C3C";
        public string BannerInfo { get; set; } = "#007ACC";
        public string BannerWarn { get; set; } = "#FF9800";
        public string BannerError { get; set; } = "#D32F2F";
    }

    // 新版可選區段
    public sealed class RoutingSection
    {
        public string? RootDir { get; set; }
        public string AutoFolderName { get; set; } = "自整理";
        public string LowConfidenceFolderName { get; set; } = "信心不足";
    }

    public sealed class DatabaseSection
    {
        public string? FilePath { get; set; }
    }
}

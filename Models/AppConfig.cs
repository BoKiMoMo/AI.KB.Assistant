using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI.KB.Assistant.Models
{
    // V13.0 (方案 C)
    // 1. (V9.7) 使用 string OverwritePolicy 和 JSON Clone()
    // 2. [V13.0] 移除 ShowDesktopInTree/ShowDrivesInTree
    // 3. [V13.0] 新增 TreeViewRootPaths (自訂路徑清單)

    public class AppConfig
    {
        [JsonPropertyName("app")]
        public AppSection App { get; set; } = new();

        [JsonPropertyName("db")]
        public DbSection Db { get; set; } = new();

        [JsonPropertyName("import")]
        public ImportSection Import { get; set; } = new();

        [JsonPropertyName("routing")]
        public RoutingSection Routing { get; set; } = new();

        [JsonPropertyName("openAI")]
        public OpenAISection OpenAI { get; set; } = new();

        /// <summary>
        /// (V9.7) 使用 JSON 序列化來深層複製
        /// </summary>
        public AppConfig Clone()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };
            var json = JsonSerializer.Serialize(this, options);
            return JsonSerializer.Deserialize<AppConfig>(json, options) ?? new AppConfig();
        }
    }

    public class AppSection
    {
        [JsonPropertyName("rootDir")]
        public string RootDir { get; set; } = "";

        [JsonPropertyName("dbPath")]
        public string DbPath { get; set; } = "";

        [JsonPropertyName("launchMode")]
        public string LaunchMode { get; set; } = "Detailed";

        // [V13.0 移除] 移除 V12.0 的 ShowDesktopInTree
        // [V13.0 移除] 移除 V12.0 的 ShowDrivesInTree

        // [V13.0 新增] 自訂檔案樹根目錄
        [JsonPropertyName("treeViewRootPaths")]
        public List<string> TreeViewRootPaths { get; set; } = new();
    }

    public class DbSection
    {
        // ... (現有的 DbSection 內容，無變更) ...
        [JsonPropertyName("dbPath")]
        public string DbPath { get; set; } = "";

        [JsonPropertyName("path")]
        public string Path { get; set; } = "";
    }

    public class ImportSection
    {
        // ... (現有的 ImportSection 內容，無變更) ...
        [JsonPropertyName("hotFolder")]
        public string HotFolder { get; set; } = "";

        [JsonPropertyName("includeSubdir")]
        public bool IncludeSubdir { get; set; } = true;

        [JsonPropertyName("enableHotFolder")]
        public bool EnableHotFolder { get; set; } = true;

        [JsonPropertyName("moveMode")]
        public string MoveMode { get; set; } = "copy";

        // (V9.7) 還原為 string
        [JsonPropertyName("overwritePolicy")]
        public string OverwritePolicy { get; set; } = "KeepBoth";

        [JsonPropertyName("blacklistExts")]
        public List<string> BlacklistExts { get; set; } = new();

        [JsonPropertyName("blacklistFolderNames")]
        public List<string> BlacklistFolderNames { get; set; } = new();
    }

    public class RoutingSection
    {
        // ... (現有的 RoutingSection 內容，無變更) ...
        // (V9.7) 滿足 MainWindow/RoutingService 備援
        [JsonPropertyName("rootDir")]
        public string RootDir { get; set; } = "";

        [JsonPropertyName("useYear")]
        public bool UseYear { get; set; } = true;

        [JsonPropertyName("useMonth")]
        public bool UseMonth { get; set; } = true;

        [JsonPropertyName("useProject")]
        public bool UseProject { get; set; } = true;

        [JsonPropertyName("useCategory")]
        public bool UseCategory { get; set; } = false;

        [JsonPropertyName("folderOrder")]
        public List<string> FolderOrder { get; set; } = new() { "year", "month", "project", "category" };

        [JsonPropertyName("useType")]
        public string UseType { get; set; } = "rule+llm";

        [JsonPropertyName("lowConfidenceFolderName")]
        public string LowConfidenceFolderName { get; set; } = "_pending";

        [JsonPropertyName("threshold")]
        public double Threshold { get; set; } = 0.75;

        [JsonPropertyName("autoFolderName")]
        public string AutoFolderName { get; set; } = "自整理";

        [JsonPropertyName("extensionGroups")]
        public Dictionary<string, List<string>> ExtensionGroups { get; set; } = new();

        // V7.34 (C#) 黑名單位置 (由 ConfigService V9.1/V9.6 映射)
        [JsonPropertyName("blacklistExts")]
        public List<string> BlacklistExts { get; set; } = new();

        [JsonPropertyName("blacklistFolderNames")]
        public List<string> BlacklistFolderNames { get; set; } = new();
    }

    public class OpenAISection
    {
        // ... (現有的 OpenAISection 內容，無變更) ...
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("model")]
        public string Model { get; set; } = "gpt-4o-mini";
    }
}
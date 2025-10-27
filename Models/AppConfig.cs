using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// App 的整體設定模型（可 JSON 序列化）
    /// </summary>
    public class AppConfig
    {
        public AppSection App { get; set; } = new();
        public ImportSection Import { get; set; } = new();
        public RoutingSection Routing { get; set; } = new();
        public ClassificationSection Classification { get; set; } = new();
        public OpenAISection OpenAI { get; set; } = new();

        /// <summary>（選用）其他 UI/Theme 相關設定若需可擴充</summary>
        public ThemeSection Theme { get; set; } = new();
    }

    public class AppSection
    {
        /// <summary>SQLite 資料庫路徑（*.db 或 *.sqlite）</summary>
        public string DbPath { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.db");

        /// <summary>檔案分類的根目錄（最終目的地樹）</summary>
        public string RootDir { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        /// <summary>專案鎖定（若非空則優先作為 Project 片段）</summary>
        public string ProjectLock { get; set; } = string.Empty;
    }

    public class ImportSection
    {
        /// <summary>收件夾（待處理）路徑</summary>
        public string HotFolderPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Inbox");

        /// <summary>加入收件夾時是否包含子資料夾（右鍵整夾加入時會用到）</summary>
        public bool IncludeSubdir { get; set; } = true;

        /// <summary>黑名單資料夾名稱（樹/清單過濾時用）</summary>
        public string[] BlacklistFolderNames { get; set; } = Array.Empty<string>();

        /// <summary>黑名單副檔名（加入/預處理時排除，不含點）</summary>
        public string[] BlacklistExts { get; set; } = new[] { "tmp", "bak", "log" };

        /// <summary>搬移模式（Move 或 Copy）</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MoveMode MoveMode { get; set; } = MoveMode.Move;

        /// <summary>檔名衝突策略</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public OverwritePolicy OverwritePolicy { get; set; } = OverwritePolicy.Rename;

        /// <summary>
        /// 副檔名群組設定（JSON 字串）。示例：
        /// {
        ///   "文件": ["pdf","doc","docx","txt","md"],
        ///   "影像": ["jpg","jpeg","png","gif","bmp"],
        ///   "表格": ["xls","xlsx","csv"],
        ///   "簡報": ["ppt","pptx"],
        ///   "壓縮": ["zip","rar","7z"],
        ///   "程式": ["cs","py","js","ts","java","cpp","h","csproj","sln"]
        /// }
        /// </summary>
        public string ExtGroupsJson { get; set; } =
            "{\"文件\":[\"pdf\",\"doc\",\"docx\",\"txt\",\"md\"],\"影像\":[\"jpg\",\"jpeg\",\"png\",\"gif\",\"bmp\"],\"表格\":[\"xls\",\"xlsx\",\"csv\"],\"簡報\":[\"ppt\",\"pptx\"],\"壓縮\":[\"zip\",\"rar\",\"7z\"],\"程式\":[\"cs\",\"py\",\"js\",\"ts\",\"java\",\"cpp\",\"h\",\"csproj\",\"sln\"]}";

        /// <summary>快取解析後的副檔名群組（執行時建構）</summary>
        [JsonIgnore] public Dictionary<string, HashSet<string>> ExtGroupMap { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>解析 ExtGroupsJson 成快取供 Routing 使用</summary>
        public void RebuildExtGroupsCache()
        {
            ExtGroupMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(ExtGroupsJson)) return;

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string[]>>(ExtGroupsJson) ?? new();
                foreach (var kv in dict)
                {
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var ext in kv.Value ?? Array.Empty<string>())
                    {
                        var clean = (ext ?? "").Trim().TrimStart('.');
                        if (!string.IsNullOrWhiteSpace(clean)) set.Add(clean);
                    }
                    if (set.Count > 0) ExtGroupMap[kv.Key] = set;
                }
            }
            catch
            {
                // 解析失敗時保持空字典（不拋例外，以免阻斷主流程）
                ExtGroupMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public class RoutingSection
    {
        /// <summary>低信心的暫存資料夾名稱（預設「信心不足」）</summary>
        public string LowConfidenceFolderName { get; set; } = "信心不足";

        /// <summary>一般自動整理的資料夾名稱（預設「自整理」）</summary>
        public string AutoFolderName { get; set; } = "自整理";

        /// <summary>是否啟用年（YYYY）片段</summary>
        public bool UseYear { get; set; } = true;

        /// <summary>是否啟用月（MM）片段</summary>
        public bool UseMonth { get; set; } = true;

        /// <summary>是否啟用專案（Project）片段</summary>
        public bool UseProject { get; set; } = true;

        /// <summary>是否啟用類型群組（由副檔名映射）片段</summary>
        public bool UseType { get; set; } = true;
    }

    public class ClassificationSection
    {
        /// <summary>信心度門檻（低於此值可觸發 LLM 建議）</summary>
        public double ConfidenceThreshold { get; set; } = 0.75;
    }

    public class OpenAISection
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gpt-4o-mini";
        /// <summary>當分類信心低於門檻時啟用 LLM 建議</summary>
        public bool EnableWhenLowConfidence { get; set; } = true;
    }

    public class ThemeSection
    {
        // 保留擴充點，實際顏色交由 Theme.xaml 管理
        public string Accent { get; set; } = "#4F46E5";
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MoveMode { Move, Copy }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OverwritePolicy { Replace, Rename, Skip }
}

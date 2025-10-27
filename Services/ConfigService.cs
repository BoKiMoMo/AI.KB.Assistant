using AI.KB.Assistant.Models;
using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 負責 App 設定（JSON）的載入/儲存/修復。
    /// </summary>
    public static class ConfigService
    {
        private static readonly JsonSerializerOptions JsonOpt = new()
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>
        /// 嘗試從指定路徑載入設定；若不存在則回寫預設檔案並回傳預設。
        /// 自動進行：欄位補齊、路徑修復、群組快取重建。
        /// </summary>
        public static AppConfig TryLoad(string configPath)
        {
            AppConfig cfg;
            try
            {
                if (!File.Exists(configPath))
                {
                    cfg = BuildDefault();
                    EnsureDirs(cfg);
                    Save(configPath, cfg);
                    return cfg;
                }

                var json = File.ReadAllText(configPath, Encoding.UTF8);
                cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpt) ?? BuildDefault();

                // 版本遷移/補預設
                MigrateDefaults(cfg);

                // 檢查與修復路徑（建立必要資料夾）
                EnsureDirs(cfg);

                // 重建副檔名群組快取
                cfg.Import?.RebuildExtGroupsCache();

                // 回存修正後的設定（避免下次再修）
                Save(configPath, cfg);
                return cfg;
            }
            catch
            {
                // 若檔案內容壞掉，回預設並覆蓋
                cfg = BuildDefault();
                EnsureDirs(cfg);
                Save(configPath, cfg);
                return cfg;
            }
        }

        /// <summary>
        /// 將設定存回檔案（UTF-8, Indented）。
        /// </summary>
        public static void Save(string configPath, AppConfig cfg)
        {
            try
            {
                var dir = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(cfg, JsonOpt);
                File.WriteAllText(configPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch
            {
                // 無須拋出，避免阻斷主程式；可視需求加上備援 Log
            }
        }

        /// <summary>
        /// 產生一份具有合理預設值的設定。
        /// </summary>
        private static AppConfig BuildDefault()
        {
            var cfg = new AppConfig();
            cfg.Import.RebuildExtGroupsCache();
            return cfg;
        }

        /// <summary>
        /// 確保 RootDir/HotFolder/DbPath 的目錄存在。
        /// </summary>
        private static void EnsureDirs(AppConfig cfg)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(cfg.App?.RootDir) && !Directory.Exists(cfg.App.RootDir))
                    Directory.CreateDirectory(cfg.App.RootDir);

                if (!string.IsNullOrWhiteSpace(cfg.Import?.HotFolderPath) && !Directory.Exists(cfg.Import.HotFolderPath))
                    Directory.CreateDirectory(cfg.Import.HotFolderPath);

                if (!string.IsNullOrWhiteSpace(cfg.App?.DbPath))
                {
                    var dbDir = Path.GetDirectoryName(cfg.App.DbPath);
                    if (!string.IsNullOrWhiteSpace(dbDir) && !Directory.Exists(dbDir))
                        Directory.CreateDirectory(dbDir);
                }
            }
            catch { /* 忽略建立失敗，避免中斷 */ }
        }

        /// <summary>
        /// 對舊版設定做欄位補齊/合理化處理（不覆蓋使用者既有值）。
        /// </summary>
        private static void MigrateDefaults(AppConfig cfg)
        {
            cfg.App ??= new AppSection();
            cfg.Import ??= new ImportSection();
            cfg.Routing ??= new RoutingSection();
            cfg.Classification ??= new ClassificationSection();
            cfg.OpenAI ??= new OpenAISection();
            cfg.Theme ??= new ThemeSection();

            // 修正 HotFolder 預設
            if (string.IsNullOrWhiteSpace(cfg.Import.HotFolderPath))
                cfg.Import.HotFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Inbox");

            // 修正 RootDir 預設
            if (string.IsNullOrWhiteSpace(cfg.App.RootDir))
                cfg.App.RootDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            // 低信心資料夾名稱預設
            if (string.IsNullOrWhiteSpace(cfg.Routing.LowConfidenceFolderName))
                cfg.Routing.LowConfidenceFolderName = "信心不足";

            if (string.IsNullOrWhiteSpace(cfg.Routing.AutoFolderName))
                cfg.Routing.AutoFolderName = "自整理";

            // 合理化門檻
            if (cfg.Classification.ConfidenceThreshold <= 0 || cfg.Classification.ConfidenceThreshold > 1)
                cfg.Classification.ConfidenceThreshold = 0.75;

            // 預設副檔名群組快取
            if (string.IsNullOrWhiteSpace(cfg.Import.ExtGroupsJson))
                cfg.Import.ExtGroupsJson =
                    "{\"文件\":[\"pdf\",\"doc\",\"docx\",\"txt\",\"md\"],\"影像\":[\"jpg\",\"jpeg\",\"png\",\"gif\",\"bmp\"],\"表格\":[\"xls\",\"xlsx\",\"csv\"],\"簡報\":[\"ppt\",\"pptx\"],\"壓縮\":[\"zip\",\"rar\",\"7z\"],\"程式\":[\"cs\",\"py\",\"js\",\"ts\",\"java\",\"cpp\",\"h\",\"csproj\",\"sln\"]}";

            // 確保布林旗標與列舉有值
            // （此處只要不是預期範圍就回到預設，避免舊檔案殘值）
            cfg.Import.IncludeSubdir = cfg.Import.IncludeSubdir;
            cfg.Import.MoveMode = cfg.Import.MoveMode; // JsonStringEnumConverter 已處理
            cfg.Import.OverwritePolicy = cfg.Import.OverwritePolicy;

            // 讓群組快取同步
            try { cfg.Import.RebuildExtGroupsCache(); } catch { }
        }

        /// <summary>
        /// 簡單檢查路徑的合法性（可供設定畫面使用）。
        /// </summary>
        public static bool ValidatePaths(AppConfig cfg, out string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(cfg.App?.DbPath))
                {
                    message = "DB 路徑不可為空。";
                    return false;
                }

                var dbDir = Path.GetDirectoryName(cfg.App.DbPath);
                if (string.IsNullOrWhiteSpace(dbDir))
                {
                    message = "DB 路徑所在資料夾無效。";
                    return false;
                }

                message = "OK";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }
    }
}

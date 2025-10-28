using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 熱資料夾監聽（可選）。目前不再依賴 AppConfig.Import.EnableHotFolder，
    /// 一律以外部呼叫 Start/Stop 控制，避免舊屬性造成編譯錯誤。
    /// </summary>
    public sealed class HotFolderService : IDisposable
    {
        private readonly IntakeService _intake;
        private readonly AppConfig _cfg;
        private FileSystemWatcher? _fw;

        public HotFolderService(IntakeService intake, AppConfig cfg)
        {
            _intake = intake;
            _cfg = cfg;
        }

        public void Start()
        {
            Stop();
            var hot = _cfg.Import?.HotFolderPath;
            if (string.IsNullOrWhiteSpace(hot) || !Directory.Exists(hot)) return;

            _fw = new FileSystemWatcher(hot)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };
            _fw.Created += OnCreated;
            _fw.Renamed += OnCreated;
            _fw.EnableRaisingEvents = true;
        }

        public void Stop()
        {
            try
            {
                if (_fw != null)
                {
                    _fw.EnableRaisingEvents = false;
                    _fw.Created -= OnCreated;
                    _fw.Renamed -= OnCreated;
                    _fw.Dispose();
                }
            }
            finally { _fw = null; }
        }

        private async void OnCreated(object sender, FileSystemEventArgs e)
        {
            // 等檔案寫入完成（簡單延遲）
            await Task.Delay(300);
            try
            {
                if (File.Exists(e.FullPath))
                    await _intake.StageOnlyAsync(e.FullPath, CancellationToken.None);
            }
            catch { /* 忽略單檔錯誤 */ }
        }

        public void Dispose() => Stop();
    }
}

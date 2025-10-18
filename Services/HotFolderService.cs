using AI.KB.Assistant.Models;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AI.KB.Assistant.Services
{
    public sealed class HotFolderService : IDisposable
    {
        private readonly IntakeService _intake;
        private readonly AppConfig _cfg;
        private FileSystemWatcher? _w;

        public HotFolderService(IntakeService intake, AppConfig cfg)
        {
            _intake = intake;
            _cfg = cfg;
        }

        public void Start()
        {
            if (!_cfg.Import.EnableHotFolder) return;

            var root = string.IsNullOrWhiteSpace(_cfg.Import.HotFolderPath)
                ? Path.Combine(_cfg.App.RootDir ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "_Inbox")
                : _cfg.Import.HotFolderPath!;

            if (!Directory.Exists(root)) Directory.CreateDirectory(root);

            _w = new FileSystemWatcher(root)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                IncludeSubdirectories = _cfg.Import.IncludeSubdirectories,
                Filter = "*.*",
                EnableRaisingEvents = true
            };

            _w.Created += async (_, e) =>
            {
                try
                {
                    // 忽略黑名單資料夾
                    if (!string.IsNullOrWhiteSpace(_cfg.Import.BlacklistFolderName) &&
                        e.FullPath.Contains(Path.DirectorySeparatorChar + _cfg.Import.BlacklistFolderName + Path.DirectorySeparatorChar,
                                            StringComparison.OrdinalIgnoreCase))
                        return;

                    // 等檔案釋放
                    await Task.Delay(200);

                    if (_cfg.Import.AutoClassifyOnDrop)
                        await _intake.ClassifyOnlyAsync(e.FullPath, CancellationToken.None);
                    else
                        await _intake.StageOnlyAsync(e.FullPath, CancellationToken.None);
                }
                catch { /* ignore */ }
            };
        }

        public void Dispose()
        {
            try { if (_w != null) { _w.EnableRaisingEvents = false; _w.Dispose(); } } catch { }
        }
    }
}

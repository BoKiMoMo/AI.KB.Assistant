using System;
using System.IO;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public class RoutingService
    {
        private readonly AppConfig _cfg;
        public RoutingService(AppConfig cfg) => _cfg = cfg;

        public string ResolveTargetPath(Item it)
        {
            var year = _cfg.AddYearFolder ? (it.Year > 0 ? it.Year : DateTime.Now.Year) : (int?)null;
            var segs = year is null
                ? new[] { _cfg.RootPath, _cfg.Project, it.Category, it.FileType }
                : new[] { _cfg.RootPath, year!.Value.ToString(), _cfg.Project, it.Category, it.FileType };

            return Path.Combine(segs);
        }

        public async Task<string> RouteAsync(Item it)
        {
            var targetDir = ResolveTargetPath(it);
            Directory.CreateDirectory(targetDir);

            var dst = Path.Combine(targetDir, it.Filename);
            if (File.Exists(dst)) // Â²³æ¥h­«
            {
                var name = Path.GetFileNameWithoutExtension(dst);
                var ext = Path.GetExtension(dst);
                dst = Path.Combine(targetDir, $"{name}_{DateTime.Now:HHmmss}{ext}");
            }

            if (string.Equals(_cfg.RoutingMode, "Move", StringComparison.OrdinalIgnoreCase))
                File.Move(it.FullPath, dst);
            else
                File.Copy(it.FullPath, dst, overwrite: false);

            await Task.CompletedTask;
            return dst;
        }
    }
}

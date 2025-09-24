using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public class ConfigService
    {
        public AppConfig Current { get; private set; } = new();
        private readonly string _configPath;

        public ConfigService(string configPath)
        {
            _configPath = configPath;
            Current = AppConfig.Load(_configPath);
        }

        public void Save() => Current.Save(_configPath);
    }
}

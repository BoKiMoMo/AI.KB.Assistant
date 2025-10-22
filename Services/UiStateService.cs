using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AI.KB.Assistant.Services
{
    public sealed class UiState
    {
        public bool LeftCollapsed { get; set; }
        public bool RightCollapsed { get; set; }
        public double LeftWidth { get; set; } = 280;
        public double RightWidth { get; set; } = 360;
        public string? LastFolder { get; set; }
        public Dictionary<string, double>? ColumnWidths { get; set; } = new();
        public double LogHeight { get; set; } = 110;
        public bool LogExpanded { get; set; } = false;
    }

    public static class UiStateService
    {
        private static string StatePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-state.json");

        public static UiState Load()
        {
            try
            {
                if (File.Exists(StatePath))
                {
                    var json = File.ReadAllText(StatePath);
                    return JsonSerializer.Deserialize<UiState>(json) ?? new UiState();
                }
            }
            catch { }
            return new UiState();
        }

        public static void Save(UiState state)
        {
            try
            {
                var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(StatePath, json);
            }
            catch { }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AI.KB.Assistant.Common
{
    /// <summary>
    /// V7.5 重構：
    /// 包含處理外部程序 (如 explorer.exe) 的靜態輔助方法。
    /// </summary>
    public static class ProcessUtils
    {
        /// <summary>
        /// 在檔案總管中開啟路徑或選取檔案。
        /// </summary>
        /// <param name="path">檔案或資料夾路徑</param>
        /// <param name="createIfNotExist">如果路徑不存在，是否嘗試建立資料夾</param>
        public static void OpenInExplorer(string? path, bool createIfNotExist = false)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("路徑為空。");
                return;
            }

            if (File.Exists(path))
            {
                TryStart("explorer.exe", $"/select,\"{path}\"");
            }
            else if (Directory.Exists(path))
            {
                TryStart("explorer.exe", $"\"{path}\"");
            }
            else if (createIfNotExist)
            {
                try
                {
                    Directory.CreateDirectory(path);
                    TryStart("explorer.exe", $"\"{path}\"");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"建立並開啟資料夾失敗：{path}\n{ex.Message}");
                }
            }
            else
            {
                MessageBox.Show($"找不到路徑：{path}");
            }
        }

        /// <summary>
        /// 嘗試啟動一個外部程序。
        /// </summary>
        public static void TryStart(string fileName, string? args = null)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args ?? string.Empty,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "啟動失敗");
            }
        }
    }
}

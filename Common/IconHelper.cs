using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AI.KB.Assistant.Common
{
    /// <summary>
    /// V19.0 (V18.1 P3 需求)
    /// 
    /// 使用 Windows Shell P/Invoke (SHGetFileInfo) 
    /// 從 'Item.Path' [cite: `Models/Item.cs (V19.0)` Line 30] 擷取系統檔案圖示 (Icon)，
    /// 以便 V19.0 'UiRow' [cite: `Models/UiRow.cs (V19.0)` Line 103] 和 'MainWindow.xaml' [cite: `Views/MainWindow.xaml (V19.0)` Line 364] 
    /// 可以在中清單顯示圖示。
    /// </summary>
    public static class IconHelper
    {
        // P/Invoke SHGetFileInfo
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        // P/Invoke DestroyIcon
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        // SHGetFileInfo 結構
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        // Flags
        private const uint SHGFI_ICON = 0x000000100;     // 取得圖示
        private const uint SHGFI_SMALLICON = 0x000000001; // 取得小圖示 (16x16)
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010; // 使用 'dwFileAttributes'
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080; // 一般檔案

        /// <summary>
        /// (V19.0) 取得 'Item.Path' [cite: `Models/Item.cs (V19.0)` Line 30] 對應的系統圖示
        /// </summary>
        public static ImageSource? GetIcon(string? path)
        {
            // V19.0 (P3) 邏輯：
            // 如果 V19.0 'UiRow' [cite: `Models/UiRow.cs (V19.0)` Line 32] 傳入的路徑是 null，則不顯示圖示
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            IntPtr hIcon = IntPtr.Zero;
            try
            {
                SHFILEINFO shfi = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_SMALLICON;

                // 如果檔案/資料夾不存在，我們仍然嘗試使用 'FILE_ATTRIBUTE_NORMAL' 
                // 搭配副檔名 (Ext) [cite: `Models/UiRow.cs (V19.0)` Line 32] 來取得預設圖示
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    flags |= SHGFI_USEFILEATTRIBUTES;
                }

                hIcon = SHGetFileInfo(
                    path,
                    FILE_ATTRIBUTE_NORMAL, // V19.0 (P3) 
                    ref shfi,
                    (uint)Marshal.SizeOf(shfi),
                    flags);

                if (hIcon == IntPtr.Zero)
                {
                    return null;
                }

                // 轉換 IntPtr (Icon) 為 WPF ImageSource
                using (Icon systemIcon = Icon.FromHandle(shfi.hIcon))
                {
                    ImageSource? iconSource = Imaging.CreateBitmapSourceFromHIcon(
                        systemIcon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    iconSource?.Freeze(); // 效能優化
                    return iconSource;
                }
            }
            catch (Exception)
            {
                return null; // 擷取圖示失敗
            }
            finally
            {
                // 釋放 hIcon 資源
                if (hIcon != IntPtr.Zero)
                {
                    DestroyIcon(hIcon);
                }
            }
        }
    }
}
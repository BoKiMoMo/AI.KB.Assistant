using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Helpers;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// �ھڳ]�w�P�������G�A�M�w�ɮץت��a���|
    /// </summary>
    public sealed class RoutingService
    {
        private readonly AppConfig _cfg;

        // �c���ɮ����������]���ɦW �� �����W�١^
        private static readonly Dictionary<string, string> FileTypeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // ���
            [".doc"] = "���",
            [".docx"] = "���",
            [".pdf"] = "���",
            [".txt"] = "���",
            [".xls"] = "���",
            [".xlsx"] = "���",
            [".ppt"] = "���",
            [".pptx"] = "²��",

            // �Ϥ�
            [".jpg"] = "�Ϥ�",
            [".jpeg"] = "�Ϥ�",
            [".png"] = "�Ϥ�",
            [".gif"] = "�Ϥ�",
            [".bmp"] = "�Ϥ�",
            [".tif"] = "�Ϥ�",
            [".tiff"] = "�Ϥ�",
            [".svg"] = "�Ϥ�",

            // ���T / �v��
            [".mp3"] = "�v��",
            [".wav"] = "�v��",
            [".aac"] = "�v��",
            [".mp4"] = "�v��",
            [".mov"] = "�v��",
            [".avi"] = "�v��",
            [".mkv"] = "�v��",

            // ���Y
            [".zip"] = "���Y",
            [".rar"] = "���Y",
            [".7z"] = "���Y",
            [".tar"] = "���Y",

            // �{���X
            [".cs"] = "�޳N",
            [".cpp"] = "�޳N",
            [".h"] = "�޳N",
            [".py"] = "�޳N",
            [".js"] = "�޳N",
            [".ts"] = "�޳N",
            [".java"] = "�޳N",
            [".html"] = "�޳N",
            [".css"] = "�޳N",
            [".json"] = "�޳N",
            [".xml"] = "�޳N",
        };

        public RoutingService(AppConfig cfg)
        {
            _cfg = cfg;
        }

        /// <summary>
        /// �إߥت��a���|
        /// </summary>
        public string BuildDestination(string srcPath, string category, DateTimeOffset when)
        {
            var fname = Path.GetFileName(srcPath);
            var parts = TimePathHelper.Parts(when);

            string destDir = _cfg.App.RootDir;

            switch (_cfg.App.ClassificationMode)
            {
                case "Category":
                    destDir = Path.Combine(_cfg.App.RootDir, category);
                    break;

                case "Date":
                    destDir = Path.Combine(_cfg.App.RootDir, parts.Year, parts.Month, category);
                    break;

                case "Project":
                    destDir = Path.Combine(_cfg.App.RootDir, _cfg.App.ProjectName, category);
                    break;

                case "FileType":
                    var ext = Path.GetExtension(srcPath);
                    var type = FileTypeMap.TryGetValue(ext, out var mapped) ? mapped : "��L";
                    destDir = Path.Combine(_cfg.App.RootDir, type, category);
                    break;

                case "TimePeriod":
                    destDir = _cfg.App.TimeGranularity switch
                    {
                        "Year" => Path.Combine(_cfg.App.RootDir, parts.Year, category),
                        "Quarter" => Path.Combine(_cfg.App.RootDir, parts.Year, parts.Quarter, category),
                        "Month" => Path.Combine(_cfg.App.RootDir, parts.Year, parts.Month, category),
                        "Week" => Path.Combine(_cfg.App.RootDir, parts.Year, parts.Week, category),
                        _ => Path.Combine(_cfg.App.RootDir, parts.Year, parts.Month, category),
                    };
                    break;

                case "Hybrid":
                    destDir = Path.Combine(_cfg.App.RootDir, _cfg.App.ProjectName, parts.Year, category);
                    break;

                default:
                    destDir = Path.Combine(_cfg.App.RootDir, "��L");
                    break;
            }

            // �T�O��Ƨ��s�b
            Directory.CreateDirectory(destDir);

            return Path.Combine(destDir, fname);
        }

        /// <summary>
        /// �Y�ɮצW�ٽĬ�A�̷� Overwrite �����ѨM
        /// </summary>
        public string ResolveCollision(string path)
        {
            if (!File.Exists(path)) return path;

            var dir = Path.GetDirectoryName(path)!;
            var fname = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);

            if (_cfg.App.Overwrite.Equals("overwrite", StringComparison.OrdinalIgnoreCase))
                return path;

            if (_cfg.App.Overwrite.Equals("skip", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(dir, $"{fname}{ext}"); // ��˪�^�A�~�h���л\

            // �w�] rename
            int i = 2;
            string newPath;
            do
            {
                newPath = Path.Combine(dir, $"{fname} ({i++}){ext}");
            } while (File.Exists(newPath));
            return newPath;
        }
    }
}

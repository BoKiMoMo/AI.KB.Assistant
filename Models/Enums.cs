using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// 檔案搬移模式。
    /// </summary>
    public enum MoveMode
    {
        /// <summary>移動檔案（搬出原位）</summary>
        Move = 0,

        /// <summary>複製檔案（保留原位）</summary>
        Copy = 1
    }

    /// <summary>
    /// 同名檔案的處理策略。
    /// </summary>
    public enum OverwritePolicy
    {
        /// <summary>覆蓋舊檔案</summary>
        Replace = 0,

        /// <summary>自動重新命名新檔，避免覆蓋</summary>
        Rename = 1,

        /// <summary>跳過該檔案，不處理</summary>
        Skip = 2
    }

    /// <summary>
    /// 檔案家族分類（對應副檔名大類）。
    /// </summary>
    public enum FileFamily
    {
        Image,
        Video,
        Audio,
        Document,
        Code,
        Archive,
        Other
    }

    /// <summary>
    /// JSON 列舉轉換器：將 enum 序列化為小寫字串，
    /// 例如 Move → "move"、Copy → "copy"。
    /// </summary>
    public sealed class LowercaseEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s))
                return default;

            // 嘗試忽略大小寫解析
            if (Enum.TryParse<T>(s, true, out var val))
                return val;

            return default;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString().ToLowerInvariant());
        }
    }
}

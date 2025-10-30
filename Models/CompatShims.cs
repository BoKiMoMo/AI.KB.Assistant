// CompatShims.cs
// 說明：此檔僅作為「相容性小工具」的容器，不得定義/局部定義 AppConfig、Item、DbService 等核心型別。
// 先前若在這裡宣告了 partial AppConfig / partial Item / 任何重複型別，請一律刪除避免 CS0260/CS0102。

namespace AI.KB.Assistant
{
    internal static class CompatShims
    {
        // 預留：放舊版轉接的小工具或 helper（不可放核心類別/屬性/partial）
    }
}

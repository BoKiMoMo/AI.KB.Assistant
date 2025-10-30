namespace AI.KB.Assistant.Models
{
    // 舊碼大量使用的列舉；缺它們會讓 IntakeService / UI 直接編不過
    public enum ItemStatus
    {
        None = 0,
        New = 1,
        Staged = 2,
        Committed = 3,
        Error = 9
    }

    public enum MoveMode
    {
        Copy = 0,
        Move = 1
    }

    public enum OverwritePolicy
    {
        Skip = 0,
        Replace = 1,
        KeepBoth = 2
    }
}

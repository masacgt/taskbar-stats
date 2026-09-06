namespace TaskbarStats.UI;

public enum LoadLevel
{
    Normal,
    Warning,
    Critical,
}

public static class LoadLevelClassifier
{
    public const int WarningThreshold = 70;
    public const int CriticalThreshold = 90;

    public static LoadLevel GetLevel(int maxPercent) =>
        maxPercent >= CriticalThreshold ? LoadLevel.Critical
        : maxPercent >= WarningThreshold ? LoadLevel.Warning
        : LoadLevel.Normal;
}

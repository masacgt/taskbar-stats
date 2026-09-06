using TaskbarStats.UI;
using Xunit;

namespace TaskbarStats.Tests;

public class LoadLevelTests
{
    [Theory]
    [InlineData(0, LoadLevel.Normal)]
    [InlineData(69, LoadLevel.Normal)]
    [InlineData(70, LoadLevel.Warning)]
    [InlineData(89, LoadLevel.Warning)]
    [InlineData(90, LoadLevel.Critical)]
    [InlineData(100, LoadLevel.Critical)]
    public void GetLevel_AppliesThresholds(int percent, LoadLevel expected)
        => Assert.Equal(expected, LoadLevelClassifier.GetLevel(percent));
}

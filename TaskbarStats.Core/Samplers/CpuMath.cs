namespace TaskbarStats.Samplers;

public static class CpuMath
{
    public static double? CalculatePercent(long prevIdle, long prevTotal, long curIdle, long curTotal)
    {
        long deltaIdle = curIdle - prevIdle;
        long deltaTotal = curTotal - prevTotal;
        if (deltaTotal <= 0)
        {
            return null;
        }

        double percent = (1.0 - (double)deltaIdle / (double)deltaTotal) * 100.0;
        return Math.Clamp(percent, 0.0, 100.0);
    }
}

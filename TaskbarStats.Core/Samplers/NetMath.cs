namespace TaskbarStats.Samplers;

public static class NetMath
{
    public const double MaxMbitsPerSec = 9999.0;

    public static double BytesPerSecToMbits(double bytesPerSec)
        => Math.Max(0.0, bytesPerSec) * 8.0 / 1_000_000.0;

    public static long ClampMbps(double mbitsPerSec)
        => (long)Math.Round(Math.Clamp(mbitsPerSec, 0.0, MaxMbitsPerSec), MidpointRounding.AwayFromZero);
}

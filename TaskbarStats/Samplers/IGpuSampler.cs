namespace TaskbarStats.Samplers;

public interface IGpuSampler : IDisposable
{
    string Vendor { get; }

    GpuSnapshot Sample();
}

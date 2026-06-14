namespace ShortLynx.Services.Visits;

public class VisitSinkOptions
{
    public int ChannelCapacity { get; set; } = 10_000;
    public int BatchSize { get; set; } = 100;
    public int DrainIntervalMs { get; set; } = 500;
}

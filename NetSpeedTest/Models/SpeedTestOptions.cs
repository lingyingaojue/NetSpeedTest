namespace NetSpeedTest.Models;

public class SpeedTestOptions
{
    public int ThreadCount { get; set; } = 128;
    public int TestTimeoutSec { get; set; } = 60;
    public int AverageDelaySec { get; set; } = 10;
    public double RateWindowSec { get; set; } = 3.0;
    public int NicPollIntervalMs { get; set; } = 1000;
    public int ThreadRampUpMs { get; set; } = 500;
    public int LatencyPollIntervalMs { get; set; } = 2000;
    public bool CompensationEnabled { get; set; } = true;
    public double CompensationThreshold { get; set; } = 0.5;
    public int CompensationExtraThreads { get; set; } = 16;
    public int CompensationConfirmSec { get; set; } = 3;
    public bool AdaptiveThreadsEnabled { get; set; } = true;
}

namespace NetSpeedTest.Models;

public class SpeedTestOptions
{
    public int ThreadCount { get; set; } = 128;
    public int TestTimeoutSec { get; set; } = 90;
    public int AverageDelaySec { get; set; } = 10;
    public double RateWindowSec { get; set; } = 3.0;
    public int NicPollIntervalMs { get; set; } = 1000;
    public int ThreadRampUpMs { get; set; } = 500;
}

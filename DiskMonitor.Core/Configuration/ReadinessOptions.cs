namespace DiskMonitor.Core.Configuration;

public sealed record ReadinessOptions(
    double ActiveTimeThresholdPercent = 20,
    int StableDurationSeconds = 60)
{
    public TimeSpan StableDuration => TimeSpan.FromSeconds(StableDurationSeconds);

    public void Validate()
    {
        if (!double.IsFinite(ActiveTimeThresholdPercent)
            || ActiveTimeThresholdPercent is <= 0 or > 100)
        {
            throw new InvalidDataException("The disk active-time threshold must be greater than 0% and no more than 100%.");
        }

        if (StableDurationSeconds is < 1 or > 3600)
        {
            throw new InvalidDataException("The stable duration must be between 1 and 3600 seconds.");
        }
    }
}

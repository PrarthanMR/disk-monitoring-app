using DiskMonitor.Core.Configuration;
using DiskMonitor.Core.Models;

namespace DiskMonitor.Core.Services;

public sealed class ReadinessEvaluator
{
    private static readonly TimeSpan SamplingJitterTolerance = TimeSpan.FromMilliseconds(500);
    private readonly ReadinessOptions _options;
    private readonly TimeSpan _samplingInterval;
    private DateTimeOffset? _lastTimestampUtc;
    private DateTimeOffset? _stableSinceUtc;
    private bool _hasReceivedValidSample;

    public ReadinessEvaluator(
        ReadinessOptions? options = null,
        TimeSpan? samplingInterval = null)
    {
        _options = options ?? new ReadinessOptions();
        _options.Validate();
        _samplingInterval = samplingInterval ?? TimeSpan.FromSeconds(1);
        if (_samplingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(samplingInterval));
        }
    }

    public ReadinessUpdate Evaluate(DiskActiveTimeSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.TimestampUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Sample timestamps must be UTC.", nameof(sample));
        }

        if (_lastTimestampUtc is DateTimeOffset previous
            && sample.TimestampUtc <= previous)
        {
            throw new ArgumentException("Sample timestamps must be strictly increasing.", nameof(sample));
        }

        bool samplingGap = _lastTimestampUtc is DateTimeOffset last
            && sample.TimestampUtc - last > _samplingInterval + SamplingJitterTolerance;
        _lastTimestampUtc = sample.TimestampUtc;
        if (samplingGap)
        {
            _stableSinceUtc = null;
        }

        if (!IsValidActiveTime(sample.ActiveTimePercent))
        {
            _stableSinceUtc = null;
            ReadinessState state = _hasReceivedValidSample
                ? ReadinessState.PleaseWait
                : ReadinessState.Initializing;
            string reason = string.IsNullOrWhiteSpace(sample.UnavailableReason)
                ? "The disk active-time counter is unavailable or invalid."
                : $"The disk active-time counter is unavailable: {sample.UnavailableReason}";
            return new ReadinessUpdate(
                sample.TimestampUtc,
                state,
                null,
                TimeSpan.Zero,
                $"{reason} The stability timer has been reset.");
        }

        _hasReceivedValidSample = true;
        double activeTime = sample.ActiveTimePercent!.Value;
        if (activeTime >= _options.ActiveTimeThresholdPercent)
        {
            _stableSinceUtc = null;
            return new ReadinessUpdate(
                sample.TimestampUtc,
                ReadinessState.PleaseWait,
                activeTime,
                TimeSpan.Zero,
                $"Disk activity is {activeTime:F1}%. Please wait until it stays below {_options.ActiveTimeThresholdPercent:F1}% for {_options.StableDurationSeconds} seconds.");
        }

        _stableSinceUtc ??= sample.TimestampUtc;
        TimeSpan stableElapsed = sample.TimestampUtc - _stableSinceUtc.Value;
        if (stableElapsed >= _options.StableDuration)
        {
            return new ReadinessUpdate(
                sample.TimestampUtc,
                ReadinessState.Ready,
                activeTime,
                _options.StableDuration,
                $"Disk activity has stayed below {_options.ActiveTimeThresholdPercent:F1}% for {_options.StableDurationSeconds} seconds. The system is ready for normal use.");
        }

        return new ReadinessUpdate(
            sample.TimestampUtc,
            ReadinessState.PleaseWait,
            activeTime,
            stableElapsed,
            $"Disk activity is below {_options.ActiveTimeThresholdPercent:F1}%. Stable for {FormatDuration(stableElapsed)} of {_options.StableDurationSeconds} seconds.");
    }

    private static bool IsValidActiveTime(double? value) =>
        value is double activeTime
        && double.IsFinite(activeTime)
        && activeTime is >= 0 and <= 100;

    private static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalMinutes:D2}:{duration.Seconds:D2}";
}

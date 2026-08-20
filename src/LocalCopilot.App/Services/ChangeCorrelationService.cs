using LocalCopilot_App.Diagnostics;
using System;
using System.Diagnostics;
using System.Globalization;

namespace LocalCopilot_App.Services;

public sealed class ChangeCorrelationService
{
    private static readonly TimeSpan DefaultLookback =
        TimeSpan.FromSeconds(
            2);

    private readonly DiagnosticTimeline _timeline;

    private readonly TimeSpan _lookback;

    public ChangeCorrelationService(
        DiagnosticTimeline timeline,
        TimeSpan? lookback = null)
    {
        _timeline =
            timeline ??
            throw new ArgumentNullException(
                nameof(timeline));

        _lookback =
            lookback ??
            DefaultLookback;

        if (_lookback <=
            TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lookback));
        }
    }

    public ChangeCorrelationResult? Observe(
        PersistentChangeSample sample)
    {
        ArgumentNullException.ThrowIfNull(
            sample);

        if (
            sample.Change.Classification !=
                ChangeClassification.Meaningful &&
            sample.Change.Classification !=
                ChangeClassification.Large)
        {
            return null;
        }

        long observedAtTicks =
            Stopwatch.GetTimestamp();

        bool activeEpoch =
            _timeline.TryReadMostRecent(
                sample.EpochId,
                observedAtTicks,
                _lookback,
                out InputActivityEvent? activity);

        if (!activeEpoch)
        {
            return null;
        }

        double? ageMilliseconds =
            activity is null
                ? null
                : Stopwatch.GetElapsedTime(
                        activity.TimestampTicks,
                        observedAtTicks)
                    .TotalMilliseconds;

        ChangeCorrelationResult result =
            new(
                sample.EpochId,
                sample.Change.Classification,
                activity?.Kind,
                ageMilliseconds);

        DiagnosticLog.Write(
            "CORRELATION.CHANGE",
            $"epoch={result.EpochId} " +
            $"classification={result.Classification} " +
            $"trigger={result.PossibleTrigger?.ToString() ?? "None"} " +
            $"ageMs={FormatAge(result.TriggerAgeMilliseconds)}");

        return result;
    }

    private static string FormatAge(
        double? ageMilliseconds)
    {
        return ageMilliseconds?.ToString(
                "0.0",
                CultureInfo.InvariantCulture) ??
            "n/a";
    }
}

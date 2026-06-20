using Microsoft.UI.Dispatching;

namespace GSBT.WinUI.Services;

/// <summary>Smooths chunky native 7-Zip progress for the UI (cosmetic; not ground truth).</summary>
public sealed class CompressionProgressSimulator : IDisposable
{
    private readonly CompressionProgressSimulationProfile _profile;
    private readonly Action<int> _report;
    private readonly DispatcherQueueTimer _timer;
    private double _displayed;
    private double _target;
    private long _targetUnchangedSince = Environment.TickCount64;

    public CompressionProgressSimulator(int sevenMx, Action<int> report, DispatcherQueue dispatcherQueue)
    {
        _profile = CompressionProgressSimulation.FromMx(sevenMx);
        _report = report;
        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(80);
        _timer.Tick += (_, _) => OnTick();
    }

    public void Start()
    {
        _displayed = 0;
        _target = 0;
        _targetUnchangedSince = Environment.TickCount64;
        _timer.Start();
        _report(0);
    }

    public void SetTarget(int pct)
    {
        pct = Math.Clamp(pct, 0, 100);
        if (Math.Abs(pct - _target) <= 0.01)
        {
            return;
        }

        if (pct > _target)
        {
            _targetUnchangedSince = Environment.TickCount64;
        }

        _target = pct;
    }

    public void Complete()
    {
        _timer.Stop();
        _displayed = 100;
        _report(100);
    }

    public void Dispose() => _timer.Stop();

    private void OnTick()
    {
        if (_profile == CompressionProgressSimulationProfile.None)
        {
            if (Math.Abs(_displayed - _target) > 0.01)
            {
                _displayed = _target;
                _report((int)Math.Round(_displayed));
            }

            return;
        }

        var stallMs = Environment.TickCount64 - _targetUnchangedSince;
        var ceiling = CompressionProgressSimulation.GetCosmeticCeiling(_displayed, _target, _profile);
        var stallThreshold = CompressionProgressSimulation.GetStallThresholdMs(_profile, _target);
        var catchUpRate = CompressionProgressSimulation.GetCatchUpRate(_profile);
        var creepRate = CompressionProgressSimulation.GetCreepRate(_profile, _target);

        if (_displayed < _target)
        {
            var gap = _target - _displayed;
            _displayed += Math.Max(0.3, gap * catchUpRate);
            _displayed = Math.Min(_displayed, _target);
        }
        else if (stallMs >= stallThreshold
                 && ceiling.HasValue
                 && _displayed < ceiling.Value
                 && _target < 100)
        {
            _displayed = Math.Min(ceiling.Value, _displayed + creepRate);
        }

        if (_target >= 100)
        {
            _displayed = 100;
        }
        else if (ceiling.HasValue)
        {
            _displayed = Math.Min(_displayed, ceiling.Value);
        }

        _report((int)Math.Round(_displayed));
    }
}

internal enum CompressionProgressSimulationProfile
{
    None,
    Moderate,
    Heavy,
}

internal static class CompressionProgressSimulation
{
    public static CompressionProgressSimulationProfile FromMx(int mx) =>
        mx switch
        {
            <= 1 => CompressionProgressSimulationProfile.None,
            3 or 5 => CompressionProgressSimulationProfile.Moderate,
            _ => CompressionProgressSimulationProfile.Heavy,
        };

    public static int GetStallThresholdMs(CompressionProgressSimulationProfile profile, double target) =>
        profile switch
        {
            CompressionProgressSimulationProfile.Heavy when IsHeavyFiftyPlateau(target) => 900,
            CompressionProgressSimulationProfile.Heavy when IsHeavySeventyFivePlateau(target) => 750,
            CompressionProgressSimulationProfile.Heavy => 450,
            CompressionProgressSimulationProfile.Moderate => 380,
            _ => 0,
        };

    public static double GetCatchUpRate(CompressionProgressSimulationProfile profile) =>
        profile switch
        {
            CompressionProgressSimulationProfile.Heavy => 0.09,
            CompressionProgressSimulationProfile.Moderate => 0.2,
            _ => 1.0,
        };

    public static double GetCreepRate(CompressionProgressSimulationProfile profile, double target) =>
        profile switch
        {
            CompressionProgressSimulationProfile.Heavy when IsHeavyFiftyPlateau(target) => 0.10,
            CompressionProgressSimulationProfile.Heavy when IsHeavySeventyFivePlateau(target) => 0.11,
            CompressionProgressSimulationProfile.Heavy when target >= 96 => 0.12,
            CompressionProgressSimulationProfile.Heavy => 0.14,
            CompressionProgressSimulationProfile.Moderate => 0.32,
            _ => 0,
        };

    /// <summary>Max displayed % while real progress is stalled (never reaches 100 until real completion).</summary>
    public static double? GetCosmeticCeiling(
        double displayed,
        double target,
        CompressionProgressSimulationProfile profile)
    {
        if (profile == CompressionProgressSimulationProfile.None)
        {
            return null;
        }

        if (target >= 100)
        {
            return 100;
        }

        if (profile == CompressionProgressSimulationProfile.Moderate)
        {
            var baseQuarter = Math.Floor(Math.Max(target, displayed) / 25.0) * 25.0;
            var nextQuarter = baseQuarter + 25.0;
            return nextQuarter >= 100 ? 99 : nextQuarter - 1;
        }

        return GetHeavyCosmeticCeiling(target);
    }

    private static bool IsHeavyFiftyPlateau(double target) => target is >= 47 and <= 53;

    private static bool IsHeavySeventyFivePlateau(double target) => target is >= 72 and <= 78;

    /// <summary>mx 7/9: small nudges above each native stall — never leap a full quarter ahead of real %.</summary>
    private static double GetHeavyCosmeticCeiling(double target)
    {
        if (target >= 96)
        {
            return 99;
        }

        if (IsHeavyFiftyPlateau(target))
        {
            return Math.Min(57, target + 7);
        }

        if (IsHeavySeventyFivePlateau(target))
        {
            return Math.Min(78, target + 4);
        }

        if (target < 47)
        {
            return Math.Min(44, target + 3);
        }

        if (target < 72)
        {
            return Math.Min(65, target + 2);
        }

        return Math.Min(92, target + 3);
    }
}

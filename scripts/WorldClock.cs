using System;
using Godot;

namespace Star;

/// <summary>
/// 全局唯一硬时钟：日推进、沙暴轮次、预警与起止事件。
/// 沙暴周期按轮次递减，所有系统共用同一个时间源。
/// </summary>
public class WorldClock
{
    public const float DayLengthSeconds = 30f;
    public const float StormDurationSeconds = 10f;
    public const float WarningLeadSeconds = 8f;
    public const float StormSpeedMultiplier = 0.6f;
    public const float StormFuelMultiplier = 1.8f;
    public const int StormVisionPenalty = 2;

    private const float FirstStormDelaySeconds = 20f;
    private const float InitialGapSeconds = 25f;
    private const float GapShrinkFactor = 0.8f;
    private const float MinGapSeconds = 10f;

    public float Elapsed { get; private set; }
    public int Day => 1 + Mathf.FloorToInt(Elapsed / DayLengthSeconds);
    public bool IsStorm { get; private set; }
    public bool IsWarning { get; private set; }
    public float TimeToNextStorm { get; private set; }
    public float StormRemaining { get; private set; }
    public int StormCount { get; private set; }
    public float GapSeconds { get; private set; }

    /// <summary>沙暴预警、开始、结束事件；建筑系统可在此接入受损结算。</summary>
    public event Action SandstormWarning;
    public event Action SandstormStarted;
    public event Action SandstormEnded;

    private float _nextStormAt;
    private float _stormEndsAt;
    private bool _warningRaised;

    public WorldClock()
    {
        _nextStormAt = FirstStormDelaySeconds;
        TimeToNextStorm = FirstStormDelaySeconds;
        GapSeconds = InitialGapSeconds;
    }

    public void Update(float dt)
    {
        Elapsed += dt;

        if (IsStorm)
        {
            StormRemaining = Mathf.Max(0f, _stormEndsAt - Elapsed);
            if (Elapsed >= _stormEndsAt)
            {
                IsStorm = false;
                StormRemaining = 0f;
                _nextStormAt = Elapsed + GapSeconds;
                GapSeconds = Mathf.Max(MinGapSeconds, GapSeconds * GapShrinkFactor);
                _warningRaised = false;
                SandstormEnded?.Invoke();
            }

            return;
        }

        TimeToNextStorm = Mathf.Max(0f, _nextStormAt - Elapsed);
        IsWarning = TimeToNextStorm > 0f && TimeToNextStorm <= WarningLeadSeconds;
        if (IsWarning && !_warningRaised)
        {
            _warningRaised = true;
            SandstormWarning?.Invoke();
        }

        if (Elapsed >= _nextStormAt)
        {
            IsStorm = true;
            IsWarning = false;
            StormCount++;
            _stormEndsAt = Elapsed + StormDurationSeconds;
            StormRemaining = StormDurationSeconds;
            TimeToNextStorm = 0f;
            SandstormStarted?.Invoke();
        }
    }

    public static string FormatSeconds(float seconds)
    {
        int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return $"{total / 60:00}:{total % 60:00}";
    }
}

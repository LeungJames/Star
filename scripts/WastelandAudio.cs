using Godot;

namespace Star;

/// <summary>混合沙地、沙暴与核心塔环境声，播放交互与可见战况的声音。</summary>
public partial class WastelandAudio : Node
{
    internal enum Cue
    {
        Ui,
        Select,
        Order,
        Denied,
        Notice,
        Decision,
        Complete,
        StormWarning,
        StormOnset,
        StormClear,
        Victory,
        Defeat,
    }

    private const float FieldRange = HexMap.HexSize * 7f;
    private static readonly StringName AmbienceBus = new("Ambience");
    private static readonly StringName FieldBus = new("Field");
    private static readonly StringName InterfaceBus = new("Interface");

    private readonly AudioStreamPlayer3D[] _fieldVoices = new AudioStreamPlayer3D[2];
    private readonly float[] _lastCueTime = new float[(int)Cue.Defeat + 1];
    private CameraRig _camera;
    private Vector3 _core;
    private WorldClock _clock;
    private AudioStreamPlayer _wind;
    private AudioStreamPlayer _storm;
    private AudioStreamPlayer _machinery;
    private AudioStreamPlayer _interface;
    private AudioStreamWav[] _cues;
    private AudioStreamWav _lightFire;
    private AudioStreamWav _heavyFire;
    private AudioStreamWav _airFire;
    private float _time;
    private float _stormBlend;
    private float _baseBlend;
    private float _sessionGain;
    private float _fieldCooldown;
    private float _closestFire = FieldRange * FieldRange;
    private Vector3 _fireOrigin;
    private FormationKind _fireKind;
    private int _shots;
    private bool _ended;

    internal void ConnectWorld(WorldClock clock, CameraRig camera, Vector3 core)
    {
        _camera = camera;
        _core = core;
        _clock = clock;
        clock.SandstormWarning += OnStormWarning;
        clock.SandstormStarted += OnStormStarted;
        clock.SandstormEnded += OnStormEnded;
    }

    public override void _Ready()
    {
        System.Array.Fill(_lastCueTime, -1f);
        _wind = MakeLoop("DustWind", WastelandSoundBank.Wind(false));
        _storm = MakeLoop("Sandstorm", WastelandSoundBank.Wind(true));
        _machinery = MakeLoop("CoreMachinery", WastelandSoundBank.Machinery());
        _cues = WastelandSoundBank.Cues();
        _lightFire = WastelandSoundBank.Weapon(false, false);
        _heavyFire = WastelandSoundBank.Weapon(true, false);
        _airFire = WastelandSoundBank.Weapon(false, true);

        _interface = new AudioStreamPlayer
        {
            Name = "RadioAndInterface",
            Bus = InterfaceBus,
            Stream = new AudioStreamPolyphonic { Polyphony = 6 },
        };
        AddChild(_interface);
        _interface.Play();

        for (int i = 0; i < _fieldVoices.Length; i++)
        {
            var voice = new AudioStreamPlayer3D
            {
                Name = $"FieldVoice{i + 1}",
                Bus = FieldBus,
                VolumeDb = -14f,
                UnitSize = 24f,
                MaxDistance = 110f,
                PanningStrength = 0.65f,
            };
            AddChild(voice);
            _fieldVoices[i] = voice;
        }
    }

    public override void _ExitTree()
    {
        if (_clock != null)
        {
            _clock.SandstormWarning -= OnStormWarning;
            _clock.SandstormStarted -= OnStormStarted;
            _clock.SandstormEnded -= OnStormEnded;
        }

        ReleasePlayer(_wind);
        ReleasePlayer(_storm);
        ReleasePlayer(_machinery);
        ReleasePlayer(_interface);
        foreach (var voice in _fieldVoices)
        {
            if (voice != null)
            {
                voice.Stop();
                voice.Stream = null;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (_clock == null || _camera == null)
        {
            return;
        }

        float dt = (float)delta;
        _time += dt;
        _stormBlend = Mathf.MoveToward(_stormBlend, _clock.IsStorm ? 1f : 0f, dt * 0.55f);
        float distanceToCore = _camera.Focus.DistanceTo(_core);
        float nearCore = 1f - Mathf.SmoothStep(HexMap.HexSize, HexMap.HexSize * 5f, distanceToCore);
        _baseBlend = Mathf.MoveToward(_baseBlend, nearCore, dt * 1.5f);
        _sessionGain = Mathf.MoveToward(_sessionGain, _ended ? 0.32f : 1f, dt * 0.95f);

        SetLoopGain(_wind, _sessionGain * (0.23f + 0.10f * (1f - _baseBlend)) * (1f - 0.78f * _stormBlend));
        SetLoopGain(_storm, _sessionGain * 0.64f * _stormBlend);
        SetLoopGain(_machinery, _sessionGain * 0.25f * _baseBlend * (1f - 0.76f * _stormBlend));
        PlayFieldVoice(dt);
    }

    internal void PlayCue(Cue cue)
    {
        int index = (int)cue;
        if (_time - _lastCueTime[index] < 0.14f ||
            _interface?.GetStreamPlayback() is not AudioStreamPlaybackPolyphonic playback)
        {
            return;
        }

        float db = cue switch
        {
            Cue.Ui or Cue.Select => -15f,
            Cue.Order or Cue.Denied or Cue.Notice => -12f,
            Cue.StormOnset or Cue.StormClear => -11f,
            Cue.Victory or Cue.Defeat => -5f,
            _ => -8f,
        };
        playback.PlayStream(_cues[index], 0f, db);
        _lastCueTime[index] = _time;
    }

    /// <summary>每拍只选取相机焦点附近的一处交火，避免持续伤害的每次结算都发声。</summary>
    internal void ObserveFire(FormationKind kind, Vector3 origin)
    {
        if (_ended || _camera == null || _fieldCooldown > 0f)
        {
            return;
        }

        Vector3 groundOrigin = new(origin.X, 0f, origin.Z);
        float distance = _camera.Focus.DistanceSquaredTo(groundOrigin);
        if (distance < _closestFire)
        {
            _closestFire = distance;
            _fireOrigin = origin;
            _fireKind = kind;
        }
    }

    internal void EndSession(bool victorious)
    {
        _ended = true;
        foreach (var voice in _fieldVoices)
        {
            voice.Stop();
        }

        PlayCue(victorious ? Cue.Victory : Cue.Defeat);
    }

    private AudioStreamPlayer MakeLoop(string nodeName, AudioStreamWav stream)
    {
        var player = new AudioStreamPlayer
        {
            Name = nodeName,
            Bus = AmbienceBus,
            Stream = stream,
            VolumeDb = -80f,
        };
        AddChild(player);
        return player;
    }

    private static void ReleasePlayer(AudioStreamPlayer player)
    {
        if (player != null)
        {
            player.Stop();
            player.Stream = null;
        }
    }

    private static void SetLoopGain(AudioStreamPlayer player, float gain)
    {
        if (gain <= 0.001f)
        {
            if (player.Playing)
            {
                player.Stop();
            }

            return;
        }

        player.VolumeDb = Mathf.LinearToDb(gain);
        if (!player.Playing)
        {
            player.Play();
        }
    }

    private void PlayFieldVoice(float dt)
    {
        _fieldCooldown = Mathf.Max(0f, _fieldCooldown - dt);
        if (_closestFire >= FieldRange * FieldRange)
        {
            return;
        }

        _closestFire = FieldRange * FieldRange;
        if (_ended)
        {
            return;
        }

        foreach (var voice in _fieldVoices)
        {
            if (voice.Playing)
            {
                continue;
            }

            voice.GlobalPosition = _fireOrigin;
            voice.Stream = _fireKind switch
            {
                FormationKind.HeavyTank or FormationKind.MediumTank => _heavyFire,
                FormationKind.LightShip or FormationKind.MediumShip => _airFire,
                _ => _lightFire,
            };
            voice.PitchScale = 0.9f + 0.05f * (_shots++ % 5);
            voice.Play();
            _fieldCooldown = _fireKind is FormationKind.HeavyTank or FormationKind.MediumTank ? 0.51f : 0.31f;
            return;
        }
    }

    private void OnStormWarning() => PlayCue(Cue.StormWarning);
    private void OnStormStarted() => PlayCue(Cue.StormOnset);
    private void OnStormEnded() => PlayCue(Cue.StormClear);
}

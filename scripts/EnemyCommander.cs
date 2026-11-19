using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Star;

/// <summary>敌方三段式推进：侦察 → 试探 → 进攻（试探受挫则短暂后撤）。</summary>
public class EnemyCommander
{
    private enum State
    {
        Scouting,
        Probing,
        Attacking,
        Retreating,
        Sheltering,
    }

    private readonly BattleManager _battle;
    private readonly FactionManager _factions;
    private readonly FactionId _owner;
    private readonly HexCoord _home;
    private readonly List<Formation> _intel = new();

    private State _state = State.Scouting;
    private Formation _target;
    private float _stateTime;
    private float _moveCooldown;

    public EnemyCommander(BattleManager battle, FactionManager factions, FactionId owner, HexCoord home)
    {
        _battle = battle;
        _factions = factions;
        _owner = owner;
        _home = home;
    }

    public void Update(float dt)
    {
        _stateTime += dt;
        _moveCooldown = Mathf.Max(0f, _moveCooldown - dt);

        var faction = _factions.Get(_owner);
        if (faction is { Eliminated: true })
        {
            return;
        }

        var enemies = _battle.Formations.Where(f => f.Owner == _owner && f.Hp > 0f).ToList();
        if (enemies.Count == 0)
        {
            return;
        }

        if (_battle.Clock.IsStorm)
        {
            if (_state != State.Sheltering)
            {
                ChangeState(State.Sheltering);
            }

            UpdateSheltering(enemies);
            return;
        }

        if (_state == State.Sheltering)
        {
            _battle.ShowMessage($"{FactionName}重新展开");
            ChangeState(State.Scouting);
        }

        if (!_factions.IsHostile(_owner))
        {
            UpdatePeacetime(enemies);
            return;
        }

        var scout = enemies.FirstOrDefault(f => f.Kind == FormationKind.LightTank) ?? enemies[0];
        switch (_state)
        {
            case State.Scouting:
                UpdateScouting(scout);
                break;
            case State.Probing:
                UpdateProbing(scout);
                break;
            case State.Attacking:
                UpdateAttacking(enemies);
                break;
            case State.Retreating:
                UpdateRetreating(scout);
                break;
        }

        RefreshIntel();
    }

    /// <summary>未开战时只在被事件要求时派轻型单位侦察，看到布局后回报关系变化。</summary>
    private void UpdatePeacetime(List<Formation> enemies)
    {
        var faction = _factions.Get(_owner);
        if (faction is { Eliminated: true })
        {
            return;
        }

        bool scoutRequested = _factions.ConsumeScoutRequest(_owner);
        if (!scoutRequested && faction != null && faction.Relation > GameBalance.ScoutRelationThreshold)
        {
            _target = null;
            _state = State.Scouting;
            return;
        }

        var scout = enemies.FirstOrDefault(f => f.Kind == FormationKind.LightTank);
        if (scout == null || scout.IsMoving || _moveCooldown > 0f)
        {
            return;
        }

        var seen = VisiblePlayers();
        if (seen.Count > 0)
        {
            _battle.ShowMessage($"{FactionName}侦察了你的防线，判断防守薄弱方向");
            _factions.ApplyRelation(_owner, -4);
            _moveCooldown = 3f;
            return;
        }

        var nearest = AlivePlayers().OrderBy(p => scout.Coord.DistanceTo(p.Coord)).FirstOrDefault();
        HexCoord? destination = nearest?.Coord ?? _battle.PlayerCoreCoord;
        if (destination.HasValue && TryMoveNear(scout, destination.Value, 2, scout.VisionRadius))
        {
            _moveCooldown = 2f;
        }
    }

    private string FactionName => _factions.Get(_owner)?.Name ?? "敌方";

    private void UpdateScouting(Formation scout)
    {
        var seen = VisiblePlayers();
        if (seen.Count > 0)
        {
            _intel.Clear();
            _intel.AddRange(seen);
            _battle.ShowMessage("敌方侦察发现：" + string.Join("、", seen.Select(p => p.DisplayName)));
            _target = WeakestTarget();
            ChangeState(State.Probing);
            return;
        }

        if (scout == null || scout.IsMoving || _moveCooldown > 0f)
        {
            return;
        }

        var nearest = AlivePlayers().OrderBy(p => scout.Coord.DistanceTo(p.Coord)).FirstOrDefault();
        HexCoord? destination = nearest?.Coord ?? _battle.PlayerCoreCoord;
        if (destination.HasValue && TryMoveNear(scout, destination.Value, 2, scout.VisionRadius))
        {
            _moveCooldown = 2f;
        }
    }

    private void UpdateProbing(Formation scout)
    {
        if (scout == null)
        {
            ChangeState(State.Attacking);
            return;
        }

        _target = _target != null && _target.Hp > 0f ? _target : WeakestTarget();
        var target = _target;
        if (target == null)
        {
            if (_battle.PlayerCoreCoord != null)
            {
                ChangeState(State.Attacking);
            }

            return;
        }

        if (!scout.IsMoving && _moveCooldown <= 0f
            && TryMoveNear(scout, target.Coord, 1, Mathf.Max(1, scout.Range)))
        {
            _moveCooldown = 1.5f;
        }

        if (scout.Hp / scout.MaxHp < 0.65f)
        {
            _battle.ShowMessage("敌方试探部队受挫，向后收缩");
            ChangeState(State.Retreating);
            return;
        }

        bool weak = _stateTime > 4f
                    && (target.Hp / target.MaxHp < 0.6f || (!target.IsCombatant && IsIsolated(target)));
        if (weak)
        {
            _battle.ShowMessage($"敌方判断：{target.DisplayName} 方向防守薄弱，主力压上");
            ChangeState(State.Attacking);
            return;
        }

        if (_stateTime > 10f)
        {
            _battle.ShowMessage("敌方试探结束，主力开始进攻");
            ChangeState(State.Attacking);
        }
    }

    private void UpdateAttacking(List<Formation> enemies)
    {
        _target = _target != null && _target.Hp > 0f ? _target : WeakestTarget();
        HexCoord? goal = _target?.Coord ?? _battle.PlayerCoreCoord;
        if (goal == null)
        {
            return;
        }

        if (_moveCooldown > 0f)
        {
            return;
        }

        foreach (var enemy in enemies)
        {
            if (enemy.IsMoving)
            {
                continue;
            }

            if (TryMoveNear(enemy, goal.Value, 1, Mathf.Max(1, enemy.Range)))
            {
                _moveCooldown = 1.2f;
                break;
            }
        }
    }

    private void UpdateSheltering(List<Formation> enemies)
    {
        if (_moveCooldown > 0f)
        {
            return;
        }

        foreach (var enemy in enemies)
        {
            if (enemy.IsMoving)
            {
                continue;
            }

            var home = _home;
            if (TryMoveNear(enemy, home, 0, 1))
            {
                _moveCooldown = 2f;
                break;
            }
        }
    }

    private void UpdateRetreating(Formation scout)    {
        if (scout == null)
        {
            ChangeState(State.Attacking);
            return;
        }

        if (!scout.IsMoving && _moveCooldown <= 0f)
        {
            var home = _home;
            if (TryMoveNear(scout, home, 0, 1))
            {
                _moveCooldown = 2f;
            }
        }

        if (_stateTime > 5f)
        {
            _battle.ShowMessage("敌方试探部队重新集结，主力开始进攻");
            ChangeState(State.Attacking);
        }
    }

    private void RefreshIntel()
    {
        var visible = VisiblePlayers();
        if (visible.Count == 0)
        {
            return;
        }

        _intel.RemoveAll(f => f.Hp <= 0f);
        foreach (var formation in visible)
        {
            if (!_intel.Contains(formation))
            {
                _intel.Add(formation);
            }
        }
    }

    private List<Formation> AlivePlayers()
    {
        return _battle.Formations.Where(f => f.IsPlayer && f.Hp > 0f).ToList();
    }

    private List<Formation> VisiblePlayers()
    {
        return AlivePlayers()
            .Where(f => _battle.IsVisibleTo(Faction.Enemy, f.Coord))
            .ToList();
    }

    private Formation WeakestTarget()
    {
        var players = AlivePlayers();
        var known = players.Where(p => _intel.Contains(p)).ToList();
        if (known.Count > 0)
        {
            players = known;
        }

        if (players.Count == 0)
        {
            return null;
        }

        return players.OrderBy(p => WeaknessScore(p, players)).First();
    }

    /// <summary>分数越低越值得打：残血、孤立、非战斗单位优先。</summary>
    private static float WeaknessScore(Formation candidate, List<Formation> players)
    {
        float ratio = candidate.Hp / candidate.MaxHp;
        float nearestAlly = players
            .Where(p => p != candidate)
            .Select(p => p.Coord.DistanceTo(candidate.Coord))
            .DefaultIfEmpty(8)
            .Min();
        float score = ratio * 100f - nearestAlly * 4f;
        if (!candidate.IsCombatant)
        {
            score -= 60f;
        }

        return score;
    }

    private bool IsIsolated(Formation candidate)
    {
        return AlivePlayers()
            .Where(p => p != candidate)
            .Select(p => p.Coord.DistanceTo(candidate.Coord))
            .DefaultIfEmpty(8)
            .Min() >= 3;
    }

    private bool TryMoveNear(Formation mover, HexCoord target, int minDistance, int maxDistance)
    {
        var candidates = new List<(HexCoord Cell, int Cost)>();
        foreach (var cell in _battle.Map.Cells())
        {
            int distance = cell.DistanceTo(target);
            if (distance < minDistance || distance > maxDistance || !_battle.CanEnter(mover, cell))
            {
                continue;
            }

            candidates.Add((cell, mover.Coord.DistanceTo(cell)));
        }

        candidates.Sort((a, b) => a.Cost.CompareTo(b.Cost));
        int tried = 0;
        foreach (var candidate in candidates)
        {
            if (tried++ >= 6)
            {
                break;
            }

            if (_battle.IssueMove(mover, candidate.Cell, false))
            {
                return true;
            }
        }

        return false;
    }

    private void ChangeState(State state)
    {
        _state = state;
        _stateTime = 0f;
        _moveCooldown = 0f;
    }
}

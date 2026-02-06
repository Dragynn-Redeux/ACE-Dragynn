using System;
using ACE.Entity;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;

namespace ACE.Server.WorldObjects.Patrol;

public sealed class PatrolComponent
{
    private readonly Creature _creature;
    // Cache the original spawn/home location as the patrol center.
    // Do NOT rely on _creature.Home because other systems may update it.
    private readonly Position _patrolHome;

    private PatrolPath _path;
    private int _index;

    private double _nextMoveTime;
    private Position _currentDest;
    private Position _finalDest;

    // Pause handling (applied only on real waypoints, not detours)
    private float _pauseOnArrivalSeconds;

    // Stuck handling: detect lack of movement progress and do small detours ("nudges")
    private double _nextProgressSampleTime;
    private Position _lastSamplePos;
    private double _stuckSinceTime;
    private int _stuckAttempts;
    private bool _detouring;
    // When patrol is interrupted (combat/emote), rejoin the patrol loop from current position.
    private bool _needsRejoin;
    private const float ArriveDistance = 1.5f;

    public PatrolComponent(Creature creature)
    {
        if (creature == null)
        {
            throw new ArgumentNullException(nameof(creature));
        }
        _creature = creature;

        // Capture patrol center once (original spawn/home). If Home isn't set, fall back quietly.
        var homePos = _creature.Home ?? _creature.Location;
        _patrolHome = new Position(homePos);

        ReloadPath();
    }


    public void ReloadPath()
    {
        var raw = _creature.GetProperty(PropertyString.PatrolPath);
        _path = PatrolPath.Parse(raw);
        _index = 0;

        _currentDest = null;
        _finalDest = null;

        _detouring = false;
        _stuckAttempts = 0;
        _stuckSinceTime = 0;
        _nextProgressSampleTime = 0;
        _lastSamplePos = null;

        _pauseOnArrivalSeconds = 0f;
        _nextMoveTime = 0;
    }

    /// <summary>
    /// Clears any in-flight patrol destination so patrol can't be wedged "waiting to arrive"
    /// after a combat interruption.
    /// </summary>
    public void ResetDestination(double currentUnixTime = 0)
    {
        _currentDest = null;
        _finalDest = null;

        _detouring = false;
        _stuckAttempts = 0;
        _stuckSinceTime = 0;
        _lastSamplePos = null;
        _nextProgressSampleTime = 0;
        _pauseOnArrivalSeconds = 0f;

        // After interruptions, recompute next waypoint from current position.
        _needsRejoin = true;

        // Allow immediate next waypoint after reset.
        _nextMoveTime = currentUnixTime;

    }


    public void Update(double currentUnixTime)
    {
        // Patrol only acts when idle; combat AI handles combat.
        if (_creature.AttackTarget != null)
        {
            _needsRejoin = true;
            return;
        }

        if (_creature.EmoteManager.IsBusy)
        {
            _needsRejoin = true;
            return;
        }
        if (_path == null || _path.Count == 0)
        {
            return;
        }

        // If patrol was interrupted (combat/emote), rejoin the loop at a computed waypoint.
        if (_needsRejoin)
        {
            RejoinLoopFromCurrentPosition(currentUnixTime);
            _needsRejoin = false;
        }
        // If we have an in-flight destination, see if we've arrived or if we're stuck.
        if (_currentDest != null)
        {
            var dx = _creature.Location.Pos.X - _currentDest.Pos.X;
            var dy = _creature.Location.Pos.Y - _currentDest.Pos.Y;
            var dz = _creature.Location.Pos.Z - _currentDest.Pos.Z;

            var distSq = (dx * dx) + (dy * dy) + (dz * dz);

            if (distSq > (ArriveDistance * ArriveDistance))
            {
                if (currentUnixTime >= _nextProgressSampleTime)
                {
                    _nextProgressSampleTime = currentUnixTime + 0.5;

                    if (_lastSamplePos == null)
                    {
                        _lastSamplePos = new Position(_creature.Location);
                        _stuckSinceTime = currentUnixTime;
                    }
                    else
                    {
                        var mx = _creature.Location.Pos.X - _lastSamplePos.Pos.X;
                        var my = _creature.Location.Pos.Y - _lastSamplePos.Pos.Y;
                        var mz = _creature.Location.Pos.Z - _lastSamplePos.Pos.Z;

                        var movedSq = (mx * mx) + (my * my) + (mz * mz);

                        // ~10cm movement threshold
                        if (movedSq < 0.01f)
                        {
                            if ((currentUnixTime - _stuckSinceTime) >= 2.0)
                            {
                                TryDetourOrSkip(currentUnixTime);
                            }
                        }
                        else
                        {
                            _stuckSinceTime = currentUnixTime;
                            _stuckAttempts = 0;
                            _lastSamplePos = new Position(_creature.Location);
                        }
                    }
                }

                return;
            }

            // Arrived at _currentDest
            _currentDest = null;

            // If we were detouring, immediately resume the real waypoint.
            // Never pause on detours.
            if (_detouring && _finalDest != null)
            {
                _detouring = false;
                _currentDest = _finalDest;
                IssueMove(_finalDest, currentUnixTime);
                return;
            }

            // Arrived at a real waypoint.
            if (_finalDest != null)
            {
                _finalDest = null;

                if (_pauseOnArrivalSeconds > 0f)
                {
                    // Pause, then give rotation/idle blend a moment to settle before next MoveTo()
                    _nextMoveTime = currentUnixTime + _pauseOnArrivalSeconds + 0.35;
                    _pauseOnArrivalSeconds = 0f;
                    return;
                }
            }
        }

        // Wait for pause/cooldown between legs.
        if (currentUnixTime < _nextMoveTime)
        {
            return;
        }

        // Select next offset (looping)
        var offset = _path[_index];
        _index = (_index + 1) % _path.Count;

        // Base position is the cached patrol center (original spawn/home).
        var basePos = new Position(_patrolHome);
        var nextPos = new Position(basePos);

        // 2D-only movement: compute XY from home + offsets, Z from terrain.
        nextPos.PositionX = basePos.Pos.X + offset.Dx;
        nextPos.PositionY = basePos.Pos.Y + offset.Dy;

        // Update cell before terrain lookup.
        nextPos.LandblockId = new LandblockId(nextPos.GetCell());

        // Terrain height accounts for hills.
        nextPos.PositionZ = nextPos.GetTerrainZ();

        // Update cell again after Z assignment.
        nextPos.LandblockId = new LandblockId(nextPos.GetCell());

        // Pause: fixed override on waypoint, otherwise weenie random default range.
        _pauseOnArrivalSeconds = offset.PauseSeconds ?? GetDefaultPauseSeconds();

        _finalDest = nextPos;
        _currentDest = nextPos;

        _detouring = false;
        _stuckAttempts = 0;
        _stuckSinceTime = currentUnixTime;
        _lastSamplePos = new Position(_creature.Location);

        IssueMove(nextPos, currentUnixTime);

        // Small pacing guard (pause is applied on arrival).
        _nextMoveTime = currentUnixTime + 0.75;
    }

    private float GetDefaultPauseSeconds()
    {
        // New weenie float properties:
        // 20055 = PatrolPauseMinSeconds
        // 20056 = PatrolPauseMaxSeconds
        var minObj = _creature.GetProperty(PropertyFloat.PatrolPauseMinSeconds);
        var maxObj = _creature.GetProperty(PropertyFloat.PatrolPauseMaxSeconds);

        if (minObj == null || maxObj == null)
        {
            return 0f;
        }

        var min = (float)minObj;
        var max = (float)maxObj;

        if (min < 0f)
        {
            min = 0f;
        }

        if (max < 0f)
        {
            max = 0f;
        }

        if (max < min)
        {
            var t = min;
            min = max;
            max = t;
        }

        if (max <= min)
        {
            return min;
        }

        return min + ((float)Random.Shared.NextDouble() * (max - min));
    }
    private void RejoinLoopFromCurrentPosition(double currentUnixTime)
    {
        // Clear any in-flight leg so we don't "arrive" at an old target after interruption.
        _currentDest = null;
        _finalDest = null;

        _detouring = false;
        _stuckAttempts = 0;
        _stuckSinceTime = 0;
        _lastSamplePos = null;
        _nextProgressSampleTime = 0;

        _pauseOnArrivalSeconds = 0f;
        _nextMoveTime = currentUnixTime;

        // Base position is the cached patrol center (original spawn/home).
        var basePos = new Position(_patrolHome);

        // Find the closest waypoint to our current location.
        var bestIndex = 0;
        var bestDistSq = double.MaxValue;

        for (var i = 0; i < _path.Count; i++)
        {
            var wp = BuildWaypoint(basePos, _path[i]);

            var dx = _creature.Location.Pos.X - wp.Pos.X;
            var dy = _creature.Location.Pos.Y - wp.Pos.Y;
            var dz = _creature.Location.Pos.Z - wp.Pos.Z;

            var distSq = (dx * dx) + (dy * dy) + (dz * dz);

            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestIndex = i;
            }
        }

        // Per-member deterministic offset, but keep it LOCAL (nearest / next / previous)
        // so we don't create diagonals that pass through Home.
        var count = _path.Count;
        var salt = (int)(_creature.Guid.Full % 3);

        var candidate = bestIndex;

        if (count >= 3)
        {
            if (salt == 1)
            {
                candidate = (bestIndex + 1) % count;
            }
            else if (salt == 2)
            {
                candidate = (bestIndex + count - 1) % count;
            }
        }
        else if (count == 2)
        {
            candidate = (bestIndex + (int)(_creature.Guid.Full % 2)) % 2;
        }

        _index = candidate;

    }

    private static Position BuildWaypoint(Position basePos, PatrolOffset offset)
    {
        var nextPos = new Position(basePos);

        nextPos.PositionX = basePos.Pos.X + offset.Dx;
        nextPos.PositionY = basePos.Pos.Y + offset.Dy;

        nextPos.LandblockId = new LandblockId(nextPos.GetCell());
        nextPos.PositionZ = nextPos.GetTerrainZ();
        nextPos.LandblockId = new LandblockId(nextPos.GetCell());

        return nextPos;
    }

    private void IssueMove(Position dest, double currentUnixTime)
    {
        var forceWalk = _creature.GetProperty(PropertyBool.PatrolForceWalk) == true;

        // In Creature_Navigation.GetMoveToPosition():
        // runRate <= 0 => clears MovementParams.CanRun (forces walk)
        var runRate = forceWalk ? 0.0f : _creature.GetRunRate();

        // last parameter is optional "speed" override; keep null to use default weenie locomotion
        _creature.MoveTo(dest, runRate, true, null, null);

        // After issuing a move, sample soon for stuck detection.
        _nextProgressSampleTime = currentUnixTime + 0.5;
    }
    private void TryDetourOrSkip(double currentUnixTime)
    {
        // If we're already detouring and still stuck, abort this leg and continue patrol.
        if (_detouring)
        {
            _currentDest = null;
            _finalDest = null;

            _stuckAttempts = 0;
            _pauseOnArrivalSeconds = 0f;
            _nextMoveTime = currentUnixTime + 0.25;
            return;
        }

        _stuckAttempts++;

        (float x, float y)[] offsets =
        {
            ( 2.0f,  0.0f),
            (-2.0f,  0.0f),
            ( 0.0f,  2.0f),
            ( 0.0f, -2.0f),
            ( 1.5f,  1.5f),
            (-1.5f,  1.5f),
            ( 1.5f, -1.5f),
            (-1.5f, -1.5f),
        };

        if (_stuckAttempts <= offsets.Length)
        {
            var (ox, oy) = offsets[_stuckAttempts - 1];

            var detour = new Position(_creature.Location);
            detour.PositionX = detour.Pos.X + ox;
            detour.PositionY = detour.Pos.Y + oy;

            detour.LandblockId = new LandblockId(detour.GetCell());
            detour.PositionZ = detour.GetTerrainZ();
            detour.LandblockId = new LandblockId(detour.GetCell());

            _detouring = true;
            _currentDest = detour;

            // Never pause on detours.
            _pauseOnArrivalSeconds = 0f;

            // If we're stuck on top of/against an object, detach before trying the detour move.
            _creature.PhysicsObj?.unstick_from_object();

            IssueMove(detour, currentUnixTime);

            _stuckSinceTime = currentUnixTime;
            _lastSamplePos = new Position(_creature.Location);
            return;

        }

        // Too many attempts: abandon this waypoint and continue patrol.
        _currentDest = null;
        _finalDest = null;

        _detouring = false;
        _stuckAttempts = 0;
        _pauseOnArrivalSeconds = 0f;
        _nextMoveTime = currentUnixTime + 0.25;
    }
}

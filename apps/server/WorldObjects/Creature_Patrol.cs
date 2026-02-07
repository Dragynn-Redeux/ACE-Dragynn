using ACE.Entity.Enum.Properties;
using System.Numerics;
using ACE.Entity.Enum;
using ACE.Server.WorldObjects.Patrol;
namespace ACE.Server.WorldObjects;


partial class Creature
{
    private PatrolComponent _patrol;

    public bool HasPatrol => (GetProperty(PropertyBool.PatrolEnabled) ?? false);

    private PatrolComponent Patrol
    {
        get
        {
            if (!HasPatrol)
            {
                // Patrol disabled at runtime (e.g., emote toggle).
                // Clear component state so re-enable starts clean without carrying stale waypoint state.
                _patrol = null;
                return null;
            }

            _patrol ??= new PatrolComponent(this);
            return _patrol;
        }
    }
    
    public void PatrolUpdate(double currentUnixTime)
    {
        Patrol?.Update(currentUnixTime);
    }

    /// <summary>
    /// Clears any in-flight patrol destination so patrol can resume cleanly after combat.
    /// </summary>
    public void PatrolResetDestination(double currentUnixTime = 0)
    {
        Patrol?.ResetDestination(currentUnixTime);
    }


    public void CancelMoveToForEmote()
    {
        if (PhysicsObj?.MovementManager?.MoveToManager == null)
        {
            return;
        }

        PhysicsObj.MovementManager.MoveToManager.CancelMoveTo(WeenieError.ActionCancelled);
        PhysicsObj.MovementManager.MoveToManager.FailProgressCount = 0;
        PhysicsObj.StopCompletely(false);

        EnqueueBroadcastMotion(new ACE.Server.Entity.Motion(CurrentMotionState.Stance, MotionCommand.Ready));

        IsMoving = false;
        PhysicsObj.CachedVelocity = Vector3.Zero;
    }


}

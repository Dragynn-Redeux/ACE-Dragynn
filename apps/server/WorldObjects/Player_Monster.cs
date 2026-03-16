namespace ACE.Server.WorldObjects;

/// <summary>
/// Handles player->monster visibility checks
/// </summary>
partial class Player
{
    private double lastCheckMonstersTime;
    private const double CheckMonstersInterval = 0.1;

    /// <summary>
    /// Wakes up any monsters within the applicable range
    /// </summary>
    public void CheckMonsters()
    {
        if (!Attackable || Teleporting)
        {
            return;
        }

        var now = Physics.Common.PhysicsTimer.CurrentTime;
        if (now - lastCheckMonstersTime < CheckMonstersInterval)
        {
            return;
        }

        lastCheckMonstersTime = now;

        PhysicsObj.ObjMaint.ForEachVisibleCreature(monster =>
        {
            if (monster is Player)
            {
                return;
            }

            var distSq = PhysicsObj.get_distance_sq_to_object(monster.PhysicsObj, true);

            if (
                distSq <= monster.VisualAwarenessRangeSq
                && !TestStealth(monster, distSq, $"{monster.Name} detects you! You lose stealth.")
            )
            {
                AlertMonster(monster);
            }
        });
    }

    /// <summary>
    /// Called when this player attacks a monster
    /// </summary>
    public void OnAttackMonster(Creature monster)
    {
        if (monster == null || !Attackable)
        {
            return;
        }

        /*Console.WriteLine($"{Name}.OnAttackMonster({monster.Name})");
        Console.WriteLine($"Attackable: {monster.Attackable}");
        Console.WriteLine($"Tolerance: {monster.Tolerance}");*/

        // faction mobs will retaliate against players belonging to the same faction
        if (SameFaction(monster))
        {
            monster.AddRetaliateTarget(this);
        }

        if (monster.MonsterState != State.Awake && (monster.Tolerance & PlayerCombatPet_RetaliateExclude) == 0)
        {
            monster.AttackTarget = this;
            monster.WakeUp();
        }
    }
}

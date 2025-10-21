using System.Linq;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.WorldObjects.Entity;
using Skill = ACE.Entity.Enum.Skill;

namespace ACE.Server.WorldObjects;

partial class Creature
{
    private int LastNumberOfNearbyPlayers;

    private bool SkillsSet;
    private uint BaseHealth;
    private uint BaseHeavyWeaponsSkill;
    private uint BaseDaggerSkill;
    private uint BaseStaffSkill;
    private uint BaseUnarmedSkill;
    private uint BaseBowSkill;
    private uint BaseThrownWeaponsSkill;
    private uint BaseWarMagicSkill;
    private uint BaseLifeMagicSkill;
    private uint BasePhysicalDefenseSkill;
    private uint BaseMagicDefenseSkill;

    private void HandlePlayerCountScaling()
    {
        if (!(UseNearbyPlayerScaling ?? false))
        {
            return;
        }

        var attackTargets = GetAttackTargets();
        foreach (var attackTarget in attackTargets.ToList())
        {
            if (attackTarget is not Player)
            {
                attackTargets.Remove(attackTarget);
            }
        }

        var numberOfNearbyPlayers = attackTargets.Count;

        if (numberOfNearbyPlayers <= LastNumberOfNearbyPlayers)
        {
            return;
        }

        LastNumberOfNearbyPlayers = numberOfNearbyPlayers;

        var playerThreshold = NearbyPlayerScalingThreshold ?? 0;
        var extraPlayers = numberOfNearbyPlayers - playerThreshold;

        if (extraPlayers < 1)
        {
            return;
        }

        SetBaseSkills();

        ApplyNearbyPlayerScalingToVitals(extraPlayers);

        ApplyNearbyPlayerScalingToAttack(extraPlayers);

        ApplyNearbyPlayerScalingToDefense(extraPlayers);

        CheckToSpawnPlayerScalingAdds();
    }

    private void ApplyNearbyPlayerScalingToVitals(int extraPlayers)
    {
        var multiplier = (NearbyPlayerVitalsScalingPerExtraPlayer ?? 1.0) * extraPlayers;

        ApplyHealthScaling((multiplier));
    }

    private void ApplyHealthScaling(double multiplier)
    {
        var currentMaxHealth = Health.MaxValue;
        var currentHealth = Health.Current;
        var currentHealthPercentage = (double)currentHealth / currentMaxHealth;

        Vitals[PropertyAttribute2nd.MaxHealth].StartingValue = (uint)(BaseHealth + BaseHealth * multiplier);
        Health.Current = (uint)(Health.MaxValue * currentHealthPercentage);
    }

    private void ApplyNearbyPlayerScalingToAttack(int extraPlayers)
    {
        var multiplier = (NearbyPlayerAttackScalingPerExtraPlayer ?? 1.0) * extraPlayers;

        SetNewSkill(BaseHeavyWeaponsSkill, multiplier, Skill.MartialWeapons);
        SetNewSkill(BaseDaggerSkill, multiplier, Skill.Dagger);
        SetNewSkill(BaseStaffSkill, multiplier, Skill.Staff);
        SetNewSkill(BaseUnarmedSkill, multiplier, Skill.UnarmedCombat);
        SetNewSkill(BaseBowSkill, multiplier, Skill.Bow);
        SetNewSkill(BaseThrownWeaponsSkill, multiplier, Skill.ThrownWeapon);
        SetNewSkill(BaseWarMagicSkill, multiplier, Skill.WarMagic);
        SetNewSkill(BaseLifeMagicSkill, multiplier, Skill.LifeMagic);
    }

    private void ApplyNearbyPlayerScalingToDefense(int extraPlayers)
    {
        var multiplier = (NearbyPlayerDefenseScalingPerExtraPlayer ?? 1.0) * extraPlayers;

        SetNewSkill(BasePhysicalDefenseSkill, multiplier, Skill.PhysicalDefense);
        SetNewSkill(BaseMagicDefenseSkill, multiplier, Skill.MagicDefense);
    }

    private void SetBaseSkills()
    {
        if (SkillsSet)
        {
            return;
        }

        BaseHealth = Health.MaxValue;
        BaseHeavyWeaponsSkill = GetCreatureSkill(Skill.MartialWeapons).Base;
        BaseDaggerSkill = GetCreatureSkill(Skill.Dagger).Base;
        BaseStaffSkill = GetCreatureSkill(Skill.Staff).Base;
        BaseUnarmedSkill = GetCreatureSkill(Skill.UnarmedCombat).Base;
        BaseBowSkill = GetCreatureSkill(Skill.Bow).Base;
        BaseThrownWeaponsSkill = GetCreatureSkill(Skill.ThrownWeapon).Base;
        BaseWarMagicSkill = GetCreatureSkill(Skill.WarMagic).Base;
        BaseLifeMagicSkill = GetCreatureSkill(Skill.LifeMagic).Base;
        BasePhysicalDefenseSkill = GetCreatureSkill(Skill.PhysicalDefense).Base;
        BaseMagicDefenseSkill = GetCreatureSkill(Skill.MagicDefense).Base;

        SkillsSet = true;
    }

    private void SetNewSkill(uint baseSkill, double multiplier, Skill skillType)
    {
        var newSkill = (uint)(baseSkill + baseSkill * multiplier);
        var propertiesSkill = new PropertiesSkill() { InitLevel = newSkill, SAC = SkillAdvancementClass.Trained };

        Skills[skillType] = new CreatureSkill(this, skillType, propertiesSkill);
    }

    private void CheckToSpawnPlayerScalingAdds()
    {
        if (NearbyPlayerScalingAddWcid == null)
        {
            return;
        }

        // TODO: reference NearbyPlayerScalingAddWcid to spawn extra creatures
    }
}

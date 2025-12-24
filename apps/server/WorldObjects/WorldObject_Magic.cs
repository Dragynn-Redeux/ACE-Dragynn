using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using ACE.Common;
using ACE.Database;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Factories.Entity;
using ACE.Server.Managers;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.Structure;
using ACE.Server.Physics;
using ACE.Server.Physics.Extensions;
using ACE.Server.WorldObjects.Managers;

namespace ACE.Server.WorldObjects;

partial class WorldObject
{
    public float SigilTrinketSpellDamageReduction;
    private bool _isSigilTrinketSpell = false;
    private PartialEvasion _partialEvasion;

    /// <summary>
    /// Instantly casts a spell for a WorldObject (ie. spell traps)
    /// </summary>
    public void TryCastSpell(
        Spell spell,
        WorldObject target,
        WorldObject itemCaster = null,
        WorldObject weapon = null,
        bool isWeaponSpell = false,
        bool fromProc = false,
        bool tryResist = true,
        bool showMsg = true,
        int? weaponSpellcraft = null,
        double damageMultiplier = 1.0
    )
    {
        // TODO: look into further normalizing this / caster / weapon

        // verify spell exists in database
        if (spell._spell == null)
        {
            if (target is Player targetPlayer)
            {
                targetPlayer.Session.Network.EnqueueSend(
                    new GameMessageSystemChat($"{spell.Name} spell not implemented, yet!", ChatMessageType.System)
                );
            }

            return;
        }

        if (spell.Flags.HasFlag(SpellFlags.FellowshipSpell))
        {
            if (target is not Player targetPlayer || targetPlayer.Fellowship == null)
            {
                return;
            }

            var fellows = targetPlayer.Fellowship.GetFellowshipMembers();

            foreach (var fellow in fellows.Values)
            {
                TryCastSpell_Inner(spell, fellow, itemCaster, weapon, isWeaponSpell, fromProc, tryResist, showMsg, weaponSpellcraft, damageMultiplier);
            }
        }
        else
        {
            TryCastSpell_Inner(spell, target, itemCaster, weapon, isWeaponSpell, fromProc, tryResist, showMsg, weaponSpellcraft, damageMultiplier);
        }
    }

    public void TryCastSpell_Inner(
        Spell spell,
        WorldObject target,
        WorldObject itemCaster = null,
        WorldObject weapon = null,
        bool isWeaponSpell = false,
        bool fromProc = false,
        bool tryResist = true,
        bool showMsg = true,
        int? weaponSpellcraft = null,
        double damageMultiplier = 1.0
    )
    {
        // verify before resist, still consumes source item
        if (spell.MetaSpellType == SpellType.Dispel && !VerifyDispelPkStatus(itemCaster, target))
        {
            return;
        }

        // perform resistance check, if applicable
        var weaponAttackMod = weapon?.WeaponOffense;

        if (tryResist && TryResistSpell(target, spell, out _, itemCaster, false, weaponSpellcraft, weaponAttackMod))
        {
            return;
        }

        // if not resisted, cast spell
        HandleCastSpell(spell, target, itemCaster, weapon, isWeaponSpell, fromProc, false, showMsg, false, weaponSpellcraft, damageMultiplier);
    }

    /// <summary>
    /// Instantly casts a spell for a WorldObject, with optional redirects for item enchantments
    /// </summary>
    public bool TryCastSpell_WithRedirects(
        Spell spell,
        WorldObject target,
        WorldObject itemCaster = null,
        WorldObject weapon = null,
        bool isWeaponSpell = false,
        bool fromProc = false,
        bool tryResist = true,
        double damageMultiplier = 1.0
    )
    {
        if (target is Creature creatureTarget)
        {
            var targets = GetNonComponentTargetTypes(spell, creatureTarget);

            if (targets != null)
            {
                foreach (var itemTarget in targets)
                {
                    TryCastSpell(spell, itemTarget, itemCaster, weapon, isWeaponSpell, fromProc, tryResist, true, null, damageMultiplier);
                }

                return targets.Count > 0;
            }
        }

        TryCastSpell(spell, target, itemCaster, weapon, isWeaponSpell, fromProc, tryResist, true, null, damageMultiplier);

        return true;
    }

    /// <summary>
    /// Determines whether a spell will be resisted,
    /// based upon the caster's magic skill vs target's magic defense skill
    /// </summary>
    /// <returns>TRUE if spell is resisted</returns>
    private static bool MagicDefenseCheck(
        uint casterMagicSkill,
        uint targetMagicDefenseSkill,
        out PartialEvasion partialResist,
        out float resistChance,
        Player targetPlayer
    )
    {
        // uses regular 0.03 factor, and not magic casting 0.07 factor
        var chance = (1.0 - SkillCheck.GetSkillChance((int)casterMagicSkill, (int)targetMagicDefenseSkill));
        resistChance = (float)chance;

        var resistRoll = ThreadSafeRandom.Next(0.0f, 1.0f);

        if (targetPlayer is { EvasiveStanceIsActive: true })
        {
            var luckyRoll = ThreadSafeRandom.Next(0.0f, 1.0f);
            if (luckyRoll < resistRoll)
            {
                resistRoll = luckyRoll;
            }
        }

        if (resistRoll < chance)
        {
            var partialResistRoll = ThreadSafeRandom.Next(0.0f, 1.0f);

            if (targetPlayer is { EvasiveStanceIsActive: true })
            {
                var luckyRoll = ThreadSafeRandom.Next(0.0f, 1.0f);
                if (luckyRoll < partialResistRoll)
                {
                    partialResistRoll = luckyRoll;
                }
            }

            // Roll resist type (33% for each resist type)
            const float fullResistChance = 1.0f / 3.0f;
            const float partialResistChance = fullResistChance * 2;

            switch (partialResistRoll)
            {
                case < fullResistChance:
                    partialResist = PartialEvasion.All;
                    return true;
                case < partialResistChance:
                    partialResist = PartialEvasion.Some;
                    return false;
            }
        }

        // If playerDefender has Phalanx active, 25-50% chance to convert a full hit into a partial hit, depending on shield size.
        if (targetPlayer is { PhalanxIsActive: true } && (targetPlayer.GetEquippedShield() is not null || targetPlayer.GetEquippedWeapon() is { IsTwoHanded: true}))
        {
            var phalanxChance = 0.25;

            if (targetPlayer.GetEquippedShield() is not null)
            {
                phalanxChance = targetPlayer.GetEquippedShield().ArmorStyle switch
                {
                    (int)ACE.Entity.Enum.ArmorStyle.CovenantShield => 0.5f,
                    (int)ACE.Entity.Enum.ArmorStyle.TowerShield => 0.45f,
                    (int)ACE.Entity.Enum.ArmorStyle.LargeShield => 0.4f,
                    (int)ACE.Entity.Enum.ArmorStyle.StandardShield => 0.35f,
                    (int)ACE.Entity.Enum.ArmorStyle.SmallShield => 0.3f,
                    (int)ACE.Entity.Enum.ArmorStyle.Buckler => 0.3f,
                    _ => 0.25f
                };
            }

            if (ThreadSafeRandom.Next(0.0f, 1.0f) < phalanxChance)
            {
                partialResist = PartialEvasion.Some;
                return false;
            }
        }

        partialResist = PartialEvasion.None;
        return false;
    }

    /// <summary>
    /// If this spell has a chance to be resisted, rolls for a chance
    /// Returns TRUE if spell is resistable and was resisted for this attempt
    /// </summary>
    public bool TryResistSpell(
        WorldObject target,
        Spell spell,
        out PartialEvasion partialResist,
        WorldObject itemCaster = null,
        bool projectileHit = false,
        int? weaponSpellcraft = null,
        double? weaponAttackMod = null
    )
    {
        partialResist = PartialEvasion.None;
        _partialEvasion = partialResist;

        // fix hermetic void?
        if (!spell.IsResistable && spell.Category != SpellCategory.ManaConversionModLowering || spell.IsSelfTargeted)
        {
            //if (!spell.IsResistable || spell.IsSelfTargeted)
            return false;
        }

        if (
            spell.MetaSpellType == SpellType.Dispel
            && spell.Align == DispelType.Negative
            && !PropertyManager.GetBool("allow_negative_dispel_resist").Item
        )
        {
            return false;
        }

        if (spell.NumProjectiles > 0 && !projectileHit)
        {
            return false;
        }

        if (itemCaster != null && Cloak.IsCloak(itemCaster))
        {
            return false;
        }

        uint magicSkill = 0;

        var caster = itemCaster ?? this;

        var casterCreature = caster as Creature;
        var player = this as Player;
        var targetPlayer = target as Player;

        if (casterCreature != null)
        {
            // Retrieve caster's skill level in the Magic School
            var magicSchool = spell.School;

            if (magicSchool is MagicSchool.VoidMagic)
            {
                magicSchool = MagicSchool.LifeMagic;
            }

            // Retrieve the casters Magic mods from worn armor
            if (magicSchool is MagicSchool.WarMagic or MagicSchool.LifeMagic)
            {
                magicSkill = magicSchool == MagicSchool.WarMagic
                        ? casterCreature.GetModdedWarMagicSkill()
                        : casterCreature.GetModdedLifeMagicSkill();
            }

            // Retrieve caster's secondary attribute mod (1% per 20 attributes)
            var secondaryAttributeMod = casterCreature.Focus.Current * 0.0005 + 1;

            // if proc spell or enchanted blade spell
            if (weaponSpellcraft is not null)
            {
                weaponSpellcraft += (int)CheckForArcaneLoreSpecSpellcraftBonus(casterCreature);

                var spellcraftBonus = (uint)(weaponSpellcraft * 0.1);

                magicSkill += spellcraftBonus;
            }

            if (weaponAttackMod is not null)
            {
                magicSkill = (uint)(magicSkill * weaponAttackMod);
            }

            if (player is {OverloadDischargeIsActive: true})
            {
                magicSkill *= (uint)(player.ManaChargeMeter + 1.0f);
            }

            magicSkill = (uint)(magicSkill * secondaryAttributeMod * LevelScaling.GetPlayerAttackSkillScalar(casterCreature, target as Creature));
        }
        else if (caster.ItemSpellcraft != null)
        {
            // Retrieve casting item's spellcraft
            var spellcraft = (uint)caster.ItemSpellcraft.Value;

            // When an item with spellcraft casts a spell while being wielded by a creature, average the spellcraft and wielder's magic skill
            if (caster.Wielder is Creature wielder)
            {
                var casterMagicSkill =
                    spell.School == MagicSchool.WarMagic
                        ? wielder.GetModdedWarMagicSkill()
                        : wielder.GetModdedLifeMagicSkill();

                spellcraft += CheckForArcaneLoreSpecSpellcraftBonus(wielder);

                var spellcraftBonus = (uint)(spellcraft * 0.1);

                magicSkill = casterMagicSkill + spellcraftBonus;
            }
        }
        else if (caster.Wielder is Creature wielder)
        {
            // Receive wielder's skill level in the Magic School?
            magicSkill = wielder.GetCreatureSkill(spell.School).Current;
        }

        // only creatures can resist spells?
        if (target is not Creature targetCreature)
        {
            return false;
        }

        // Retrieve target's Magic Defense Skill
        var difficulty = (uint)(targetCreature.GetModdedMagicDefSkill() * LevelScaling.GetPlayerDefenseSkillScalar(targetCreature, casterCreature));

        difficulty = Convert.ToUInt32(difficulty * (1.0f + CheckForCombatAbilityReflectMagicDefBonus(targetPlayer)));
        difficulty = Convert.ToUInt32(difficulty * (1.0f + Jewel.GetJewelEffectMod(targetPlayer, PropertyInt.GearFamiliarity, "Familiarity")));

        var resisted = MagicDefenseCheck(magicSkill, difficulty, out var pResist, out var resistChance, targetPlayer);

        partialResist = pResist;
        _partialEvasion = pResist;

        if (targetPlayer != null)
        {
            if (targetPlayer.Invincible)
            {
                resisted = true;
            }

            if (targetPlayer.UnderLifestoneProtection)
            {
                targetPlayer.HandleLifestoneProtection();
                resisted = true;
            }
        }

        if (caster == target)
        {
            resisted = false;
        }

        if (resisted)
        {
            if (player != null)
            {
                player.SendChatMessage(
                    targetCreature,
                    $"{targetCreature.Name} resists your spell",
                    ChatMessageType.Magic
                );

                player.Session.Network.EnqueueSend(new GameMessageSound(player.Guid, Sound.ResistSpell));
            }

            if (targetPlayer != null)
            {
                if (targetPlayer.Reprisal)
                {
                    targetPlayer.Reprisal = false;
                    targetPlayer.SendChatMessage(
                        this,
                        $"Reprisal! You resist the spell cast by {Name}",
                        ChatMessageType.Magic
                    );
                }
                targetPlayer.SendChatMessage(this, $"You resist the spell cast by {Name}", ChatMessageType.Magic);

                targetPlayer.Session.Network.EnqueueSend(
                    new GameMessageSound(targetPlayer.Guid, Sound.ResistSpell)
                );

                if (casterCreature != null)
                {
                    targetPlayer.SetCurrentAttacker(casterCreature);
                }

                Proficiency.OnSuccessUse(targetPlayer, targetPlayer.GetCreatureSkill(Skill.MagicDefense), magicSkill);
            }

            if (this is Creature creature)
            {
                targetCreature.EmoteManager.OnResistSpell(creature);
            }
        }

        if (player != null && player.DebugDamage.HasFlag(Creature.DebugDamageType.Attacker))
        {
            ShowResistInfo(player, this, target, spell, magicSkill, difficulty, resistChance, resisted);
        }
        if (targetCreature.DebugDamage.HasFlag(Creature.DebugDamageType.Defender))
        {
            ShowResistInfo(targetCreature, this, target, spell, magicSkill, difficulty, resistChance, resisted);
        }

        return resisted;
    }

    /// <summary>
    /// RATING - Familiarity: Bonus resist chance for having attacked target creature.
    /// Up to +20% + 1% per rating (with max quest stamps).
    /// (JEWEL - Fire Opal)
    /// </summary>
    private static float CheckForRatingFamiliaritySpellResistBonus(Player targetPlayer, Creature casterCreature)
    {
        if (targetPlayer == null || casterCreature == null)
        {
            return 1.0f;
        }

        var rating = targetPlayer.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearFamiliarity);

        if (rating <= 0)
        {
            return 1.0f;
        }

        if (!casterCreature.QuestManager.HasQuest($"{targetPlayer.Name},Familiarity"))
        {
            return 1.0f;
        }

        var rampPercentage = Math.Clamp((float)casterCreature.QuestManager.GetCurrentSolves($"{targetPlayer.Name},Familiarity") / 100, 0.0f, 1.0f);

        const float baseMod = 0.2f;
        const float bonusPerRating = 0.01f;

        return rampPercentage * (baseMod + bonusPerRating * rating);
    }

    /// <summary>
    /// COMBAT ABILITY - Reflect: Gain 50% increased magic defense while attempting to resist a spell
    /// </summary>
    private static float CheckForCombatAbilityReflectMagicDefBonus(Player targetPlayer)
    {
        return targetPlayer is { ReflectIsActive: true } ? 0.5f : 0.0f;
    }

    /// <summary>
    /// SPEC BONUS - Arcane Lore: 50% of skill is added to Spellcraft
    /// </summary>
    public static uint CheckForArcaneLoreSpecSpellcraftBonus(Creature wielder)
    {
        if (wielder == null)
        {
            return 0;
        }

        var arcaneLore = wielder.GetCreatureSkill(Skill.ArcaneLore);

        if (arcaneLore.AdvancementClass == SkillAdvancementClass.Specialized)
        {
            return (uint)(arcaneLore.Current * 0.5);
        }

        return 0;
    }

    private static void ShowResistInfo(
        Creature observed,
        WorldObject attacker,
        WorldObject defender,
        Spell spell,
        uint attackSkill,
        uint defenseSkill,
        float resistChance,
        bool resisted
    )
    {
        var targetInfo = PlayerManager.GetOnlinePlayer(observed.DebugDamageTarget);

        if (targetInfo == null)
        {
            observed.DebugDamage = Creature.DebugDamageType.None;
            return;
        }

        // initial info / resist chance
        var info = $"Attacker: {attacker.Name} ({attacker.Guid})\n";
        info += $"Defender: {defender.Name} ({defender.Guid})\n";

        info += $"CombatType: Magic\n";

        info += $"Spell: {spell.Name} ({spell.Id})\n";

        info += $"EffectiveAttackSkill: {attackSkill}\n";
        info += $"EffectiveDefenseSkill: {defenseSkill}\n";

        info += $"ResistChance: {resistChance}\n";

        info += $"Resisted: {resisted}";

        if (resisted || spell.NumProjectiles == 0)
        {
            targetInfo.Session.Network.EnqueueSend(new GameMessageSystemChat(info, ChatMessageType.Broadcast));
        }
        else
        {
            targetInfo.DebugDamageBuffer = $"{info}\n";
        }
    }

    /// <summary>
    /// Creates a spell based on MetaSpellType
    /// </summary>
    protected bool HandleCastSpell(
        Spell spell,
        WorldObject target,
        WorldObject itemCaster = null,
        WorldObject weapon = null,
        bool isWeaponSpell = false,
        bool fromProc = false,
        bool equip = false,
        bool showMsg = true,
        bool sigilTrinketSpell = false,
        int? weaponSpellcraft = null,
        double damageMultiplier = 1.0
    )
    {
        _isSigilTrinketSpell = sigilTrinketSpell;

        var targetCreature = !spell.IsSelfTargeted || spell.IsFellowshipSpell ? target as Creature : this as Creature;

        if (this is Gem || this is Food || this is Hook)
        {
            targetCreature = target as Creature;
        }

        //Player playerCaster = itemCaster as Player;

        if (spell.School == MagicSchool.LifeMagic || spell.MetaSpellType == SpellType.Dispel)
        {
            // NonComponentTargetType should be 0 for untargeted spells.
            // Return if the spell type is targeted with no target defined or the target is already dead.
            if (
                (targetCreature == null || !targetCreature.IsAlive)
                && spell.NonComponentTargetType != ItemType.None
                && spell.DispelSchool != MagicSchool.PortalMagic
            )
            {
                return false;
            }
        }

        // Sigil Scarabs
        if (this is Player)
        {
            if (targetCreature != null && !equip)
            {
                var player = this as Player;
                switch (spell.School)
                {
                    case MagicSchool.LifeMagic:
                        player?.CheckForSigilTrinketOnCastEffects(targetCreature, spell, true, Skill.LifeMagic, SigilTrinketLifeWarMagicEffect.Intensity, null, false, _isSigilTrinketSpell);
                        player?.CheckForSigilTrinketOnCastEffects(targetCreature, spell, true, Skill.LifeMagic, SigilTrinketLifeWarMagicEffect.Shielding, null, false, _isSigilTrinketSpell);
                        player?.CheckForSigilTrinketOnCastEffects(targetCreature, spell, true, Skill.LifeMagic, SigilTrinketLifeMagicEffect.CastProt, null, false, _isSigilTrinketSpell);
                        player?.CheckForSigilTrinketOnCastEffects(targetCreature, spell, true, Skill.LifeMagic, SigilTrinketLifeMagicEffect.CastVuln, null, false, _isSigilTrinketSpell);
                        player?.CheckForSigilTrinketOnCastEffects(targetCreature, spell, true, Skill.LifeMagic, SigilTrinketLifeMagicEffect.CastItemBuff, null, false, _isSigilTrinketSpell);
                        player?.CheckForSigilTrinketOnCastEffects(targetCreature, spell, true, Skill.LifeMagic, SigilTrinketLifeMagicEffect.CastVitalRate, null, false, _isSigilTrinketSpell);
                        break;
                    case MagicSchool.WarMagic:
                        player?.CheckForSigilTrinketOnCastEffects(targetCreature, spell, true, Skill.WarMagic, SigilTrinketLifeWarMagicEffect.Intensity, null, false, _isSigilTrinketSpell);
                        player?.CheckForSigilTrinketOnCastEffects(targetCreature, spell, true, Skill.WarMagic, SigilTrinketLifeWarMagicEffect.Shielding, null, false, _isSigilTrinketSpell);
                        player?.CheckForSigilTrinketOnCastEffects(targetCreature, spell, true, Skill.WarMagic, SigilTrinketWarMagicEffect.Duplicate, null, false, _isSigilTrinketSpell);
                        break;
                }
            }
        }

        switch (spell.MetaSpellType)
        {
            case SpellType.Enchantment:
            case SpellType.FellowEnchantment:

                if (itemCaster == null && targetCreature != null)
                {
                    GenerateSupportSpellThreat(spell, targetCreature);
                }

                var playerCaster = this as Player;

                if (playerCaster is { OverloadStanceIsActive: true } or { BatteryStanceIsActive: true } &&
                    spell.School is MagicSchool.VoidMagic &&
                    targetCreature != playerCaster)
                {
                    playerCaster.IncreaseChargedMeter(spell);
                }
                
                // TODO: replace with some kind of 'rootOwner unless equip' concept?
                if (itemCaster != null && (equip || itemCaster is Gem || itemCaster is Food))
                {
                    CreateEnchantment(targetCreature ?? target, itemCaster, itemCaster, spell, equip, false, showMsg);
                }
                else
                {
                    CreateEnchantment(targetCreature ?? target, this, this, spell, equip, false, showMsg);
                }

                break;

            case SpellType.Boost:
            case SpellType.FellowBoost:

                HandleCastSpell_Boost(spell, targetCreature, fromProc, showMsg, weapon, damageMultiplier);
                break;

            case SpellType.Transfer:

                if (itemCaster == null && targetCreature != null)
                {
                    GenerateSupportSpellThreat(spell, targetCreature);
                }

                HandleCastSpell_Transfer(spell, targetCreature, showMsg, weapon);
                break;

            case SpellType.Projectile:
            case SpellType.LifeProjectile:
            case SpellType.EnchantmentProjectile:

                HandleCastSpell_Projectile(spell, targetCreature, itemCaster, weapon, isWeaponSpell, fromProc, weaponSpellcraft, damageMultiplier);
                break;

            case SpellType.PortalLink:

                HandleCastSpell_PortalLink(spell, target);
                break;

            case SpellType.PortalRecall:

                HandleCastSpell_PortalRecall(spell, targetCreature);
                break;

            case SpellType.PortalSummon:

                HandleCastSpell_PortalSummon(spell, targetCreature, itemCaster);
                break;

            case SpellType.PortalSending:

                HandleCastSpell_PortalSending(spell, targetCreature, itemCaster);
                break;

            case SpellType.FellowPortalSending:

                HandleCastSpell_FellowPortalSending(spell, targetCreature, itemCaster);
                break;

            case SpellType.Dispel:
            case SpellType.FellowDispel:

                if (itemCaster == null && targetCreature != null)
                {
                    GenerateSupportSpellThreat(spell, targetCreature);
                }

                HandleCastSpell_Dispel(spell, targetCreature ?? target, showMsg);
                break;

            default:

                if (this is Player player)
                {
                    player.Session.Network.EnqueueSend(
                        new GameMessageSystemChat("Spell not implemented, yet!", ChatMessageType.Magic)
                    );
                }

                return false;
        }

        // play spell effects
        DoSpellEffects(spell, this, target);

        return true;
    }

    /// <summary>
    /// Plays the caster/target effects for a spell
    /// </summary>
    protected static void DoSpellEffects(Spell spell, WorldObject caster, WorldObject target, bool projectileHit = false)
    {
        if (spell.CasterEffect != 0 && (!spell.IsProjectile || !projectileHit))
        {
            caster.EnqueueBroadcast(new GameMessageScript(caster.Guid, spell.CasterEffect, spell.Formula.Scale));
        }

        if (spell.TargetEffect == 0 || (spell.IsProjectile && !projectileHit))
        {
            return;
        }

        if (!spell.IsFellowshipSpell && (spell.IsSelfTargeted || spell.Id == 5206)) // Surge of Protection
        {
            target = caster;
        }
        else if (target == null)
        {
            _log.Warning("DoSpellEffects(spell = {Spell}, caster = {Caster}, target = null, projectileHit = {ProjectileHit}) - Target is null.", spell, caster, projectileHit);
            return;
        }

        var targetBroadcaster = target.Wielder ?? target;

        targetBroadcaster.EnqueueBroadcast(
            new GameMessageScript(target.Guid, spell.TargetEffect, spell.Formula.Scale)
        );
    }

    /// <summary>
    /// Handles casting SpellType.Enchantment / FellowEnchantment spells
    /// this is also called if SpellType.EnchantmentProjectile successfully hits
    /// </summary>
    public void CreateEnchantment(
        WorldObject target,
        WorldObject caster,
        WorldObject weapon,
        Spell spell,
        bool equip = false,
        bool fromProc = false,
        bool showMsg = true
    )
    {
        // weird itemCaster -> caster collapsing going on here -- fixme
        
        var player = this as Player;
        var targetCreature = target as Creature;

        var aetheriaProc = false;
        var cloakProc = false;

        // technically unsafe, should be using fromProc
        if (caster.ProcSpell == spell.Id)
        {
            if (caster is Gem && Aetheria.IsAetheria(caster.WeenieClassId))
            {
                caster = this;
                aetheriaProc = true;
            }
            else if (Cloak.IsCloak(caster))
            {
                caster = this;
                cloakProc = true;
            }
        }
        else if (fromProc)
        {
            // fromProc is assumed to be cloakProc currently
            // todo: change fromProc from bool to WorldObject
            // do we need separate concepts for itemCaster and fromProc objects?
            caster = this;
            cloakProc = true;
        }

        // create enchantment
        var addResult = target.EnchantmentManager.Add(spell, caster, weapon, equip);

        // Ward reduction of Creature and Life debuffs
        if (target is Player && spell.Id != (uint)SpellId.Vitae)
        {
            //Console.WriteLine("enchantment target player: " + target.Name);
            var targetPlayer = target as Player;

            var wardBuffDebuffMod = EnchantmentManager.GetWardMultiplicativeMod();

            var targetPlayerWard = targetPlayer.GetWardLevel() * wardBuffDebuffMod;

            if (addResult.Enchantment.StatModValue < 0 && targetPlayerWard > 0)
            {
                //Console.WriteLine($"StatModValue Before: {addResult.Enchantment.StatModType} {addResult.Enchantment.StatModValue} {addResult.Enchantment.Duration}\n" +
                //    $" -Target Ward Level: {targetPlayer.GetWardLevel()}");

                var ignoreWardMod = 1.0f;

                if (player != null)
                {
                    ignoreWardMod = player.GetIgnoreWardMod(weapon);

                    if (weapon != null && weapon.HasImbuedEffect(ImbuedEffectType.WardRending))
                    {
                        ignoreWardMod -= GetWardRendingMod(player.GetCreatureSkill(Skill.LifeMagic));
                    }

                    ignoreWardMod *= 1.0f - Jewel.GetJewelEffectMod(player, PropertyInt.GearWardPen, "WardPen");
                }

                var wardMod = GetWardMod(caster as Creature, targetPlayer, ignoreWardMod);

                wardMod += (1 - wardMod) * 0.5f;

                //addResult.Enchantment.StatModValue *= wardMod;
                addResult.Enchantment.Duration *= wardMod;
                //Console.WriteLine($"StatModValue After: {addResult.Enchantment.StatModType} {addResult.Enchantment.StatModValue} {addResult.Enchantment.Duration}");
            }
        }

        // build message
        var suffix = "";
        switch (addResult.StackType)
        {
            case StackType.Surpass:
                suffix = $", surpassing {addResult.SurpassSpell.Name}";
                break;
            case StackType.Refresh:
                suffix = $", refreshing {addResult.RefreshSpell.Name}";
                break;
            case StackType.Surpassed:
                suffix = $", but it is surpassed by {addResult.SurpassedSpell.Name}";
                break;
        }

        if (aetheriaProc)
        {
            var message = new GameMessageSystemChat(
                $"Aetheria surges on {target.Name} with the power of {spell.Name}!",
                ChatMessageType.Spellcasting
            );

            EnqueueBroadcast(message, LocalBroadcastRange, ChatMessageType.Spellcasting);
        }
        else if (player != null && !cloakProc)
        {
            // TODO: replace with some kind of 'rootOwner unless equip' concept?
            // for item casters where the message should be 'You cast', we still need pass the caster as item
            // down this far, to prevent using player's AugmentationIncreasedSpellDuration
            var casterCheck = caster == this || caster is Gem || caster is Food;

            if (casterCheck || target == this || caster != target)
            {
                var chargedPercent = Math.Round(player.ManaChargeMeter * 100);
                var chargedMsg = player is { OverloadStanceIsActive: true } or { BatteryStanceIsActive: true } ? $"{chargedPercent}% Charged! " : "";

                chargedMsg = player switch
                {
                    { OverloadDischargeIsActive: true } => "Overload Discharge! ",
                    { BatteryDischargeIsActive: true } => "Battery Discharge! ",
                    _ => chargedMsg
                };

                var casterName = casterCheck ? "You" : caster.Name;
                var targetName = target.Name;
                if (target == this)
                {
                    targetName = casterCheck ? "yourself" : "you";
                    chargedMsg = "";
                }

                if (showMsg)
                {
                    player.SendChatMessage(
                        player,
                        $"{chargedMsg}{casterName} cast {spell.Name} on {targetName}{suffix}",
                        ChatMessageType.Magic
                    );
                }
            }
        }

        var playerTarget = target as Player;

        if (playerTarget != null)
        {
            playerTarget.Session.Network.EnqueueSend(
                new GameEventMagicUpdateEnchantment(
                    playerTarget.Session,
                    new Enchantment(playerTarget, addResult.Enchantment)
                )
            );

            playerTarget.HandleSpellHooks(spell);

            if (!spell.IsBeneficial && this is Creature creatureCaster)
            {
                playerTarget.SetCurrentAttacker(creatureCaster);
            }
        }

        if (playerTarget == null && target.Wielder is Player wielder)
        {
            playerTarget = wielder;
        }

        if (playerTarget == null || playerTarget == this || cloakProc)
        {
            return;
        }

        {
            var targetName = target == playerTarget ? "you" : $"your {target.Name}";

            if (showMsg)
            {
                playerTarget.SendChatMessage(
                    this,
                    $"{caster.Name} cast {spell.Name} on {targetName}{suffix}",
                    ChatMessageType.Magic
                );
            }
        }
    }

    /// <summary>
    /// Handles casting SpellType.Boost / FellowBoost spells
    /// typically for Life Magic, ie. Heal, Harm
    /// </summary>
    private void HandleCastSpell_Boost(
        Spell spell,
        Creature targetCreature,
        bool fromProc,
        bool showMsg = true,
        WorldObject weapon = null,
        double damageMultiplier = 1.0
    )
    {
        var player = this as Player;
        var creature = this as Creature;
        var targetPlayer = targetCreature as Player;

        // prevent double deaths from indirect casts
        // caster is already checked in player/monster, and re-checking caster here would break death emotes such as bunny smite
        if (targetCreature != null && targetCreature.IsDead)
        {
            return;
        }

        // handle negatives?
        var minBoostValue = Math.Min(spell.Boost, spell.MaxBoost);
        var maxBoostValue = Math.Max(spell.Boost, spell.MaxBoost);

        var resistanceType =
            minBoostValue > 0
                ? GetBoostResistanceType(spell.VitalDamageType)
                : GetDrainResistanceType(spell.VitalDamageType);

        double? weaponRestorationMod = 1.0;
        if (weapon is { WeaponRestorationSpellsMod: > 1 })
        {
            weaponRestorationMod = weapon.WeaponRestorationSpellsMod;
        }

        // Resist
        if (targetPlayer is { ReflectIsActive: true, ReflectFirstSpell: true })
        {
            CheckForCombatAbilityReflectSpell(true, targetPlayer, this as Creature, spell);
            targetPlayer.ReflectFirstSpell = false;
        }
        else
        {
            CheckForCombatAbilityReflectSpell(_partialEvasion is PartialEvasion.All or PartialEvasion.Some, targetPlayer, this as Creature, spell);
        }

        var resistedMod = GetResistedMod(_partialEvasion);

        var selfTargetProcSpellMod = SelfTargetSpellProcMod(fromProc, spell, weapon, player);

        var tryBoost = (int)(
            ThreadSafeRandom.Next(minBoostValue, maxBoostValue) * weaponRestorationMod * selfTargetProcSpellMod
        );

        // Boost Crits
        var critMessage = "";
        var critChance = 0.1;
        if (weapon != null && weapon.CriticalFrequency != null)
        {
            critChance += (double)weapon.CriticalFrequency;
        }

        var critMultiplier = 1.5f;
        if (weapon?.GetProperty(PropertyFloat.CriticalMultiplier) != null)
        {
            var weaponCritMulti = (float)weapon.GetProperty(PropertyFloat.CriticalMultiplier);
            critMultiplier += (weaponCritMulti / 1.5f);
        }

        var roll = ThreadSafeRandom.Next(0.0f, 1.0f);

        if (critChance > roll)
        {
            tryBoost = (int)(tryBoost * critMultiplier);
            critMessage = "Critical! ";
        }

        if (targetCreature != this && tryBoost < 0)
        {
            var damageRating = creature?.GetDamageRating() ?? 0;
            var damageRatingMod = Creature.AdditiveCombine(Creature.GetPositiveRatingMod(damageRating));

            tryBoost = (int)(tryBoost * damageRatingMod);
        }

        if (targetCreature == null)
        {
            return;
        }

        tryBoost = (int)Math.Round(tryBoost * targetCreature.GetResistanceMod(resistanceType));

        int boost;

        // handle cloak damage proc for harm other
        var equippedCloak = targetCreature.EquippedCloak;

        if (targetCreature != this && spell.VitalDamageType == DamageType.Health && tryBoost < 0)
        {
            var percent = (float)-tryBoost / targetCreature.Health.MaxValue;

            if (equippedCloak != null && Cloak.HasDamageProc(equippedCloak) &&
                Cloak.RollProc(equippedCloak, percent))
            {
                var reduced = -Cloak.GetReducedAmount(this, -tryBoost);

                Cloak.ShowMessage(targetCreature, this, -tryBoost, -reduced);

                tryBoost = reduced;
            }
        }

        var overloadMod = CheckForCombatAbilityOverloadDamageBonus(player);
        var batterMod = CheckForCombatAbilityBatteryDamagePenalty(player);

        var spellcraftMod = 1.0f;
        if (fromProc && weapon?.ItemSpellcraft != null)
        {
            var spellcraft = weapon.ItemSpellcraft.Value + CheckForArcaneLoreSpecSpellcraftBonus(player);
            spellcraftMod += spellcraft * 0.01f;
        }

        // for traps and creatures that don't have a lethality mod,
        // make sure they receive multipliers from landblock mods
        var landblockScalingMod = 1.0f;
        if (ArchetypeLethality is null && CurrentLandblock is not null)
        {
            landblockScalingMod *= (1.0f + (float)CurrentLandblock.GetLandblockLethalityMod());
        }

        tryBoost = (int)(tryBoost * overloadMod * batterMod * damageMultiplier * spellcraftMod * landblockScalingMod * resistedMod);

        string srcVital;

        if (tryBoost > 0) // heal
        {
            // increases
            tryBoost = Convert.ToInt32(tryBoost * (1.0f + Jewel.GetJewelEffectMod(player, PropertyInt.GearSelflessness)));
            tryBoost = Convert.ToInt32(tryBoost * (1.0f + Jewel.GetJewelEffectMod(player, PropertyInt.GearHealBubble)));

            // reductions
            tryBoost = Convert.ToInt32(tryBoost * (1.0f - Jewel.GetJewelEffectMod(player, PropertyInt.GearVitalsTransfer)));

            // Recent Void Spell Cast Mod - Lower all healing spell effects for 10 seconds.
            if (player is not null && player.LastVoidSpellCastTime + player.LastVoidSpellCastPenaltyLength > Time.GetUnixTime())
            {
                tryBoost = Convert.ToInt32(tryBoost * 0.5f);
            }
        }
        else // harm
        {
            // increases
            tryBoost = Convert.ToInt32(tryBoost * (1.0f + Jewel.GetJewelRedFury(player)));
            tryBoost = Convert.ToInt32(tryBoost * (1.0f + Jewel.GetJewelBlueFury(player)));
            tryBoost = Convert.ToInt32(tryBoost * (1.0f + Jewel.GetJewelEffectMod(player, PropertyInt.GearSelfHarm)));

            var attributeMod = creature?.GetAttributeMod(weapon, true) ?? 1.0f;
            tryBoost = Convert.ToInt32(tryBoost * attributeMod);

            // reductions
            tryBoost = Convert.ToInt32(tryBoost * (1.0f - Jewel.GetJewelEffectMod(targetPlayer, PropertyInt.GearNullification,"Nullification")));

            // ward
            var ignoreWardMod = 1.0f - Jewel.GetJewelEffectMod(player, PropertyInt.GearWardPen, "WardPen");
            var wardMod = GetWardMod(player, targetCreature, ignoreWardMod);

            tryBoost = Convert.ToInt32(tryBoost * wardMod);
        }

        ResetRatingElementalistQuestStamps(player);

        if (creature is not null)
        {
            var archetypeSpellDamageMod = (float)(creature.ArchetypeSpellDamageMultiplier ?? 1.0);
            tryBoost = Convert.ToInt32(tryBoost * archetypeSpellDamageMod);
        }

        // LEVEL SCALING - Reduces harms against enemies, and restoration for players
        var scalar = LevelScaling.GetPlayerBoostSpellScalar(player, targetCreature);
        tryBoost = (int)(tryBoost * scalar);

        SigilTrinketSpellDamageReduction = 1.0f;
        targetPlayer?.CheckForSigilTrinketOnSpellHitReceivedEffects(this, spell, tryBoost, Skill.MagicDefense, SigilTrinketMagicDefenseEffect.Absorption);
        tryBoost = Convert.ToInt32(tryBoost * SigilTrinketSpellDamageReduction);

        switch (spell.VitalDamageType)
        {
            case DamageType.Mana:
                boost = targetCreature.UpdateVitalDelta(targetCreature.Mana, tryBoost);
                srcVital = "mana";
                break;
            case DamageType.Stamina:
                boost = targetCreature.UpdateVitalDelta(targetCreature.Stamina, tryBoost);
                srcVital = "stamina";
                break;
            default: // Health
                boost = targetCreature.UpdateVitalDelta(targetCreature.Health, tryBoost);
                srcVital = "health";

                if (boost >= 0 && !targetCreature.IsMonster)
                {
                    targetCreature.DamageHistory.OnHeal((uint)boost);
                    GenerateSupportSpellThreat(spell, targetCreature, boost);

                    if (player is { OverloadStanceIsActive: true } or {BatteryStanceIsActive: true} && boost > 0)
                    {
                        player.IncreaseChargedMeter(spell);
                    }
                }
                else if (creature is { IsMonster: false } && targetCreature.IsMonster)
                {
                    targetCreature.DamageHistory.Add(this, DamageType.Health, (uint)-boost);
                    targetCreature.IncreaseTargetThreatLevel(player, boost);

                    if (player is { OverloadStanceIsActive: true } or {BatteryStanceIsActive: true} && boost < 0)
                    {
                        player.IncreaseChargedMeter(spell);
                    }
                }
                break;
        }

        if (boost < 0)
        {
            HandlePostDamageRatingEffects(targetCreature, boost, player, targetPlayer, creature, spell, ProjectileSpellType.Undef);
        }
        else if (boost > 0)
        {
            HandlePostHealRatingEffects(player, targetPlayer);
        }

        var partialResist = _partialEvasion == PartialEvasion.Some ? "Partial Resist! " : "";

        if (player != null)
        {
            string casterMessage;

            var chargedPercent = Math.Round(player.ManaChargeMeter * 100);
            var chargedMsg = player is {OverloadStanceIsActive: true} or {BatteryStanceIsActive: true} ? $"{chargedPercent}% Charged! " : "";

            chargedMsg = player switch
            {
                { OverloadDischargeIsActive: true } => "Overload Discharge! ",
                { BatteryDischargeIsActive: true } => "Battery Discharge! ",
                _ => chargedMsg
            };

            if (player != targetCreature)
            {
                if (spell.IsBeneficial)
                {
                    casterMessage = $"{partialResist}{chargedMsg}{critMessage}With {spell.Name} you restore {boost} points of {srcVital} to {targetCreature.Name}.";
                }
                else
                {
                    casterMessage = $"{partialResist}{chargedMsg}{critMessage}With {spell.Name} you drain {Math.Abs(boost)} points of {srcVital} from {targetCreature.Name}.";
                }
            }
            else
            {
                var verb = spell.IsBeneficial ? "restore" : "drain";

                casterMessage = $"{partialResist}{chargedMsg}{critMessage}You cast {spell.Name} and {verb} {Math.Abs(boost)} points of your {srcVital}.";
            }

            if (showMsg)
            {
                player.SendChatMessage(player, casterMessage, ChatMessageType.Magic);
            }
        }

        if (targetPlayer != null && player != targetPlayer)
        {
            string targetMessage;

            if (spell.IsBeneficial)
            {
                targetMessage = $"{partialResist}{critMessage}{Name} casts {spell.Name} and restores {boost} points of your {srcVital}.";
            }
            else
            {
                targetMessage = $"{partialResist}{critMessage}{Name} casts {spell.Name} and drains {Math.Abs(boost)} points of your {srcVital}.";

                if (creature != null)
                {
                    targetPlayer.SetCurrentAttacker(creature);
                }
            }

            if (showMsg)
            {
                targetPlayer.SendChatMessage(player, targetMessage, ChatMessageType.Magic);
            }
        }

        if (targetCreature.IsAlive && spell.VitalDamageType == DamageType.Health &&
            boost < 0)
        {
            // handle cloak spell proc
            if (equippedCloak != null && Cloak.HasProcSpell(equippedCloak))
            {
                var pct = (float)-boost / targetCreature.Health.MaxValue;

                // ensure message is sent after enchantment.Message
                var actionChain = new ActionChain();
                actionChain.AddDelayForOneTick();
                actionChain.AddAction(this, () => Cloak.TryProcSpell(targetCreature, this, equippedCloak, pct));
                actionChain.EnqueueChain();
            }

            // ensure emote process occurs after damage msg
            var emoteChain = new ActionChain();
            emoteChain.AddDelayForOneTick();
            emoteChain.AddAction(targetCreature, () => targetCreature.EmoteManager.OnDamage(creature));
            //if (critical)
            //    emoteChain.AddAction(target, () => target.EmoteManager.OnReceiveCritical(creature));
            emoteChain.EnqueueChain();
        }

        HandleBoostTransferDeath(creature, targetCreature);
    }

    /// <summary>
    /// COMBAT ABILITY - Reflect: Reflect resisted spells back to the caster.
    /// </summary>
    private void CheckForCombatAbilityReflectSpell(bool resisted, Player targetPlayer, Creature sourceCreature, Spell spell)
    {
        if (!resisted || targetPlayer == null || sourceCreature == null || targetPlayer == sourceCreature || spell.IsBeneficial)
        {
            return;
        }

        if (targetPlayer.ReflectIsActive)
        {
            targetPlayer.TryCastSpell(spell, sourceCreature, null, null, false, false, false);
        }
    }

    /// <summary>
    /// If resist succeeded, determine if resist was partial or full.
    /// </summary>
    private float GetResistedMod(PartialEvasion partialEvasion)
    {
        switch (partialEvasion)
        {
            case PartialEvasion.None:
                return 1.0f;
            case PartialEvasion.Some:
                return 0.5f;
            case PartialEvasion.All:
            default:
                return 0.0f;
        }
    }

    private static void HandlePostDamageRatingEffects(Creature target, float damage, Player sourcePlayer, Player targetPlayer, Creature sourceCreature, Spell spell, ProjectileSpellType projectileSpellType)
    {
        if (sourcePlayer != null)
        {
            Jewel.HandleCasterAttackerRampingQuestStamps(sourcePlayer, target, spell, projectileSpellType);
            Jewel.HandlePlayerAttackerBonuses(sourcePlayer, target, damage, spell.DamageType);
        }

        if (targetPlayer != null)
        {
            Jewel.HandleCasterDefenderRampingQuestStamps(targetPlayer, sourceCreature);
            Jewel.HandlePlayerDefenderBonuses(targetPlayer, sourceCreature, damage);
        }
    }

    private static void HandlePostHealRatingEffects(Player sourcePlayer, Player targetPlayer)
    {
        if (sourcePlayer != null && targetPlayer != null)
        {
             Jewel.HandlePlayerHealerBonuses(sourcePlayer, targetPlayer);
        }
    }

    private static void ResetRatingElementalistQuestStamps(Player player)
    {
        // JEWEL - Elementalist Reset
        if (player != null)
        {
            if (player.QuestManager.HasQuest($"{player.Name},Elementalist"))
            {
                player.QuestManager.Erase($"{player.Name},Elementalist");
            }
        }
    }

    /// <summary>
    /// RATING - Nullification: Ramping boost spell defense.
    /// Spell damage taken reduced by up to 20% + 1% per rating. (after quest stamp build up of 25% per spell hit received)
    /// (JEWEL - Amethyst)
    /// </summary>
    private static float CheckForRatingNullificationBoostDefenseBonus(Creature targetCreature)
    {
        if (targetCreature is not Player targetPlayer)
        {
            return 1.0f;
        }

        if (targetPlayer.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearNullification) <= 0)
        {
            return 1.0f;
        }

        const float baseMod = 0.2f;
        const float bonusPerRating = 0.01f;
        var rating = targetPlayer.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearNullification);
        var finalRating = baseMod + bonusPerRating * rating;

        var rampMod = (float)targetPlayer.QuestManager.GetCurrentSolves($"{targetPlayer.Name},Nullification") / 100;

        return 1.0f - rampMod * finalRating;
    }

    /// <summary>
    /// RATING - Heal Bubble: 10% + 0.5% per rating to restoration, chance of healing hotspot.
    /// (JEWEL - White Jade)
    /// </summary>
    private static float CheckForRatingHealBubbleHotspot(Spell spell, Creature targetCreature, WorldObject weapon, Player player)
    {
        if (player == null)
        {
            return 1.0f;
        }

        if (player.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearHealBubble) <= 0 || targetCreature.IsMonster)
        {
            return 1.0f;
        }

        if (weapon is { Tier: not null })
        {
            Hotspot.TryGenHotspot(player, targetCreature, (int)weapon.Tier, spell.VitalDamageType);
        }

        const float baseMod = 1.1f;
        const float bonusPerRating = 0.005f;
        var equippedRating = player.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearHealBubble);

        return baseMod + equippedRating * bonusPerRating;
    }

    /// <summary>
    /// RATING - Selflessness: +2% Boost bonus per rating to others, -2% penalty to self.
    /// (JEWEL - Lavender Jade)
    /// </summary>
    private float CheckForRatingSelflessnessBoostMod(Creature targetCreature, Player player)
    {
        if (player == null)
        {
            return 1.0f;
        }

        if (player.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearSelflessness) <= 0 || targetCreature.IsMonster)
        {
            return 1.0f;
        }

        var ratingMod = player.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearSelflessness) * 0.02f;

        if (targetCreature == this)
        {
            return 1.0f - ratingMod;
        }

        return 1.0f + ratingMod;
    }

    /// <summary>
    /// COMBAT ABILITY - Overload: Increased effectiveness up to 20% with Overload Charged stacks
    /// </summary>
    private static float CheckForCombatAbilityOverloadDamageBonus(Player player)
    {
        return player switch
        {
            { OverloadDischargeIsActive: true } => 1.0f + player.DischargeLevel,
            { OverloadStanceIsActive: true } => 1.0f + player.ManaChargeMeter * 0.2f,
            _ => 1.0f
        };
    }

    /// <summary>
    /// COMBAT ABILITY - Battery: Reduced effectiveness up to 10% with Battery Charged stacks
    /// </summary>
    private static float CheckForCombatAbilityBatteryDamagePenalty(Player player)
    {
        return player is { BatteryStanceIsActive: true } ? 1.0f - player.ManaChargeMeter * 0.1f : 1.0f;
    }

    /// <summary>
    /// Returns the boost resistance for a damage type
    /// </summary>
    private static ResistanceType GetBoostResistanceType(DamageType damageType)
    {
        switch (damageType)
        {
            case DamageType.Health:
                return ResistanceType.HealthBoost;
            case DamageType.Stamina:
                return ResistanceType.StaminaBoost;
            case DamageType.Mana:
                return ResistanceType.ManaBoost;
            default:
                return ResistanceType.Undef;
        }
    }

    /// <summary>
    /// Returns the drain resistance for a damage type
    /// </summary>
    private static ResistanceType GetDrainResistanceType(DamageType damageType)
    {
        switch (damageType)
        {
            case DamageType.Health:
                return ResistanceType.HealthDrain;
            case DamageType.Stamina:
                return ResistanceType.StaminaDrain;
            case DamageType.Mana:
                return ResistanceType.ManaDrain;
            default:
                return ResistanceType.Undef;
        }
    }

    /// <summary>
    /// Returns the boost resistance type for a vital
    /// </summary>
    private static ResistanceType GetBoostResistanceType(PropertyAttribute2nd vital)
    {
        switch (vital)
        {
            case PropertyAttribute2nd.Health:
                return ResistanceType.HealthBoost;
            case PropertyAttribute2nd.Stamina:
                return ResistanceType.StaminaBoost;
            case PropertyAttribute2nd.Mana:
                return ResistanceType.ManaBoost;
            default:
                return ResistanceType.Undef;
        }
    }

    /// <summary>
    /// Returns the drain resistance type for a vital
    /// </summary>
    private static ResistanceType GetDrainResistanceType(PropertyAttribute2nd vital)
    {
        switch (vital)
        {
            case PropertyAttribute2nd.Health:
                return ResistanceType.HealthDrain;
            case PropertyAttribute2nd.Stamina:
                return ResistanceType.StaminaDrain;
            case PropertyAttribute2nd.Mana:
                return ResistanceType.ManaDrain;
            default:
                return ResistanceType.Undef;
        }
    }

    /// <summary>
    /// Checks for death from a boost / transfer spell
    /// </summary>
    private static void HandleBoostTransferDeath(Creature caster, Creature target)
    {
        if (caster is { IsDead: true })
        {
            caster.OnDeath(caster.DamageHistory.LastDamager, DamageType.Health, false);
            caster.Die();
        }

        if (target is { IsDead: true } && target != caster)
        {
            target.OnDeath(target.DamageHistory.LastDamager, DamageType.Health, false);
            target.Die();
        }
    }

    /// <summary>
    /// Handles casting SpellType.Transfer spells
    /// usually for Life Magic, ie. Stamina to Mana, Drain
    /// </summary>
    private void HandleCastSpell_Transfer(Spell spell, Creature targetCreature, bool showMsg = true, WorldObject weapon = null)
    {
        var player = this as Player;
        var creature = this as Creature;

        var targetPlayer = targetCreature as Player;

        // prevent double deaths from indirect casts
        // caster is already checked in player/monster, and re-checking caster here would break death emotes such as bunny smite
        if (targetCreature != null && targetCreature.IsDead)
        {
            return;
        }

        // source and destination can be the same creature, or different creatures
        var caster = this as Creature;
        var transferSource = spell.TransferFlags.HasFlag(TransferFlags.CasterSource) ? caster : targetCreature;
        var destination = spell.TransferFlags.HasFlag(TransferFlags.CasterDestination) ? caster : targetCreature;

        // Calculate vital changes
        uint srcVitalChange = 0,
            destVitalChange;

        // Drain Resistances - allows one to partially resist drain health/stamina/mana and harm attacks (not including other life transfer spells).
        var isDrain = spell.TransferFlags.HasFlag(TransferFlags.TargetSource | TransferFlags.CasterDestination);
        if (transferSource != null)
        {
            var drainMod = isDrain ? (float)transferSource.GetResistanceMod(GetDrainResistanceType(spell.Source)) : 1.0f;

            srcVitalChange = (uint)
                Math.Round(transferSource.GetCreatureVital(spell.Source).Current * spell.Proportion * drainMod);
        }

        // TransferCap caps both srcVitalChange and destVitalChange
        // https://asheron.fandom.com/wiki/Announcements_-_2003/01_-_The_Slumbering_Giant#Letter_to_the_Players

        if (spell.TransferCap != 0 && srcVitalChange > spell.TransferCap)
        {
            srcVitalChange = (uint)spell.TransferCap;
        }

        // should healing resistances be applied here?
        if (destination != null)
        {
            var boostMod = isDrain ? (float)destination.GetResistanceMod(GetBoostResistanceType(spell.Destination)) : 1.0f;

            destVitalChange = (uint)Math.Round(srcVitalChange * (1.0f - spell.LossPercent) * boostMod);

            // scale srcVitalChange to destVitalChange?
            var missingDest = destination.GetCreatureVital(spell.Destination).Missing;

            var maxDestVitalChange = missingDest;
            if (spell.TransferCap != 0 && maxDestVitalChange > spell.TransferCap)
            {
                maxDestVitalChange = (uint)spell.TransferCap;
            }

            if (destVitalChange > maxDestVitalChange)
            {
                var scalar = (float)maxDestVitalChange / destVitalChange;

                srcVitalChange = (uint)Math.Round(srcVitalChange * scalar);
                destVitalChange = maxDestVitalChange;
            }

            var vitalTransferRatingBonus = CheckForRatingVitalsTransferBonus(player);
            srcVitalChange = Convert.ToUInt32(srcVitalChange * vitalTransferRatingBonus);
            destVitalChange = Convert.ToUInt32(destVitalChange * vitalTransferRatingBonus);

            ResetRatingElementalistQuestStamps(player);

            var nullificationRatingBonus = CheckForRatingNullificationBoostDefenseBonus(targetPlayer);
            srcVitalChange = Convert.ToUInt32(srcVitalChange * nullificationRatingBonus);
            destVitalChange = Convert.ToUInt32(destVitalChange * nullificationRatingBonus);

            // handle cloak damage procs for drain health other
            var equippedCloak = targetCreature?.EquippedCloak;

            if (isDrain && spell.Source == PropertyAttribute2nd.Health)
            {
                if (targetCreature != null)
                {
                    var percent = (float)srcVitalChange / targetCreature.Health.MaxValue;

                    if (equippedCloak != null && Cloak.HasDamageProc(equippedCloak) && Cloak.RollProc(equippedCloak, percent))
                    {
                        var reduced = Cloak.GetReducedAmount(this, srcVitalChange);

                        Cloak.ShowMessage(targetCreature, this, srcVitalChange, reduced);

                        srcVitalChange = reduced;
                        destVitalChange = (uint)Math.Round(srcVitalChange * (1.0f - spell.LossPercent) * boostMod);
                    }
                }
            }

            string srcVital = null, destVital;

            // Restoration Spell Mod
            if (weapon is { WeaponRestorationSpellsMod: > 1 })
            {
                var weaponRestorationMod = weapon.WeaponRestorationSpellsMod;

                destVitalChange = Convert.ToUInt32(destVitalChange * weaponRestorationMod);
            }

            // COMBAT ABILITIES: Spell Effectiveness Mods
            if (player != null)
            {
                var overloadMod = CheckForCombatAbilityOverloadDamageBonus(player);
                var batterMod = CheckForCombatAbilityBatteryDamagePenalty(player);

                destVitalChange = (uint)(destVitalChange * overloadMod * batterMod);
            }

            // Archetype Mod
            if (creature is not null)
            {
                var archetypeSpellDamageMod = (float)(creature.ArchetypeSpellDamageMultiplier ?? 1.0);
                destVitalChange = Convert.ToUInt32(destVitalChange * archetypeSpellDamageMod);
            }

            // Recent Void Spell Cast Mod - Lower all healing spell effects for 10 seconds.
            if (player is not null && player.LastVoidSpellCastTime + player.LastVoidSpellCastPenaltyLength > Time.GetUnixTime())
            {
                destVitalChange = (uint)(destVitalChange * 0.5f);
            }

            // LEVEL SCALING - Reduce Drain effectiveness vs. monsters, and increase vs. player
            if (spell.TransferFlags.HasFlag(TransferFlags.TargetSource | TransferFlags.CasterDestination))
            {
                var levelScalingMod = LevelScaling.GetPlayerBoostSpellScalar(player, targetCreature);

                srcVitalChange = (uint)(srcVitalChange * levelScalingMod);
                destVitalChange = (uint)(destVitalChange * levelScalingMod);
            }

            // for traps and creatures that don't have a lethality mod,
            // make sure they receive multipliers from landblock mods
            if (ArchetypeLethality is null && CurrentLandblock is not null)
            {
                destVitalChange *= (uint)(1.0f + (float)CurrentLandblock.GetLandblockLethalityMod());
            }

            // Apply the change in vitals to the source
            if (transferSource != null)
            {
                switch (spell.Source)
                {
                    case PropertyAttribute2nd.Mana:
                        srcVital = "mana";
                        srcVitalChange = (uint)-transferSource.UpdateVitalDelta(transferSource.Mana, -(int)srcVitalChange);
                        break;
                    case PropertyAttribute2nd.Stamina:
                        srcVital = "stamina";
                        srcVitalChange = (uint)-transferSource.UpdateVitalDelta(transferSource.Stamina, -(int)srcVitalChange);
                        break;
                    default: // Health
                        srcVital = "health";
                        srcVitalChange = (uint)-transferSource.UpdateVitalDelta(transferSource.Health, -(int)srcVitalChange);

                        transferSource.DamageHistory.Add(this, DamageType.Health, srcVitalChange);
                        break;
                }

                // Determine if this drain/infuse should increase the charge meter. Self-transfer does not increase charge.
                var shouldIncreaseCharge =
                    destVitalChange > 0 &&
                    (
                        transferSource is not Player || 
                        (transferSource is Player && destination != transferSource)
                    );

                if (shouldIncreaseCharge &&
                    player is { OverloadStanceIsActive: true } or { BatteryStanceIsActive: true })
                {
                    player.IncreaseChargedMeter(spell);
                }
            }

            // Apply the scaled change in vitals to the caster
            switch (spell.Destination)
            {
                case PropertyAttribute2nd.Mana:
                    destVital = "mana";
                    destVitalChange = (uint)destination.UpdateVitalDelta(destination.Mana, destVitalChange);
                    break;
                case PropertyAttribute2nd.Stamina:
                    destVital = "stamina";
                    destVitalChange = (uint)destination.UpdateVitalDelta(destination.Stamina, destVitalChange);
                    break;
                default: // Health
                    destVital = "health";
                    destVitalChange = (uint)destination.UpdateVitalDelta(destination.Health, destVitalChange);

                    destination.DamageHistory.OnHeal(destVitalChange);

                    //var destPlayer = destination as Player;
                    //if (destPlayer != null && destPlayer.Fellowship != null)
                    //destPlayer.Fellowship.OnVitalUpdate(destPlayer);

                    break;
            }

            if (transferSource != player && srcVitalChange > 0)
            {
                HandlePostDamageRatingEffects(transferSource, srcVitalChange, player, targetPlayer, creature, spell, ProjectileSpellType.Undef);
            }
            else if (targetPlayer == player && destVitalChange >= 0)
            {
                HandlePostHealRatingEffects(player, targetPlayer);
            }

            // You gain 52 points of health due to casting Drain Health Other I on Olthoi Warrior
            // You lose 22 points of mana due to casting Incantation of Infuse Mana Other on High-Voltage VI
            // You lose 12 points of mana due to Zofrit Zefir casting Drain Mana Other II on you

            // You cast Stamina to Mana Self I on yourself and lose 50 points of stamina and also gain 45 points of mana
            // You cast Stamina to Health Self VI on yourself and fail to affect your  stamina and also gain 1 point of health

            // unverified:
            // You gain X points of vital due to caster casting spell on you
            // You lose X points of vital due to caster casting spell on you

            var playerSource = transferSource as Player;
            var playerDestination = destination as Player;

            string sourceMsg = null, targetMsg = null;

            var chargedMsg = "";

            if (player is {OverloadStanceIsActive: true} or {BatteryStanceIsActive: true})
            {
                var chargedPercent = Math.Round(player.ManaChargeMeter * 100);
                chargedMsg = $"{chargedPercent}% Charged! ";
            }

            chargedMsg = player switch
            {
                { OverloadDischargeIsActive: true } => "Overload Discharge! ",
                { BatteryDischargeIsActive: true } => "Battery Discharge! ",
                _ => chargedMsg
            };

            if (playerSource != null && playerDestination != null && transferSource.Guid == destination.Guid)
            {
                sourceMsg = $"{chargedMsg}You cast {spell.Name} on yourself and lose {srcVitalChange} points of {srcVital} and also gain {destVitalChange} points of {destVital}";
            }
            else
            {
                if (playerSource != null)
                {
                    if (transferSource == this)
                    {
                        sourceMsg = $"{chargedMsg}You lose {srcVitalChange} points of {srcVital} due to casting {spell.Name} on {targetCreature.Name}";
                    }
                    else
                    {
                        targetMsg = $"{chargedMsg}You lose {srcVitalChange} points of {srcVital} due to {caster.Name} casting {spell.Name} on you";
                    }

                    if (destination != null)
                    {
                        playerSource.SetCurrentAttacker(destination);
                    }
                }

                if (playerDestination != null)
                {
                    if (destination == this)
                    {
                        sourceMsg = $"{chargedMsg}You gain {destVitalChange} points of {destVital} due to casting {spell.Name} on {targetCreature.Name}";
                    }
                    else
                    {
                        targetMsg = $"{chargedMsg}You gain {destVitalChange} points of {destVital} due to {caster.Name} casting {spell.Name} on you";
                    }
                }
            }

            if (player != null && sourceMsg != null && showMsg)
            {
                player.SendChatMessage(player, sourceMsg, ChatMessageType.Magic);
            }

            if (targetPlayer != null && targetMsg != null && showMsg)
            {
                targetPlayer.SendChatMessage(caster, targetMsg, ChatMessageType.Magic);
            }

            if (isDrain && targetCreature.IsAlive && spell.Source == PropertyAttribute2nd.Health)
            {
                // handle cloak spell proc
                if (equippedCloak != null && Cloak.HasProcSpell(equippedCloak))
                {
                    var pct = (float)srcVitalChange / targetCreature.Health.MaxValue;

                    // ensure message is sent after enchantment.Message
                    var actionChain = new ActionChain();
                    actionChain.AddDelayForOneTick();
                    actionChain.AddAction(this, () => Cloak.TryProcSpell(targetCreature, this, equippedCloak, pct));
                    actionChain.EnqueueChain();
                }

                // ensure emote process occurs after damage msg
                var emoteChain = new ActionChain();
                emoteChain.AddDelayForOneTick();
                emoteChain.AddAction(targetCreature, () => targetCreature.EmoteManager.OnDamage(creature));
                //if (critical)
                //    emoteChain.AddAction(targetCreature, () => targetCreature.EmoteManager.OnReceiveCritical(creature));
                emoteChain.EnqueueChain();
            }
        }

        HandleBoostTransferDeath(creature, targetCreature);
    }

    /// <summary>
    /// COMBAT ABILITY - Battery: Decrease vital transfer effectiveness.
    /// </summary>
    private static uint CheckForCombatAbilityBatteryVitalTransferPenalty(CombatAbility combatAbility, Player player, uint srcVitalChange, ref uint destVitalChange)
    {
        if (!player.BatteryDischargeIsActive)
        {
            return srcVitalChange;
        }

        var maxMana = (float)player.Mana.MaxValue;
        var currentMana = (float)player.Mana.Current == 0 ? 1 : (float)player.Mana.Current;

        if ((currentMana / maxMana) < 0.75)
        {
            var newMax = maxMana * 0.75;
            var batteryMod = 1f - 0.25f * ((newMax - currentMana) / newMax);

            srcVitalChange = (uint)(srcVitalChange * batteryMod);
            destVitalChange = (uint)(destVitalChange * batteryMod);
        }

        return srcVitalChange;
    }

    /// <summary>
    /// RATING - Vitals Trasfer: +2% boost effecitveness per rating to transfer spells
    /// (JEWEL - Rose Quartz)
    /// </summary>
    private static float CheckForRatingVitalsTransferBonus(Player player)
    {
        if (player == null)
        {
            return 1.0f;
        }

        if (player.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearVitalsTransfer) <= 0)
        {
            return 1.0f;
        }

        var ratingMod = player.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearVitalsTransfer) * 0.02f;

        return 1.0f + ratingMod;
    }

    /// <summary>
    /// Handles casting SpellType.Projectile / LifeProjectile / EnchantmentProjectile spells
    /// </summary>
    private void HandleCastSpell_Projectile(
        Spell spell,
        WorldObject target,
        WorldObject itemCaster,
        WorldObject weapon,
        bool isWeaponSpell,
        bool fromProc,
        int? weaponSpellcraft = null,
        double damageMultiplier = 1.0
    )
    {
        uint damage = 0;
        var caster = this as Creature;

        var damageType = DamageType.Undef;

        if (spell.School == MagicSchool.LifeMagic && caster != null)
        {
            if (spell.DamageType.HasFlag(DamageType.Mana))
            {
                var tryDamage = (int)
                    Math.Round(caster.GetCreatureVital(PropertyAttribute2nd.Mana).Current * spell.DrainPercentage);
                damage = (uint)-caster.UpdateVitalDelta(caster.Mana, -tryDamage);
                damageType = DamageType.Mana;
            }
            else if (spell.DamageType.HasFlag(DamageType.Stamina))
            {
                var tryDamage = (int)
                    Math.Round(caster.GetCreatureVital(PropertyAttribute2nd.Stamina).Current * spell.DrainPercentage);
                damage = (uint)-caster.UpdateVitalDelta(caster.Stamina, -tryDamage);
                damageType = DamageType.Stamina;
            }
            else if (spell.DamageType.HasFlag(DamageType.Health))
            {
                var tryDamage = (int)
                    Math.Round(caster.GetCreatureVital(PropertyAttribute2nd.Health).Current * spell.DrainPercentage);
                damage = (uint)-caster.UpdateVitalDelta(caster.Health, -tryDamage);
                caster.DamageHistory.Add(this, DamageType.Health, damage);
                damageType = DamageType.Health;

                //if (player != null && player.Fellowship != null)
                //player.Fellowship.OnVitalUpdate(player);
            }
            else
            {
                _log.Warning("Unknown DamageType for LifeProjectile {SpellName} - {SpellId}", spell.Name, spell.Id);
                return;
            }

            if (caster is Player playerCaster and ({ OverloadStanceIsActive: true } or {BatteryStanceIsActive: true}))
            {
                playerCaster.IncreaseChargedMeter(spell);
            }
        }

        var projectileSpellType = SpellProjectile.GetProjectileSpellType(spell.Id);

        if (projectileSpellType != ProjectileSpellType.Blast)
        {
            CreateSpellProjectiles(spell, target, weapon, isWeaponSpell, fromProc, damage, false, weaponSpellcraft, damageMultiplier);
        }

        var targetCreature = target as Creature;

        if (targetCreature != null && projectileSpellType == ProjectileSpellType.Blast)
        {
            List<Creature> nearbyTargets;
            const int blastRadius = 10;
            nearbyTargets = this is Player ? targetCreature.GetNearbyMonsters(blastRadius) : targetCreature.GetNearbyPlayers(blastRadius);

            var blastTargets = new List<Creature> { targetCreature };

            if (nearbyTargets != null)
            {
                var blastCount = 0;
                foreach (var nearbyTarget in nearbyTargets)
                {
                    if (blastCount == 2)
                    {
                        break;
                    }

                    if (nearbyTarget.Translucency == 1 || nearbyTarget.Visibility)
                    {
                        continue;
                    }

                    var angle = targetCreature.GetAngle(nearbyTarget);
                    if (Math.Abs(angle) > Creature.CleaveAngle / 4.0f)
                    {
                        continue;
                    }

                    blastTargets.Add(nearbyTarget);
                    blastCount++;
                }
            }

            if (blastTargets.Count >= 3)
            {
                spell.SpellPowerMod = 0.67f;
            }

            if (blastTargets.Count == 2)
            {
                spell.SpellPowerMod = 0.75f;
            }

            foreach (var blastTarget in blastTargets)
            {
                CreateSpellProjectiles(spell, blastTarget, weapon, isWeaponSpell, fromProc, damage, false, weaponSpellcraft, damageMultiplier);
            }
        }

        CheckForRatingSlashCleaveBonus(spell, weapon, isWeaponSpell, fromProc, caster, targetCreature, damage);

        if (spell.School == MagicSchool.LifeMagic && caster != null)
        {
            if (caster.Health.Current <= 0)
            {
                // should this be possible?
                var lastDamager = new DamageHistoryInfo(caster);

                caster.OnDeath(lastDamager, damageType, false);
                caster.Die();
            }
        }
    }

    /// <summary>
    /// RATING - Slash: Chance for bonus cleave target with slashing damage.
    /// (JEWEL - Imperial Topaz)
    /// </summary>
    private void CheckForRatingSlashCleaveBonus(Spell spell, WorldObject weapon, bool isWeaponSpell, bool fromProc,
        Creature caster, Creature targetCreature, uint damage)
    {
        // JEWEL - Imperial Topaz - Bonus cleave chance
        if (caster is not Player playerCaster || targetCreature == null)
        {
            return;
        }

        if (spell.DamageType != DamageType.Slash)
        {
            return;
        }

        if (spell.NumProjectiles > 1)
        {
            return;
        }

        var rating = playerCaster.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearSlash);

        if (rating <= 0)
        {
            return;
        }

        var chance = Jewel.GetJewelEffectMod(playerCaster, PropertyInt.GearSlash);

        if (ThreadSafeRandom.Next(0.0f, 1.0f) > chance)
        {
            return;
        }

        var cleave = targetCreature.GetNearbyMonsters(10);

        foreach (var cleaveHit in cleave)
        {
            if (cleaveHit.Translucency == 1 || cleaveHit.Visibility)
            {
                continue;
            }

            var angle = caster.GetAngle(cleaveHit);
            var angleWrong = Math.Abs(angle) > 90.0f;

            if (!angleWrong)
            {
                CreateSpellProjectiles(spell, cleaveHit, weapon, isWeaponSpell, fromProc, damage);
            }

            break;
        }
    }

    /// <summary>
    /// Handles casting SpellType.PortalLink spells
    /// </summary>
    private void HandleCastSpell_PortalLink(Spell spell, WorldObject target)
    {
        if (this is not Player player)
        {
            return;
        }

        if (player.IsOlthoiPlayer)
        {
            player.Session.Network.EnqueueSend(
                new GameEventWeenieError(player.Session, WeenieError.OlthoiCanOnlyRecallToLifestone)
            );
            return;
        }

        switch ((SpellId)spell.Id)
        {
            case SpellId.LifestoneTie1: // Lifestone Tie

                if (target.WeenieType == WeenieType.LifeStone)
                {
                    player.SendChatMessage(
                        this,
                        "You have successfully linked with the life stone.",
                        ChatMessageType.Magic
                    );
                    player.LinkedLifestone = target.Location;
                }
                else
                {
                    player.SendChatMessage(this, "You cannot link that.", ChatMessageType.Magic);
                }

                break;

            case SpellId.PortalTie1: // Primary Portal Tie
            case SpellId.PortalTie2: // Secondary Portal Tie

                if (target.WeenieType != WeenieType.Portal)
                {
                    player.SendChatMessage(this, "You cannot link that.", ChatMessageType.Magic);
                    break;
                }

                var targetPortal = target as Portal;

                var summoned = targetPortal is { OriginalPortal: not null };

                if (targetPortal != null)
                {
                    var targetDid = summoned ? targetPortal.OriginalPortal : targetPortal.WeenieClassId;

                    var tiePortal = GetPortal(targetDid.Value);

                    if (tiePortal == null)
                    {
                        player.Session.Network.EnqueueSend(
                            new GameEventWeenieError(player.Session, WeenieError.YouCannotLinkToThatPortal)
                        );
                        break;
                    }

                    tiePortal.PlayerUsingTieOrSummonSpell = true;

                    var result = tiePortal.CheckUseRequirements(player);

                    if (!result.Success && result.Message != null)
                    {
                        player.Session.Network.EnqueueSend(result.Message);
                    }

                    if (tiePortal.NoTie || !result.Success)
                    {
                        player.Session.Network.EnqueueSend(
                            new GameEventWeenieError(player.Session, WeenieError.YouCannotLinkToThatPortal)
                        );
                        break;
                    }

                    var isPrimary = spell.Id == (int)SpellId.PortalTie1;

                    if (isPrimary)
                    {
                        player.LinkedPortalOneDID = targetDid;
                        player.SetProperty(PropertyBool.LinkedPortalOneSummon, summoned);
                    }
                    else
                    {
                        player.LinkedPortalTwoDID = targetDid;
                        player.SetProperty(PropertyBool.LinkedPortalTwoSummon, summoned);
                    }
                }

                player.SendChatMessage(this, "You have successfully linked with the portal.", ChatMessageType.Magic);
                break;
        }
    }

    /// <summary>
    /// Returns a Portal object for a WCID
    /// </summary>
    private static Portal GetPortal(uint wcid)
    {
        var weenie = DatabaseManager.World.GetCachedWeenie(wcid);

        return WorldObjectFactory.CreateWorldObject(weenie, new ObjectGuid(wcid)) as Portal;
    }

    /// <summary>
    /// Handles casting SpellType.PortalRecall spells
    /// </summary>
    private void HandleCastSpell_PortalRecall(Spell spell, Creature targetCreature)
    {
        var player = this as Player;

        if (player is { IsOlthoiPlayer: true })
        {
            player.Session.Network.EnqueueSend(
                new GameEventWeenieError(player.Session, WeenieError.OlthoiCanOnlyRecallToLifestone)
            );
            return;
        }

        var targetPlayer = targetCreature as Player;

        if (player is { PKTimerActive: true })
        {
            player.Session.Network.EnqueueSend(
                new GameEventWeenieError(player.Session, WeenieError.YouHaveBeenInPKBattleTooRecently)
            );
            return;
        }

        var recall = PositionType.Undef;
        uint? recallDid = null;

        // verify pre-requirements for recalls

        switch ((SpellId)spell.Id)
        {
            case SpellId.PortalRecall: // portal recall

                if (targetPlayer is { LastPortalDID: null })
                {
                    // You must link to a portal to recall it!
                    targetPlayer.Session.Network.EnqueueSend(
                        new GameEventWeenieError(targetPlayer.Session, WeenieError.YouMustLinkToPortalToRecall)
                    );
                }
                else
                {
                    recall = PositionType.LastPortal;
                    if (targetPlayer != null)
                    {
                        recallDid = targetPlayer.LastPortalDID;
                    }
                }
                break;

            case SpellId.LifestoneRecall1: // lifestone recall

                if (targetPlayer != null && targetPlayer.GetPosition(PositionType.LinkedLifestone) == null)
                {
                    // You must link to a lifestone to recall it!
                    targetPlayer.Session.Network.EnqueueSend(
                        new GameEventWeenieError(targetPlayer.Session, WeenieError.YouMustLinkToLifestoneToRecall)
                    );
                }
                else
                {
                    recall = PositionType.LinkedLifestone;
                }

                break;

            case SpellId.LifestoneSending1:

                if (player?.GetPosition(PositionType.Sanctuary) != null)
                {
                    recall = PositionType.Sanctuary;
                }
                else if (targetPlayer != null && targetPlayer.GetPosition(PositionType.Sanctuary) != null)
                {
                    recall = PositionType.Sanctuary;
                }

                break;

            case SpellId.PortalTieRecall1: // primary portal tie recall

                if (targetPlayer != null && targetPlayer.LinkedPortalOneDID == null)
                {
                    // You must link to a portal to recall it!
                    targetPlayer.Session.Network.EnqueueSend(
                        new GameEventWeenieError(targetPlayer.Session, WeenieError.YouMustLinkToPortalToRecall)
                    );
                }
                else
                {
                    recall = PositionType.LinkedPortalOne;
                    if (targetPlayer != null)
                    {
                        recallDid = targetPlayer.LinkedPortalOneDID;
                    }
                }
                break;

            case SpellId.PortalTieRecall2: // secondary portal tie recall

                if (targetPlayer is { LinkedPortalTwoDID: null })
                {
                    // You must link to a portal to recall it!
                    targetPlayer.Session.Network.EnqueueSend(
                        new GameEventWeenieError(targetPlayer.Session, WeenieError.YouMustLinkToPortalToRecall)
                    );
                }
                else
                {
                    recall = PositionType.LinkedPortalTwo;
                    if (targetPlayer != null)
                    {
                        recallDid = targetPlayer.LinkedPortalTwoDID;
                    }
                }
                break;
        }

        if (recall != PositionType.Undef)
        {
            if (recallDid == null)
            {
                // lifestone recall
                var lifestoneRecall = new ActionChain();
                lifestoneRecall.AddAction(targetPlayer, () =>
                {
                    targetPlayer?.DoPreTeleportHide();
                });
                lifestoneRecall.AddDelaySeconds(2.0f); // 2 second delay
                lifestoneRecall.AddAction(targetPlayer, () => targetPlayer?.TeleToPosition(recall));
                lifestoneRecall.EnqueueChain();
            }
            else
            {
                // portal recall
                var portal = GetPortal(recallDid.Value);
                if (portal == null || portal.NoRecall)
                {
                    // You cannot recall that portal!
                    player?.Session.Network.EnqueueSend(
                        new GameEventWeenieError(player.Session, WeenieError.YouCannotRecallPortal)
                    );

                    return;
                }

                var result = portal.CheckUseRequirements(targetPlayer);
                if (!result.Success)
                {
                    if (result.Message != null)
                    {
                        targetPlayer.Session.Network.EnqueueSend(result.Message);
                    }

                    return;
                }

                var portalRecall = new ActionChain();
                portalRecall.AddAction(targetPlayer, () => targetPlayer.DoPreTeleportHide());
                portalRecall.AddDelaySeconds(2.0f); // 2 second delay
                portalRecall.AddAction(
                    targetPlayer,
                    () =>
                    {
                        var teleportDest = new Position(portal.Destination);
                        AdjustDungeon(teleportDest);

                        targetPlayer.Teleport(teleportDest);

                        portal.EmoteManager.OnPortal(player);
                    }
                );
                portalRecall.EnqueueChain();
            }
        }
    }

    /// <summary>
    /// Handles casting SpellType.PortalSummon spells
    /// </summary>
    private void HandleCastSpell_PortalSummon(Spell spell, Creature targetCreature, WorldObject itemCaster)
    {
        var player = this as Player;

        switch (player)
        {
            case { IsOlthoiPlayer: true }:
                player.Session.Network.EnqueueSend(
                    new GameEventWeenieError(player.Session, WeenieError.OlthoiCanOnlyRecallToLifestone)
                );
                return;
            case { PKTimerActive: true }:
                player.Session.Network.EnqueueSend(
                    new GameEventWeenieError(player.Session, WeenieError.YouHaveBeenInPKBattleTooRecently)
                );
                return;
        }

        var source = player ?? itemCaster;

        uint portalId;
        bool linkSummoned;

        // spell.link = 1 = LinkedPortalOneDID
        // spell.link = 2 = LinkedPortalTwoDID

        if (spell.Link <= 1)
        {
            portalId = source.LinkedPortalOneDID ?? 0;
            linkSummoned = source.GetProperty(PropertyBool.LinkedPortalOneSummon) ?? false;
        }
        else
        {
            portalId = source.LinkedPortalTwoDID ?? 0;
            linkSummoned = source.GetProperty(PropertyBool.LinkedPortalTwoSummon) ?? false;
        }

        Position summonLoc = null;

        if (player != null)
        {
            if (portalId == 0)
            {
                // You must link to a portal to summon it!
                player.Session.Network.EnqueueSend(
                    new GameEventWeenieError(player.Session, WeenieError.YouMustLinkToPortalToSummonIt)
                );
                return;
            }

            var summonPortal = GetPortal(portalId);
            if (
                summonPortal == null
                || summonPortal.NoSummon
                || (linkSummoned && !PropertyManager.GetBool("gateway_ties_summonable").Item)
            )
            {
                // You cannot summon that portal!
                player.Session.Network.EnqueueSend(
                    new GameEventWeenieError(player.Session, WeenieError.YouCannotSummonPortal)
                );
                return;
            }

            summonPortal.PlayerUsingTieOrSummonSpell = true;

            var result = summonPortal.CheckUseRequirements(player);
            if (!result.Success)
            {
                if (result.Message != null)
                {
                    player.Session.Network.EnqueueSend(result.Message);
                }

                return;
            }

            summonLoc = player.Location.InFrontOf(3.0f);
        }
        else if (itemCaster != null)
        {
            if (itemCaster.PortalSummonLoc != null)
            {
                summonLoc = new Position(PortalSummonLoc);
            }
            else
            {
                if (itemCaster.Location != null)
                {
                    summonLoc = itemCaster.Location.InFrontOf(3.0f);
                }
                else if (targetCreature is { Location: not null })
                {
                    summonLoc = targetCreature.Location.InFrontOf(3.0f);
                }
            }
        }

        if (summonLoc != null)
        {
            summonLoc.LandblockId = new LandblockId(summonLoc.GetCell());
        }

        var success = SummonPortal(portalId, summonLoc, spell.PortalLifetime);

        if (!success && player != null)
        {
            player.Session.Network.EnqueueSend(
                new GameEventWeenieError(player.Session, WeenieError.YouFailToSummonPortal)
            );
        }
    }

    /// <summary>
    /// Spawns a portal for SpellType.PortalSummon spells
    /// </summary>
    private static bool SummonPortal(uint portalId, Position location, double portalLifetime)
    {
        var portal = GetPortal(portalId);

        if (portal == null || location == null)
        {
            return false;
        }

        if (WorldObjectFactory.CreateNewWorldObject("portalgateway") is not Portal gateway)
        {
            return false;
        }

        gateway.Location = new Position(location);
        gateway.OriginalPortal = portalId;

        gateway.UpdatePortalDestination(new Position(portal.Destination));

        gateway.TimeToRot = portalLifetime;

        gateway.MinLevel = portal.MinLevel;
        gateway.MaxLevel = portal.MaxLevel;
        gateway.PortalRestrictions = portal.PortalRestrictions;
        gateway.FellowshipRequired = portal.FellowshipRequired;
        gateway.AccountRequirements = portal.AccountRequirements;
        gateway.AdvocateQuest = portal.AdvocateQuest;

        gateway.Quest = portal.Quest;
        gateway.QuestRestriction = portal.QuestRestriction;

        gateway.Biota.PropertiesEmote = portal.Biota.PropertiesEmote;

        gateway.PortalRestrictions |= PortalBitmask.NoSummon; // all gateways are marked NoSummon but by default ruleset, the OriginalPortal is the one that is checked against

        gateway.EnterWorld();

        return true;
    }

    /// <summary>
    /// Handles casting SpellType.PortalSending spells
    /// </summary>
    private static void HandleCastSpell_PortalSending(Spell spell, Creature targetCreature, WorldObject itemCaster)
    {
        if (targetCreature is Player targetPlayer)
        {
            if (targetPlayer.PKTimerActive)
            {
                targetPlayer.Session.Network.EnqueueSend(
                    new GameEventWeenieError(targetPlayer.Session, WeenieError.YouHaveBeenInPKBattleTooRecently)
                );
                return;
            }

            var portalSendingChain = new ActionChain();
            //portalSendingChain.AddDelaySeconds(2.0f);  // 2 second delay
            portalSendingChain.AddAction(targetPlayer, () => targetPlayer.DoPreTeleportHide());
            portalSendingChain.AddAction(
                targetPlayer,
                () =>
                {
                    var teleportDest = new Position(spell.Position);
                    AdjustDungeon(teleportDest);

                    targetPlayer.Teleport(teleportDest);

                    targetPlayer.SendTeleportedViaMagicMessage(itemCaster, spell);
                }
            );
            portalSendingChain.EnqueueChain();
        }
        else if (targetCreature != null)
        {
            // monsters can cast some portal spells on themselves too, possibly?
            // under certain circumstances, such as ensuring the destination is the same landblock
            var teleportDest = new Position(spell.Position);
            AdjustDungeon(teleportDest);

            targetCreature.FakeTeleport(teleportDest);
        }
    }

    /// <summary>
    /// Handles casting SpellType.FellowPortalSending spells
    /// </summary>
    private void HandleCastSpell_FellowPortalSending(Spell spell, Creature targetCreature, WorldObject itemCaster)
    {
        var creature = this as Creature;

        if (targetCreature is not Player targetPlayer || targetPlayer.Fellowship == null)
        {
            return;
        }

        if (targetPlayer.PKTimerActive)
        {
            targetPlayer.Session.Network.EnqueueSend(
                new GameEventWeenieError(targetPlayer.Session, WeenieError.YouHaveBeenInPKBattleTooRecently)
            );
            return;
        }

        if (creature != null)
        {
            var distanceToTarget = creature.GetDistance(targetPlayer);
            var skill = creature.GetCreatureSkill(spell.School);
            var magicSkill = skill.InitLevel + skill.Ranks; // synced with acclient DetermineSpellRange -> InqSkillLevel

            var maxRange = spell.BaseRangeConstant + magicSkill * spell.BaseRangeMod;
            if (maxRange == 0.0f)
            {
                maxRange = float.PositiveInfinity;
            }

            if (distanceToTarget > maxRange)
            {
                return;
            }
        }

        var portalSendingChain = new ActionChain();
        portalSendingChain.AddAction(targetPlayer, () => targetPlayer.DoPreTeleportHide());
        portalSendingChain.AddAction(
            targetPlayer,
            () =>
            {
                var teleportDest = new Position(spell.Position);
                AdjustDungeon(teleportDest);

                targetPlayer.Teleport(teleportDest);

                targetPlayer.SendTeleportedViaMagicMessage(itemCaster, spell);
            }
        );
        portalSendingChain.EnqueueChain();
    }

    /// <summary>
    /// Handles casting SpellType.Dispel / FellowDispel spells
    /// </summary>
    private void HandleCastSpell_Dispel(Spell spell, WorldObject target, bool showMsg = true)
    {
        var player = this as Player;
        var creature = this as Creature;

        var removeSpells = target.EnchantmentManager.SelectDispel(spell);

        // dispel on server and client
        target.EnchantmentManager.Dispel(removeSpells.Select(s => s.Enchantment).ToList());

        var spellList = BuildSpellList(removeSpells);
        var suffix = "";
        if (removeSpells.Count > 0)
        {
            suffix = $" and dispel: {spellList}.";
        }
        else
        {
            suffix = ", but the dispel fails.";
        }

        if (player != null)
        {
            string casterMsg;

            if (player == target)
            {
                casterMsg = $"You cast {spell.Name} on yourself{suffix}";
            }
            else
            {
                casterMsg = $"You cast {spell.Name} on {target.Name}{suffix}";
            }

            if (showMsg)
            {
                player.SendChatMessage(player, casterMsg, ChatMessageType.Magic);
            }
        }

        if (target is Player targetPlayer && targetPlayer != player)
        {
            var targetMsg = $"{Name} casts {spell.Name} on you{suffix.Replace("and dispel", "and dispels")}";

            if (showMsg)
            {
                targetPlayer.SendChatMessage(this, targetMsg, ChatMessageType.Magic);
            }

            // all dispels appear to be listed as non-beneficial, even the ones that only dispel negative spells
            // we filter here to positive or all
            if (creature != null && spell.Align != DispelType.Negative)
            {
                targetPlayer.SetCurrentAttacker(creature);
            }
        }
    }

    protected static bool VerifyDispelPkStatus(WorldObject caster, WorldObject target)
    {
        // https://asheron.fandom.com/wiki/Announcements_-_2004/04_-_A_New_Threat
        // https://asheron.fandom.com/wiki/Dispel_Spells

        // Dispel spells and potions have been revised. All dispels are also now tied to the PK/L timer.

        // The feedback on the suggested dispel timer for PK/L was very mixed. There was no clear majority either for or against.
        // With that in mind, we've gone ahead with the changes that we feel best improve majority of PK/L combat:
        // we've decided to implement the PK/L timer on dispels.

        // If you have been in a PK/L action within the last 20 seconds, you will not be able to:

        // - Use a dispel gem.
        // - Use a dispel potion.
        // - Use the Awakener or Attenuated Awakener on someone else.
        // - Cast any dispel spell on yourself.
        // - Cast any dispel spell on someone else.

        var casterPlayer = caster as Player;

        if (casterPlayer != null && casterPlayer.PKTimerActive)
        {
            casterPlayer.SendWeenieError(WeenieError.YouHaveBeenInPKBattleTooRecently);
            return false;
        }

        if ((target.Wielder ?? target) is Player targetPlayer && targetPlayer.PKTimerActive)
        {
            if ( /* casterPlayer != null || */
                caster is Gem
                || caster is Food
            )
            {
                targetPlayer.SendWeenieError(WeenieError.YouHaveBeenInPKBattleTooRecently);

                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns a string with the spell list format as:
    /// Spell Name 1, Spell Name 2, and Spell Name 3
    /// </summary>
    private static string BuildSpellList(List<SpellEnchantment> spells)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < spells.Count; i++)
        {
            var spell = spells[i];

            if (i > 0)
            {
                sb.Append(", ");
                if (i == spells.Count - 1)
                {
                    sb.Append("and ");
                }
            }

            sb.Append(spell.Spell.Name);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Creates and launches the projectiles for a spell
    /// </summary>
    protected List<SpellProjectile> CreateSpellProjectiles(
        Spell spell,
        WorldObject target,
        WorldObject weapon,
        bool isWeaponSpell = false,
        bool fromProc = false,
        uint lifeProjectileDamage = 0,
        bool castAtTarget = false,
        int? weaponSpellcraft = null,
        double damageMultiplier = 1.0
    )
    {
        if (spell.NumProjectiles == 0)
        {
            _log.Error($"{Name} ({Guid}).CreateSpellProjectiles({spell.Id} - {spell.Name}) - spell.NumProjectiles == 0");
            return new List<SpellProjectile>();
        }
        var spellType = SpellProjectile.GetProjectileSpellType(spell.Id);

        var origins = CalculateProjectileOrigins(spell, spellType, target);

        var velocity = CalculateProjectileVelocity(spell, target, spellType, origins[0]);

        // EMPOWERED SCARAB - Crushing
        var fireAllProjectilesFromCenter = false;
        var propertiesEnchantmentRegistry = EnchantmentManager.GetEnchantment(
            (uint)SpellId.GauntletCriticalDamageBoostI,
            null
        );
        if (propertiesEnchantmentRegistry != null && spellType == ProjectileSpellType.Blast)
        {
            EnchantmentManager.Dispel(propertiesEnchantmentRegistry);
            fireAllProjectilesFromCenter = true;
        }

        return LaunchSpellProjectiles(
            spell,
            target,
            spellType,
            weapon,
            isWeaponSpell,
            fromProc,
            origins,
            velocity,
            lifeProjectileDamage,
            castAtTarget,
            fireAllProjectilesFromCenter,
            weaponSpellcraft,
            damageMultiplier
        );
    }

    private const float ProjHeight = 2.0f / 3.0f;

    private Vector3 CalculatePreOffset(Spell spell, ProjectileSpellType spellType, WorldObject target)
    {
        var startFactor = spellType == ProjectileSpellType.Arc ? 1.0f : ProjHeight;

        var preOffset = new Vector3(0, 0, Height * startFactor);

        if (target == null)
        {
            return preOffset;
        }

        var startPos = new Physics.Common.Position(PhysicsObj.Position);
        startPos.Frame.Origin.Z += Height * startFactor;

        var endFactor = spellType == ProjectileSpellType.Arc ? ProjHeightArc : ProjHeight;

        var endPos = new Physics.Common.Position(target.PhysicsObj.Position);
        endPos.Frame.Origin.Z += target.Height * endFactor;

        var globOffset = startPos.GetOffset(endPos);

        // align in x
        var rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)Math.Atan2(globOffset.X, globOffset.Y));

        var offset = Vector3.Transform(globOffset, rotate);

        var localDir = Vector3.Normalize(offset);

        var radsum = PhysicsObj.GetPhysicsRadius() + GetProjectileRadius(spell);

        var defaultSpawnPos = Vector3.UnitY * radsum;

        var spawnPos = localDir * radsum;

        var spawnOffset = spawnPos - defaultSpawnPos;

        return preOffset + spawnOffset;
    }

    /// <summary>
    /// Returns a list of positions to spawn projectiles for a spell,
    /// in local space relative to the caster
    /// </summary>
    private List<Vector3> CalculateProjectileOrigins(
        Spell spell,
        ProjectileSpellType spellType,
        WorldObject target,
        bool castAtTarget = false
    )
    {
        var numProjectiles = spell.NumProjectiles;
        if (spellType == ProjectileSpellType.Blast)
        {
            numProjectiles = 1;
        }

        var origins = new List<Vector3>();

        var radius = GetProjectileRadius(spell);
        //Console.WriteLine($"Radius: {radius}");

        var vRadius = Vector3.One * radius;

        var baseOffset = spell.CreateOffset;

        var radsum = PhysicsObj.GetPhysicsRadius() * 2.0f + radius * 2.0f;

        var heightOffset = CalculatePreOffset(spell, spellType, target);

        if (target != null)
        {
            var cylDist = GetCylinderDistance(target);
            //Console.WriteLine($"CylDist: {cylDist}");
            if (cylDist < 0.6f)
            {
                radsum = PhysicsObj.GetPhysicsRadius() + radius;
            }
        }

        if (Math.Abs(spell.SpreadAngle - 360) < 1)
        {
            radsum *= 0.6f;
        }

        baseOffset.Y += radsum;

        baseOffset += heightOffset;

        var anglePerStep = GetSpreadAnglePerStep(spell);

        // TODO: normalize data
        var dims = new Vector3(
            spell._spell.DimsOriginX ?? numProjectiles,
            spell._spell.DimsOriginY ?? 1,
            spell._spell.DimsOriginZ ?? 1
        );

        var i = 0;
        for (var z = 0; z < dims.Z; z++)
        {
            for (var y = 0; y < dims.Y; y++)
            {
                var oddRow = (int)Math.Min(dims.X, numProjectiles - i) % 2 == 1;

                for (var x = 0; x < dims.X; x++)
                {
                    if (i >= numProjectiles)
                    {
                        break;
                    }

                    var curOffset = baseOffset;

                    if (spell.Peturbation != Vector3.Zero)
                    {
                        var rng = new Vector3(
                            (float)ThreadSafeRandom.Next(-1.0f, 1.0f),
                            (float)ThreadSafeRandom.Next(-1.0f, 1.0f),
                            (float)ThreadSafeRandom.Next(-1.0f, 1.0f)
                        );

                        curOffset += rng * spell.Peturbation * spell.Padding;
                    }

                    if (!oddRow && spell.SpreadAngle == 0)
                    {
                        curOffset.X += spell.Padding.X * 0.5f + radius;
                    }

                    var xFactor =
                        spell.SpreadAngle == 0
                            ? oddRow
                                ? (float)Math.Ceiling(x * 0.5f)
                                : (float)Math.Floor(x * 0.5f)
                            : 0;

                    var origin = curOffset + (vRadius * 2.0f + spell.Padding) * new Vector3(xFactor, y, z);

                    if (spell.SpreadAngle == 0)
                    {
                        if (x % 2 == (oddRow ? 1 : 0))
                        {
                            origin.X *= -1.0f;
                        }
                    }
                    else
                    {
                        // get the rotation matrix to apply to x
                        var numSteps = (x + 1) / 2;
                        if (x % 2 == 0)
                        {
                            numSteps *= -1;
                        }

                        //Console.WriteLine($"NumSteps: {numSteps}");

                        var curAngle = anglePerStep * numSteps;
                        var rads = curAngle.ToRadians();

                        var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rads);
                        origin = Vector3.Transform(origin, rot);
                    }

                    origins.Add(origin);
                    i++;
                }

                if (i >= numProjectiles)
                {
                    break;
                }
            }

            if (i >= numProjectiles)
            {
                break;
            }
        }

        /*foreach (var origin in origins)
            Console.WriteLine(origin);*/

        return origins;
    }

    /// <summary>
    /// Returns the angle in degrees between projectiles
    /// for spells with SpreadAngle
    /// </summary>
    private static float GetSpreadAnglePerStep(Spell spell)
    {
        if (spell.SpreadAngle == 0.0f || spell.NumProjectiles == 1)
        {
            return 0.0f;
        }

        var numProjectiles = spell.NumProjectiles;

        if (numProjectiles % 2 == 1)
        {
            numProjectiles--;
        }

        return spell.SpreadAngle / numProjectiles;
    }

    private static readonly Quaternion OneEighty = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)Math.PI);

    private const float ProjHeightArc = 5.0f / 6.0f;

    /// <summary>
    /// Calculates the spell projectile velocity in global space
    /// </summary>
    private Vector3 CalculateProjectileVelocity(
        Spell spell,
        WorldObject target,
        ProjectileSpellType spellType,
        Vector3 origin
    )
    {
        var casterLoc = PhysicsObj.Position.ACEPosition();

        var speed = GetProjectileSpeed(spell);

        if (target == null && this is Creature creature && !(this is Player))
        {
            target = creature.AttackTarget;
        }

        if (target == null)
        {
            // launch along forward vector
            return Vector3.Transform(Vector3.UnitY, casterLoc.Rotation) * speed;
        }

        var targetLoc = target.PhysicsObj.Position.ACEPosition();

        var strikeSpell = spellType == ProjectileSpellType.Strike;

        var crossLandblock = !strikeSpell && casterLoc.Landblock != targetLoc.Landblock;

        var qDir = PhysicsObj.Position.GetOffset(target.PhysicsObj.Position);
        var rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)Math.Atan2(-qDir.X, qDir.Y));

        var startPos = strikeSpell
            ? targetLoc.Pos
            : crossLandblock
                ? casterLoc.ToGlobal(false)
                : casterLoc.Pos;
        startPos += Vector3.Transform(origin, strikeSpell ? rotate * OneEighty : rotate);

        var endPos = crossLandblock ? targetLoc.ToGlobal(false) : targetLoc.Pos;

        endPos.Z += target.Height * (spellType == ProjectileSpellType.Arc ? ProjHeightArc : ProjHeight);

        var dir = Vector3.Normalize(endPos - startPos);

        var targetVelocity = spell.IsTracking ? target.PhysicsObj.CachedVelocity : Vector3.Zero;

        var useGravity = spellType == ProjectileSpellType.Arc;

        if (useGravity || targetVelocity != Vector3.Zero)
        {
            var gravity = useGravity ? PhysicsGlobals.Gravity : 0.0f;

            Vector3 velocity;
            if (!PropertyManager.GetBool("trajectory_alt_solver").Item)
            {
                Trajectory.solve_ballistic_arc_lateral(
                    startPos,
                    speed,
                    endPos,
                    targetVelocity,
                    gravity,
                    out velocity,
                    out var time,
                    out var impactPoint
                );
            }
            else
            {
                velocity = Trajectory2.CalculateTrajectory(startPos, endPos, targetVelocity, speed, useGravity);
            }

            if (velocity == Vector3.Zero && useGravity && targetVelocity != Vector3.Zero)
            {
                // intractable?
                // try to solve w/ zero velocity
                if (!PropertyManager.GetBool("trajectory_alt_solver").Item)
                {
                    Trajectory.solve_ballistic_arc_lateral(
                        startPos,
                        speed,
                        endPos,
                        Vector3.Zero,
                        gravity,
                        out velocity,
                        out var time,
                        out var impactPoint
                    );
                }
                else
                {
                    velocity = Trajectory2.CalculateTrajectory(startPos, endPos, Vector3.Zero, speed, useGravity);
                }
            }
            if (velocity != Vector3.Zero)
            {
                return velocity;
            }
        }

        return dir * speed;
    }

    private List<SpellProjectile> LaunchSpellProjectiles(
        Spell spell,
        WorldObject target,
        ProjectileSpellType spellType,
        WorldObject weapon,
        bool isWeaponSpell,
        bool fromProc,
        List<Vector3> origins,
        Vector3 velocity,
        uint lifeProjectileDamage = 0,
        bool castAtTarget = false,
        bool fireAllProjectilesFromCenter = false,
        int? weaponSpellcraft = null,
        double damageMultiplier = 1.0
    )
    {
        var useGravity = spellType == ProjectileSpellType.Arc;

        var strikeSpell = target != null && spellType == ProjectileSpellType.Strike;

        var spellProjectiles = new List<SpellProjectile>();

        var casterLoc = castAtTarget ? target?.PhysicsObj.Position.ACEPosition() : PhysicsObj.Position.ACEPosition();
        var targetLoc = target?.PhysicsObj.Position.ACEPosition();

        for (var i = 0; i < origins.Count; i++)
        {
            var origin = origins[i];

            if (fireAllProjectilesFromCenter)
            {
                origin = origins[0];
            }

            var sp = WorldObjectFactory.CreateNewWorldObject(spell.Wcid) as SpellProjectile;

            if (sp == null)
            {
                _log.Error(
                    $"{Name} ({Guid}).LaunchSpellProjectiles({spell.Id} - {spell.Name}) - failed to create spell projectile from wcid {spell.Wcid}"
                );
                break;
            }

            sp.Setup(spell, spellType);

            if (casterLoc != null)
            {
                var rotate = casterLoc.Rotation;
                if (target != null)
                {
                    var qDir = PhysicsObj.Position.GetOffset(target.PhysicsObj.Position);
                    rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)Math.Atan2(-qDir.X, qDir.Y));
                }

                sp.Location = strikeSpell ? new Position(targetLoc) : new Position(casterLoc);
                sp.Location.Pos += Vector3.Transform(origin, strikeSpell ? rotate * OneEighty : rotate);
            }

            sp.PhysicsObj.Velocity = velocity;

            if (spell.SpreadAngle > 0 && !fireAllProjectilesFromCenter)
            {
                var n = Vector3.Normalize(origin);
                var angle = Math.Atan2(-n.X, n.Y);
                var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)angle);
                sp.PhysicsObj.Velocity = Vector3.Transform(velocity, q);
            }

            // set orientation
            var dir = Vector3.Normalize(sp.Velocity);
            sp.PhysicsObj.Position.Frame.set_vector_heading(dir);
            sp.Location.Rotation = sp.PhysicsObj.Position.Frame.Orientation;

            sp.ProjectileSource = this;
            sp.FromProc = fromProc;

            // side projectiles always untargeted?
            if (i == 0)
            {
                sp.ProjectileTarget = target;
            }

            sp.ProjectileLauncher = weapon;
            sp.IsWeaponSpell = isWeaponSpell;

            sp.SetProjectilePhysicsState(sp.ProjectileTarget, useGravity);
            sp.SpawnPos = new Position(sp.Location);

            sp.LifeProjectileDamage = lifeProjectileDamage;

            sp.WeaponSpellcraft = weaponSpellcraft;

            sp.DamageMultiplier = damageMultiplier;

            if (!LandblockManager.AddObject(sp))
            {
                sp.Destroy();
                continue;
            }

            if (sp.WorldEntryCollision)
            {
                continue;
            }

            sp.EnqueueBroadcast(
                new GameMessageScript(sp.Guid, PlayScript.Launch, sp.GetProjectileScriptIntensity(spellType))
            );

            if (!IsProjectileVisible(sp))
            {
                sp.OnCollideEnvironment();
                continue;
            }

            spellProjectiles.Add(sp);
        }

        return spellProjectiles;
    }

    public static void ClearSpellCache()
    {
        ProjectileRadiusCache.Clear();
        ProjectileSpeedCache.Clear();
    }

    protected static readonly ConcurrentDictionary<uint, float> ProjectileRadiusCache =
        new ConcurrentDictionary<uint, float>();

    private float GetProjectileRadius(Spell spell)
    {
        var projectileWcid = spell.WeenieClassId;

        if (ProjectileRadiusCache.TryGetValue(projectileWcid, out var radius))
        {
            return radius;
        }

        var weenie = DatabaseManager.World.GetCachedWeenie(projectileWcid);

        if (weenie == null)
        {
            _log.Error(
                $"{Name} ({Guid}).GetSetupRadius({spell.Id} - {spell.Name}): couldn't find weenie {projectileWcid}"
            );
            return 0.0f;
        }

        if (!weenie.PropertiesDID.TryGetValue(PropertyDataId.Setup, out var setupId))
        {
            _log.Error(
                $"{Name} ({Guid}).GetSetupRadius({spell.Id} - {spell.Name}): couldn't find setup ID for {weenie.WeenieClassId} - {weenie.ClassName}"
            );
            return 0.0f;
        }

        var setup = DatManager.PortalDat.ReadFromDat<SetupModel>(setupId);

        if (!weenie.PropertiesFloat.TryGetValue(PropertyFloat.DefaultScale, out var scale))
        {
            scale = 1.0f;
        }

        var result = (float)(setup.Spheres[0].Radius * scale);

        ProjectileRadiusCache.TryAdd(projectileWcid, result);

        return result;
    }

    /// <summary>
    /// This is a temporary structure
    /// GetSpellProjectileSpeed() can easily be moved to SpellProjectile.CalculateSpeed()
    /// however the current calling pattern for Rings and Walls needs some work still..
    /// </summary>
    private static readonly ConcurrentDictionary<uint, float> ProjectileSpeedCache =
        new ConcurrentDictionary<uint, float>();

    /// <summary>
    /// Gets the speed of a projectile based on the distance to the target.
    /// </summary>
    private float GetProjectileSpeed(Spell spell, float? distance = null)
    {
        var projectileWcid = spell.WeenieClassId;

        if (!ProjectileSpeedCache.TryGetValue(projectileWcid, out var baseSpeed))
        {
            var weenie = DatabaseManager.World.GetCachedWeenie(projectileWcid);

            if (weenie == null)
            {
                _log.Error(
                    $"{Name} ({Guid}).GetSpellProjectileSpeed({spell.Id} - {spell.Name}, {distance}): couldn't find weenie {projectileWcid}"
                );
                return 0.0f;
            }

            if (!weenie.PropertiesFloat.TryGetValue(PropertyFloat.MaximumVelocity, out var maxVelocity))
            {
                _log.Error(
                    $"{Name} ({Guid}).GetSpellProjectileSpeed({spell.Id} - {spell.Name}, {distance}): couldn't find MaxVelocity for {weenie.WeenieClassId} - {weenie.ClassName}"
                );
                return 0.0f;
            }

            baseSpeed = (float)maxVelocity;

            ProjectileSpeedCache.TryAdd(projectileWcid, baseSpeed);
        }

        // TODO:
        // Speed seems to increase when target is moving away from the caster and decrease when
        // the target is moving toward the caster. This still needs more research.
        if (distance == null)
        {
            return baseSpeed;
        }

        var speed = (float)(
            (baseSpeed * .9998363f)
            - (baseSpeed * .62034f) / distance
            + (baseSpeed * .44868f) / Math.Pow(distance.Value, 2f)
            - (baseSpeed * .25256f) / Math.Pow(distance.Value, 3f)
        );

        speed = Math.Clamp(speed, 1, 50);

        return speed;
    }

    /// <summary>
    /// Returns the epic cantrips from this item's spellbook
    /// </summary>
    public Dictionary<
        int,
        float /* probability */
    > EpicCantrips => Biota.GetMatchingSpells(LootTables.EpicCantrips, BiotaDatabaseLock);

    /// <summary>
    /// Returns the legendary cantrips from this item's spellbook
    /// </summary>
    public Dictionary<
        int,
        float /* probability */
    > LegendaryCantrips => Biota.GetMatchingSpells(LootTables.LegendaryCantrips, BiotaDatabaseLock);

    private int? _maxSpellLevel;

    public int GetMaxSpellLevel()
    {
        if (_maxSpellLevel == null)
        {
            _maxSpellLevel =
                Biota.PropertiesSpellBook != null && Biota.PropertiesSpellBook.Count > 0
                    ? Biota.PropertiesSpellBook.Keys.Max(i => SpellLevelCache.GetSpellLevel(i))
                    : 0;
        }
        return _maxSpellLevel.Value;
    }

    /// <summary>
    /// Calculates the StatModVal x buffs to enter into the enchantment registry
    /// </summary>
    /// <param name="spell">A spell with a DotDuration</param>
    public float CalculateDotEnchantment_StatModValue(
    Spell spell,
    WorldObject target,
    WorldObject weapon,
    float statModVal
)
    {
        if (spell.DotDuration == 0)
        {
            return statModVal;
        }

        var enchantment_statModVal = statModVal;

        var creatureTarget = target as Creature;

        if (spell.Category == SpellCategory.AetheriaProcHealthOverTimeRaising)
        {
            return enchantment_statModVal;
        }

        if (spell.Category == SpellCategory.AetheriaProcDamageOverTimeRaising)
        {
            return enchantment_statModVal;
        }

        var player = this as Player;
        var creatureSource = this as Creature;

        var equippedWeapon = player.GetEquippedWeapon() ?? player.GetEquippedWand();

        var damageRatingMod = 1.0f;

        if (creatureSource != null)
        {
            var damageRating = creatureSource.GetDamageRating();

            if (player != null)
            {
                if (player.GetHeritageBonus(equippedWeapon))
                {
                    damageRating += 5;
                }

                if (target is Player)
                {
                    damageRating += player.GetPKDamageRating();
                }
            }
            damageRatingMod = Creature.GetPositiveRatingMod(damageRating);
        }

        if (spell.Category is SpellCategory.DFBleedDamage)
        {
            return enchantment_statModVal * damageRatingMod;
        }

        if (
            spell.Category != SpellCategory.NetherDamageOverTimeRaising
            && spell.Category != SpellCategory.NetherDamageOverTimeRaising2
            && spell.Category != SpellCategory.NetherDamageOverTimeRaising3
            && spell.Category != SpellCategory.BleedDamage
            && spell.Category != SpellCategory.HealKitRegen
            && spell.Category != SpellCategory.StaminaKitRegen
            && spell.Category != SpellCategory.ManaKitRegen
        )
        {
            _log.Error(
                $"{Name}.CalculateDamageOverTimeBase({spell.Id} - {spell.Name}, {target?.Name}) - unknown dot spell category {spell.Category}"
            );
            return enchantment_statModVal;
        }

        if (spell.Category is SpellCategory.HealKitRegen or SpellCategory.StaminaKitRegen or SpellCategory.ManaKitRegen)
        {
            return enchantment_statModVal;
        }

        var elementalDamageMod = 1.0f;
        var attributeDamageMod = 1.0f;

        if (creatureSource != null)
        {
            elementalDamageMod = GetCasterElementalDamageModifier(
                equippedWeapon,
                creatureSource,
                creatureTarget,
                spell.DamageType
            );

            var baseAttributeDamageMod = creatureSource.GetAttributeMod(creatureSource.GetEquippedWeapon(), true);
            var halfOfBonus = (baseAttributeDamageMod - 1.0f) * 0.5f;
            attributeDamageMod = 1.0f + halfOfBonus;
        }

        enchantment_statModVal *= elementalDamageMod * attributeDamageMod * damageRatingMod;

        //Console.WriteLine($"\nCalculateDotEnchantment_StatModValue()\n" +
        //    $" -elementalDamageMod: {elementalDamageMod}\n" +
        //    $" -attributeDamageMod: {attributeDamageMod}\n" +
        //    $" -damageRatingMod: {damageRatingMod}\n" +
        //    $" -statModVal: {enchantment_statModVal}");

        return enchantment_statModVal;
    }

    public void TryCastItemEnchantment_WithRedirects(Spell spell, WorldObject target, WorldObject itemCaster = null)
    {
        var caster = itemCaster ?? this;

        var creature = this as Creature;
        var player = this as Player;

        var targetCreature = target as Creature;
        var targetPlayer = target as Player;

        // if negative item spell, can be resisted by the wielder
        if (spell.IsHarmful)
        {
            var targetResist = targetCreature;

            if (targetResist == null && target?.WielderId != null)
            {
                targetResist = CurrentLandblock?.GetObject(target.WielderId.Value) as Creature;
            }

            // skip TryResistSpell() for non-player casters, they already performed it previously
            if (player != null && targetResist != null)
            {
                if (TryResistSpell(targetResist, spell, out var resistedMod, caster))
                {
                    return;
                }
            }
            // should this be set if the spell is invalid / 'fails to affect' below?
            if (creature != null && targetResist is Player playerTargetResist)
            {
                playerTargetResist.SetCurrentAttacker(creature);
            }
        }

        if (spell.IsImpenBaneType)
        {
            // impen / bane / brittlemail / lure

            // a lot of these will already be filtered out by IsInvalidTarget()
            if (targetCreature == null)
            {
                // targeting an individual item / wo
                HandleCastSpell(spell, target);
            }
            else
            {
                // targeting a creature
                if (targetPlayer == this)
                {
                    // targeting self
                    if (creature != null)
                    {
                        var items = creature
                            .EquippedObjects.Values.Where(i =>
                                (i.WeenieType == WeenieType.Clothing || i.IsShield) && i.IsEnchantable
                            )
                            .ToList();

                        foreach (var item in items)
                        {
                            HandleCastSpell(spell, item);
                        }

                        if (items.Count > 0)
                        {
                            DoSpellEffects(spell, this, creature);
                        }
                    }
                }
                else
                {
                    // targeting another player or monster
                    var item = targetCreature.EquippedObjects.Values.FirstOrDefault(i => i.IsShield && i.IsEnchantable);

                    if (item != null)
                    {
                        HandleCastSpell(spell, item);
                    }
                    else
                    {
                        // 'fails to affect'?
                        player?.Session.Network.EnqueueSend(
                            new GameMessageSystemChat(
                                $"You fail to affect {targetCreature.Name} with {spell.Name}",
                                ChatMessageType.Magic
                            )
                        );

                        if (
                            targetPlayer != null
                            && !targetPlayer.SquelchManager.Squelches.Contains(this, ChatMessageType.Magic)
                        )
                        {
                            targetPlayer.Session.Network.EnqueueSend(
                                new GameMessageSystemChat(
                                    $"{Name} fails to affect you with {spell.Name}",
                                    ChatMessageType.Magic
                                )
                            );
                        }
                    }
                }
            }
        }
        else if (spell.IsItemRedirectableType)
        {
            // blood loather, spirit loather, lure blade, turn blade, leaden weapon, hermetic void
            if (targetCreature == null)
            {
                // targeting an individual item / wo
                HandleCastSpell(spell, target);
            }
            else
            {
                // targeting a creature, try to redirect to primary weapon
                var weapon = spell.NonComponentTargetType switch
                {
                    ItemType.Weapon => targetCreature.GetEquippedWeapon(),
                    ItemType.Caster => targetCreature.GetEquippedWand(),
                    ItemType.WeaponOrCaster => targetCreature.GetEquippedWeapon() ?? targetCreature.GetEquippedWand(),
                    ItemType.MeleeWeapon => targetCreature.GetEquippedMeleeWeapon(),
                    ItemType.MissileWeapon => targetCreature.GetEquippedMissileWeapon(),
                    _ => null
                };

                if (weapon != null && weapon.IsEnchantable)
                {
                    HandleCastSpell(spell, weapon);
                }
                else
                {
                    // 'fails to affect'?
                    if (player != null)
                    {
                        player.Session.Network.EnqueueSend(
                            new GameMessageSystemChat(
                                $"You fail to affect {targetCreature.Name} with {spell.Name}",
                                ChatMessageType.Magic
                            )
                        );
                    }

                    if (
                        targetPlayer != null
                        && !targetPlayer.SquelchManager.Squelches.Contains(this, ChatMessageType.Magic)
                    )
                    {
                        targetPlayer.Session.Network.EnqueueSend(
                            new GameMessageSystemChat(
                                $"{Name} fails to affect you with {spell.Name}",
                                ChatMessageType.Magic
                            )
                        );
                    }
                }
            }
        }
        else
        {
            // all other item spells, cast directly on target
            HandleCastSpell(spell, target);
        }
    }

    public float ItemManaRateAccumulator { get; set; }

    public bool ItemManaDepletionMessage { get; set; }

    public void OnSpellsActivated()
    {
        IsAffecting = true;
        ItemManaRateAccumulator = 0;
        ItemManaDepletionMessage = false;
    }

    public void OnSpellsDeactivated()
    {
        IsAffecting = false;
    }

    private const double defaultIgnoreSomeMagicProjectileDamage = 0.25;

    public double? GetAbsorbMagicDamage()
    {
        var absorbMagicDamage = AbsorbMagicDamage;

        if (absorbMagicDamage == null && HasImbuedEffect(ImbuedEffectType.IgnoreSomeMagicProjectileDamage))
        {
            absorbMagicDamage = defaultIgnoreSomeMagicProjectileDamage;
        }

        return absorbMagicDamage;
    }

    /// <summary>
    /// For spells with NonComponentTargetType, returns the list of equipped items matching the target type
    /// </summary>
    private static List<WorldObject> GetNonComponentTargetTypes(Spell spell, Creature target)
    {
        switch (spell.NonComponentTargetType)
        {
            case ItemType.Vestements: // impen / bane
            case ItemType.Weapon: // blood drinker
            case ItemType.LockableMagicTarget: // strengthen lock
            case ItemType.Caster: // hermetic void
            case ItemType.WeaponOrCaster: // lure blade, defender cantrip, hermetic link cantrip, mukkir sense
            case ItemType.Item: // essence lull

                return target
                    .EquippedObjects.Values.Where(i =>
                        (i.ItemType & spell.NonComponentTargetType) != 0
                        && (i.ValidLocations & EquipMask.Selectable) != 0
                        && i.IsEnchantable
                    )
                    .ToList();
        }
        return null;
    }

    private void GenerateSupportSpellThreat(Spell spell, Creature playerTarget, int amount = 0)
    {
        var player = this as Player;

        if (player == null || playerTarget == null)
        {
            return;
        }

        if (playerTarget == player) // don't add support threat if player is targeting themself
        {
            return;
        }

        var targetThreatRange = 3.0; // casting support spells on another player adds threat to monsters that are close to the targeted player
        var casterThreatRange = 3.0; // casting any support spell adds threat to all creatures near the caster

        var nearbyMonstersOfTarget = playerTarget.GetNearbyMonsters(targetThreatRange);
        var nearbyMonstersOfCaster = player.GetNearbyMonsters(casterThreatRange);

        var threatAmount = 2 * spell.Level;

        if (spell.MetaSpellType == SpellType.Boost)
        {
            threatAmount = (uint)(amount / 2);
        }

        var threatenedByTargetSupport = new List<Creature>();

        foreach (var creature in nearbyMonstersOfTarget)
        {
            creature.IncreaseTargetThreatLevel(player, (int)threatAmount * 2);
            threatenedByTargetSupport.Add(creature);
        }

        // While increasing threat of enemies nearby caster, don't increase a monster's threat towards them again if they already received threat from a targeted support spell
        foreach (var creature in nearbyMonstersOfCaster)
        {
            var skip = false;

            foreach (var threatenedCreature in threatenedByTargetSupport)
            {
                if (creature.Guid == threatenedCreature.Guid)
                {
                    skip = true;
                }
            }

            if (skip)
            {
                continue;
            }

            creature.IncreaseTargetThreatLevel(player, (int)threatAmount);
        }
    }

    private float SelfTargetSpellProcMod(bool fromProc, Spell spell, WorldObject weapon, Player player)
    {
        if (!fromProc || player == null || weapon == null)
        {
            return 1.0f;
        }

        var spellcraft = (uint)(weapon.ItemSpellcraft ?? 1) + CheckForArcaneLoreSpecSpellcraftBonus(player);

        var playerSpellSkill =
            spell.School == MagicSchool.WarMagic
                ? player.GetModdedWarMagicSkill()
                : player.GetModdedLifeMagicSkill();

        var procSpellSkill = (int)(playerSpellSkill + spellcraft * 0.1);
        var mod = procSpellSkill / spell.Power;

        return Math.Clamp(mod, 0.5f, 2.0f);
    }

    public float GetWardMod(Creature caster, Creature target, float ignoreWardMod)
    {
        var wardLevel = target.GetWardLevel();

        if (caster is Player)
        {
            wardLevel = Convert.ToInt32(wardLevel * LevelScaling.GetMonsterArmorWardScalar(caster, target));
        }
        else if (target is Player)
        {
            wardLevel = Convert.ToInt32(wardLevel * LevelScaling.GetPlayerArmorWardScalar(target, caster));
        }

        var wardBuffDebuffMod = target.EnchantmentManager.GetWardMultiplicativeMod();

        return SkillFormula.CalcWardMod(wardLevel * ignoreWardMod * wardBuffDebuffMod);
    }
}

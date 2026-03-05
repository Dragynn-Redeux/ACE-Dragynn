using System;
using System.Collections.Generic;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

public class Food : Stackable
{
    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public Food(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public Food(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private void SetEphemeralValues()
    {
        ObjectDescriptionFlags |= ObjectDescriptionFlag.Food;

        if ((Spell2 == null || Spell2 == 0) && CooldownId == null)
        {
            CooldownDuration = 300;
            if (ItemType == ItemType.Food)
            {
                CooldownId = 10076;
            }
            else if (ItemType == ItemType.Misc)
            {
                CooldownId = 10077;
            }
        }
    }

    // WCID ranges for Long-effect essence foods and potions (no cooldown, spell-only buff).
    // Short-effect items share the same WCID base but have CooldownId set.
    private const uint LongEssenceBaseFoodWcidMin = 1053937u;
    private const uint LongEssenceBaseFoodWcidMax = 1053951u;
    private const uint LongEssenceFoodWcidMin   = 1054600u;
    private const uint LongEssenceFoodWcidMax   = 1054731u;
    private const uint LongEssenceBasePotionWcidMin = 1053957u;
    private const uint LongEssenceBasePotionWcidMax = 1053971u;
    private const uint LongEssencePotionWcidMin = 1054732u;
    private const uint LongEssencePotionWcidMax = 1054959u;

    /// <summary>
    /// This is raised by Player.HandleActionUseItem.<para />
    /// The item should be in the players possession.
    /// </summary>
    public override void ActOnUse(WorldObject activator)
    {
        if (!(activator is Player player))
        {
            return;
        }

        if (player.IsBusy || player.Teleporting || player.suicideInProgress)
        {
            player.SendWeenieError(WeenieError.YoureTooBusy);
            return;
        }

        if (player.IsJumping)
        {
            player.SendWeenieError(WeenieError.YouCantDoThatWhileInTheAir);
            return;
        }

        var motionCommand = GetUseSound() == Sound.Eat1 ? MotionCommand.Eat : MotionCommand.Drink;

        player.ApplyConsumable(motionCommand, () => ApplyConsumable(player));
    }

    /// <summary>
    /// Applies the boost from the consumable, broadcasts the sound,
    /// sends message to player, and consumes from inventory
    /// </summary>
    public void ApplyConsumable(Player player)
    {
        if (player.IsDead)
        {
            return;
        }

        // verify item is still valid
        if (player.FindObject(Guid.Full, Player.SearchLocations.MyInventory) == null)
        {
            //player.SendWeenieError(WeenieError.ObjectGone);   // results in 'Unable to move object!' transient error
            player.SendTransientError($"Cannot find the {Name}"); // custom message
            return;
        }

        // trying to use a dispel potion while pk timer is active
        // send error message and cancel - do not consume item
        if (SpellDID != null)
        {
            var spell = new Spell(SpellDID.Value);

            if (spell.MetaSpellType == SpellType.Dispel && !VerifyDispelPkStatus(this, player))
            {
                return;
            }
        }

        if (BoosterEnum != PropertyAttribute2nd.Undef)
        {
            BoostVital(player);
        }

        if (SpellDID != null || Spell2 != null)
        {
            TryDispelPreviousLongEssenceBuff(player);
        }

        if (SpellDID != null)
        {
            CastSpell(player);
        }

        if (Spell2 != null && Spell2 != 0)
        {
            CastAddedSpell(player);
        }

        var soundEvent = new GameMessageSound(player.Guid, GetUseSound(), 1.0f);
        player.EnqueueBroadcast(soundEvent);

        if (!UnlimitedUse)
        {
            player.TryConsumeFromInventoryWithNetworking(this, 1);
        }
    }

    public void BoostVital(Player player)
    {
        var vital = player.GetCreatureVital(BoosterEnum);

        if (vital == null)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"{Name} ({Guid}) contains invalid vital {BoosterEnum}",
                    ChatMessageType.Broadcast
                )
            );
            return;
        }

        // only apply to restoration food?
        var ratingMod = BoostValue > 0 ? player.GetHealingRatingMod() : 1.0f;

        var boostValue = (int)Math.Round(BoostValue * ratingMod);

        var vitalChange = (uint)Math.Abs(player.UpdateVitalDelta(vital, boostValue));

        if (BoosterEnum == PropertyAttribute2nd.Health)
        {
            if (BoostValue >= 0)
            {
                player.DamageHistory.OnHeal(vitalChange);
            }
            else
            {
                player.DamageHistory.Add(this, DamageType.Health, vitalChange);
            }
        }

        var verb = BoostValue >= 0 ? "restores" : "takes";

        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"The {Name} {verb} {vitalChange} points of your {BoosterEnum}.",
                ChatMessageType.Broadcast
            )
        );

        if (player.IsDead)
        {
            player.OnDeath(player.DamageHistory.LastDamager, DamageType.Health, false);
            player.Die();
        }
    }

    /// <summary>
    /// Before applying a Long-effect essence buff, dispels any previously active Long buff
    /// of the same category (cooking or alchemy) so only one can be active at a time.
    /// Short-effect items (CooldownId set) are excluded.
    /// </summary>
    private void TryDispelPreviousLongEssenceBuff(Player player)
    {
        if (CooldownId != null && CooldownId != 0)
        {
            return;
        }
        
        HashSet<uint> spellIds;
        if ((WeenieClassId >= LongEssenceFoodWcidMin && WeenieClassId <= LongEssenceFoodWcidMax) ||
            (WeenieClassId >= LongEssenceBaseFoodWcidMin && WeenieClassId <= LongEssenceBaseFoodWcidMax))
            
        {
            spellIds = TrophyEssence.AllCookFoodSpellIds;
        }
        else if ((WeenieClassId >= LongEssencePotionWcidMin && WeenieClassId <= LongEssencePotionWcidMax) ||
                 (WeenieClassId >= LongEssenceBasePotionWcidMin && WeenieClassId <= LongEssenceBasePotionWcidMax))
        {
            spellIds = TrophyEssence.AllAlchPotionSpellIds;
        }
        else
        {
            return;
        }

        var all = player.Biota.PropertiesEnchantmentRegistry.Clone(player.BiotaDatabaseLock);
        var toDispel = new List<PropertiesEnchantmentRegistry>();

        foreach (var enchantment in all)
        {
            if (spellIds.Contains((uint)enchantment.SpellId))
            {
                toDispel.Add(enchantment);
            }
        }

        if (toDispel.Count > 0)
        {
            player.EnchantmentManager.Dispel(toDispel);
        }
    }

    public void CastSpell(Player player)
    {
        var spell = new Spell(SpellDID.Value);

        if (spell.NotFound)
        {
            if (spell._spellBase != null)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat($"{spell.Name} spell not implemented, yet!", ChatMessageType.System)
                );
            }
            else
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat($"Invalid spell id {SpellDID ?? 0}", ChatMessageType.System)
                );
            }

            return;
        }

        // should be 'You cast', instead of 'Item cast'
        // omitting the item caster here, so player is also used for enchantment registry caster,
        // which could prevent some scenarios with spamming enchantments from multiple food sources to protect against dispels
        player.TryCastSpell(spell, player, this, tryResist: false);
    }

    public void CastAddedSpell(Player player)
    {
        var spell = new Spell(Spell2.Value);

        if (spell.NotFound)
        {
            if (spell._spellBase != null)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat($"{spell.Name} spell not implemented, yet!", ChatMessageType.System)
                );
            }
            else
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat($"Invalid added spell id {Spell2 ?? 0}", ChatMessageType.System)
                );
            }

            return;
        }

        player.TryCastSpell(spell, player, this, tryResist: false);
    }

    public Sound GetUseSound()
    {
        var useSound = UseSound;

        if (useSound == Sound.Invalid)
        {
            useSound = Sound.Eat1;
        }

        return useSound;
    }
}

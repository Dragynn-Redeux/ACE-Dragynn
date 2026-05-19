using System;
using System.Collections.Generic;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories.Tables;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using MotionCommand = ACE.Entity.Enum.MotionCommand;
using SpellId = ACE.Entity.Enum.SpellId;

namespace ACE.Server.WorldObjects;

public class SpellPurge : Stackable
{
    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public SpellPurge(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public SpellPurge(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private void SetEphemeralValues() { }

    private static void BroadcastSpellPurge(
        Player player,
        string spellName,
        WorldObject target,
        int  numberOfPearlsConsumed,
        bool success
    )
    {
        // send local broadcast
        if (success)
        {
            player.EnqueueBroadcast(
                new GameMessageSystemChat(
                    $"You successfully removes {spellName} from the {target.NameWithMaterial}, consuming {numberOfPearlsConsumed} Pearls of Purging.",
                    ChatMessageType.Broadcast
                ),
                8f,
                ChatMessageType.Broadcast
            );
        }
        else
        {
            player.EnqueueBroadcast(
                new GameMessageSystemChat(
                    $"You fails to remove {spellName} from the {target.NameWithMaterial}.",
                    ChatMessageType.Broadcast
                ),
                8f,
                ChatMessageType.Broadcast
            );
        }
    }

    public override void HandleActionUseOnTarget(Player player, WorldObject target)
    {
        UseObjectOnTarget(player, this, target);
    }

    private static bool TryRequireStableTarget(Player player, WorldObject target, bool endUse)
    {
        if (target.Retained)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {target.NameWithMaterial} is retained and cannot be altered.",
                    ChatMessageType.Craft
                )
            );

            if (endUse)
            {
                player.SendUseDoneEvent();
            }

            return true;
        }

        if (ForgeStageDisplay.IsAllowedStage(target, ForgeStage.Stable))
        {
            return false;
        }

        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"The {target.NameWithMaterial} must be stable before it can be altered.",
                ChatMessageType.Craft
            )
        );

        if (endUse)
        {
            player.SendUseDoneEvent();
        }

        return true;
    }

    public static void UseObjectOnTarget(Player player, WorldObject source, WorldObject target, bool confirmed = false)
    {
        var pearlStackSize = source.StackSize ?? 1;
        var targetWorkmanship = Math.Clamp((target.ItemWorkmanship ?? 1) - 1, 1, 10);
        var amountToAdd = targetWorkmanship * targetWorkmanship;

        if (player.IsBusy)
        {
            player.SendUseDoneEvent(WeenieError.YoureTooBusy);
            return;
        }

        if (target.WeenieType == source.WeenieType)
        {
            player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
            return;
        }

        if (!RecipeManager.VerifyUse(player, source, target, true))
        {
            if (!confirmed)
            {
                player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
            }
            else
            {
                player.SendTransientError(
                    "Either you or one of the items involved does not pass the requirements for this craft interaction."
                );
            }

            return;
        }

        if (target.Workmanship == null || target.Tier == null)
        {
            player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
            return;
        }

        if (TryRequireStableTarget(player, target, true))
        {
            return;
        }

        if (pearlStackSize < amountToAdd)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You require a stack of {amountToAdd} Pearls of Spell Purging to remove a spell from {target.NameWithMaterial}.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        var spellCount = 0;
        var allSpells = target.Biota.GetKnownSpellsIds(target.BiotaDatabaseLock);

        if (target.ProcSpell != null && target.ProcSpell != 0)
        {
            allSpells.Add((int)target.ProcSpell);
        }

        var spells = new List<int>();

        foreach (var spellId in allSpells)
        {
            spells.Add(spellId);
        }

        spellCount = spells.Count;
        if (spellCount == 0)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {target.NameWithMaterial} does not have any valid spells to remove.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        // if not confirmed yet, we select a spell from the item and assign the ID to the pearl's SpellToExtract property.
        // confirmation runs this method a second time after hitting yes
        // with multi-spell items, repeated "No" confirmations will cycle through the item's spells
        if (!confirmed)
        {
            if (target.RemainingConfirmations == null)
            {
                target.RemainingConfirmations = spellCount;
            }

            var spellToExtractRoll = (target.RemainingConfirmations ?? 1) - 1;
            var spellToExtractId = spells[spellToExtractRoll];

            source.SpellToExtract = (uint?)spellToExtractId;

            target.RemainingConfirmations--;
            if (target.RemainingConfirmations == 0)
            {
                target.RemainingConfirmations = null;
            }
        }

        if (source.SpellToExtract == null)
        {
            _log.Error("UseObjectOnTarget() - {Source}.SpellToExtract is null", source);
            return;
        }

        var chosenSpell = new Spell((uint)source.SpellToExtract);

        if (!confirmed)
        {
            var potentialArcaneLoreReq = CalculateArcaneLore(target, (int)chosenSpell.Id);
            var arcaneLoreString = potentialArcaneLoreReq > 0 ?
                $"The {target.Name}'s arcane lore activation requirement will become {potentialArcaneLoreReq} and" :
                $"The {target.Name} will not have an arcane lore requirement and";

            var confirmationMessage =
                $"Remove {chosenSpell.Name} from {target.NameWithMaterial}?\n\n" +
                $"If this item contains more than one spell, selecting 'No' will cycle through the remaining spells.\n\n" +
                $"{arcaneLoreString}" +
                $" {amountToAdd} Pearls of Purging will be consumed.\n\n";

            if (!player.ConfirmationManager.EnqueueSend(new Confirmation_CraftInteration(player.Guid, source.Guid, target.Guid), confirmationMessage))
            {
                player.SendUseDoneEvent(WeenieError.ConfirmationInProgress);
            }

            if (PropertyManager.GetBool("craft_exact_msg").Item)
            {
                var exactMsg = $"You have a 100% chance of removing a spell from {target.NameWithMaterial}.";

                player.Session.Network.EnqueueSend(new GameMessageSystemChat(exactMsg, ChatMessageType.Craft));
            }

            return;
        }

        var actionChain = new ActionChain();

        var animTime = 0.0f;

        player.IsBusy = true;

        if (player.CombatMode != CombatMode.NonCombat)
        {
            var stanceTime = player.SetCombatMode(CombatMode.NonCombat);
            actionChain.AddDelaySeconds(stanceTime);

            animTime += stanceTime;
        }

        animTime += player.EnqueueMotion(actionChain, MotionCommand.ClapHands);

        actionChain.AddAction(
            player,
            () =>
            {
                if (!RecipeManager.VerifyUse(player, source, target, true))
                {
                    player.SendTransientError(
                        "Either you or one of the items involved does not pass the requirements for this craft interaction."
                    );
                    return;
                }

                if (TryRequireStableTarget(player, target, false))
                {
                    return;
                }

                if (source.SpellToExtract == null)
                {
                    _log.Error("UseObjectOnTarget() - {Source}.SpellToRemove is null. Cannot perform spell purge.", source);
                    return;
                }

                if (target.ProcSpell == source.SpellToExtract)
                {
                    target.ProcSpell = null;

                    if (target is { WieldSkillType3: (int)Skill.LifeMagic or (int)Skill.WarMagic })
                    {
                        target.WieldSkillType3 = 0;
                        target.WieldDifficulty3 = 0;
                        target.WieldRequirements3 = 0;
                    }
                }

                target.Biota.TryRemoveKnownSpell((int)source.SpellToExtract.Value, target.BiotaDatabaseLock);
                target.ItemDifficulty = CalculateArcaneLore(target);

                player.EnqueueBroadcast(new GameMessageUpdateObject(target));
                player.TryConsumeFromInventoryWithNetworking(source, amountToAdd);

                var spellName = chosenSpell.Name;

                BroadcastSpellPurge(player, spellName, target, amountToAdd, true);
            }
        );

        player.EnqueueMotion(actionChain, MotionCommand.Ready);

        actionChain.AddAction(
            player,
            () =>
            {
                player.SendUseDoneEvent();
                player.IsBusy = false;
            }
        );

        actionChain.EnqueueChain();

        player.NextUseTime = DateTime.UtcNow.AddSeconds(animTime);
    }

    private static int CalculateArcaneLore(WorldObject target, int? potentialRemovedSpell = null)
    {
        var numSpells = 0;
        var increasedDifficulty = 0.0f;

        var targetSpellBook = target.Biota.PropertiesSpellBook;

        if (targetSpellBook == null)
        {
            return 0;
        }

        var spellBook = new Dictionary<int,float>(targetSpellBook);

        if (potentialRemovedSpell != null)
        {
            spellBook.Remove(potentialRemovedSpell.Value);
        }

        int MINOR = 0,
            MAJOR = 1,
            EPIC = 2,
            LEGENDARY = 3;

        foreach (SpellId spellId in spellBook.Keys)
        {
            numSpells++;

            var cantripLevels = SpellLevelProgression.GetSpellLevels(spellId);

            var cantripLevel = cantripLevels.IndexOf(spellId);

            if (cantripLevel == MINOR)
            {
                increasedDifficulty += 5;
            }
            else if (cantripLevel == MAJOR)
            {
                increasedDifficulty += 10;
            }
            else if (cantripLevel == EPIC)
            {
                increasedDifficulty += 15;
            }
            else if (cantripLevel == LEGENDARY)
            {
                increasedDifficulty += 20;
            }
        }


        var tier = (target.Tier ?? 1) - 1;

        if (target.ProcSpell != null)
        {
            numSpells++;
            increasedDifficulty += Math.Max(5 * tier, 5);
        }

        var finalDifficulty = 0;
        var armorSlots = target.ArmorSlots ?? 1;
        var spellsPerSlot = (float)numSpells / armorSlots;

        if (spellsPerSlot > 1 || target.ProcSpell != null)
        {
            var baseDifficulty = ActivationDifficultyPerTier(tier);

            finalDifficulty = baseDifficulty + (int)(increasedDifficulty / armorSlots);
        }

        return finalDifficulty;
    }

    private static int ActivationDifficultyPerTier(int tier)
    {
        switch (tier)
        {
            case 1:
                return 75;
            case 2:
                return 175;
            case 3:
                return 225;
            case 4:
                return 275;
            case 5:
                return 325;
            case 6:
                return 375;
            case 7:
                return 425;
            default:
                return 50;
        }
    }
}

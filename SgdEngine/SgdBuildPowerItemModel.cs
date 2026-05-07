using RoR2;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace GeneticsArtifact.SgdEngine
{
    internal readonly struct SgdBuildPowerAxisWeights
    {
        public readonly float Hp;
        public readonly float MoveSpeed;
        public readonly float AttackSpeed;
        public readonly float AttackDamage;

        public SgdBuildPowerAxisWeights(float hp, float moveSpeed, float attackSpeed, float attackDamage)
        {
            Hp = hp;
            MoveSpeed = moveSpeed;
            AttackSpeed = attackSpeed;
            AttackDamage = attackDamage;
        }

        public static SgdBuildPowerAxisWeights operator +(SgdBuildPowerAxisWeights a, SgdBuildPowerAxisWeights b)
        {
            return new SgdBuildPowerAxisWeights(
                a.Hp + b.Hp,
                a.MoveSpeed + b.MoveSpeed,
                a.AttackSpeed + b.AttackSpeed,
                a.AttackDamage + b.AttackDamage);
        }
    }

    internal static class SgdBuildPowerItemModel
    {
        public static SgdBuildPowerAxisWeights EstimateInventoryBonus(Inventory inventory)
        {
            if (inventory == null)
            {
                return default;
            }

            var total = default(SgdBuildPowerAxisWeights);
            foreach (ItemIndex itemIndex in EnumerateAcquiredItems(inventory))
            {
                int count = inventory.GetItemCount(itemIndex);
                if (count <= 0)
                {
                    continue;
                }

                total += EstimateItem(ItemCatalog.GetItemDef(itemIndex), count);
            }

            return total;
        }

        private static SgdBuildPowerAxisWeights EstimateItem(ItemDef def, int count)
        {
            if (def == null || count <= 0)
            {
                return default;
            }

            float stack = Mathf.Log(1f + count);
            float hp = 0f;
            float moveSpeed = 0f;
            float attackSpeed = 0f;
            float attackDamage = 0f;

            if (HasTag(def, "Healing"))
            {
                hp += 0.08f * stack;
            }
            if (HasTag(def, "Utility"))
            {
                hp += 0.03f * stack;
            }
            if (HasTag(def, "MobilityRelated") || HasTag(def, "SprintRelated"))
            {
                moveSpeed += 0.10f * stack;
            }
            if (HasTag(def, "Damage"))
            {
                attackDamage += 0.08f * stack;
            }
            if (HasTag(def, "OnKillEffect"))
            {
                attackDamage += 0.03f * stack;
            }
            if (HasTag(def, "EquipmentRelated"))
            {
                attackSpeed += 0.02f * stack;
                attackDamage += 0.02f * stack;
            }

            ApplyKnownItemWeights(def.name, stack, ref hp, ref moveSpeed, ref attackSpeed, ref attackDamage);
            return new SgdBuildPowerAxisWeights(hp, moveSpeed, attackSpeed, attackDamage);
        }

        private static void ApplyKnownItemWeights(
            string itemName,
            float stack,
            ref float hp,
            ref float moveSpeed,
            ref float attackSpeed,
            ref float attackDamage)
        {
            switch (itemName ?? "")
            {
                case "FlatHealth":
                case "BoostHp":
                case "Infusion":
                case "Knurl":
                case "PersonalShield":
                    hp += 0.10f * stack;
                    break;
                case "ArmorPlate":
                case "Bear":
                case "BearVoid":
                case "OutOfCombatArmor":
                case "SprintArmor":
                    hp += 0.08f * stack;
                    break;
                case "Hoof":
                case "SprintBonus":
                case "SpeedBoostPickup":
                case "SpeedOnPickup":
                case "MoveSpeedOnKill":
                    moveSpeed += 0.14f * stack;
                    break;
                case "AttackSpeedAndMoveSpeed":
                    moveSpeed += 0.08f * stack;
                    attackSpeed += 0.08f * stack;
                    break;
                case "Syringe":
                case "BoostAttackSpeed":
                case "AttackSpeedOnCrit":
                case "AttackSpeedPerNearbyAllyOrEnemy":
                case "EnergizedOnEquipmentUse":
                    attackSpeed += 0.15f * stack;
                    break;
                case "Crowbar":
                case "CritGlasses":
                case "CritDamage":
                case "BossDamageBonus":
                case "NearbyDamageBonus":
                case "FragileDamageBonus":
                case "BoostDamage":
                    attackDamage += 0.12f * stack;
                    break;
                case "BleedOnHit":
                case "BleedOnHitVoid":
                case "Missile":
                case "MissileVoid":
                case "ChainLightning":
                case "ChainLightningVoid":
                case "FireRing":
                case "IceRing":
                case "StickyBomb":
                case "Behemoth":
                case "Dagger":
                    attackDamage += 0.10f * stack;
                    break;
            }
        }

        private static bool HasTag(ItemDef def, string tagName)
        {
            if (def?.tags == null)
            {
                return false;
            }

            foreach (ItemTag tag in def.tags)
            {
                if (tag.ToString() == tagName)
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable EnumerateAcquiredItems(Inventory inventory)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var field = typeof(Inventory).GetField("itemAcquisitionOrder", flags);
            var acquiredItems = field?.GetValue(inventory) as IEnumerable;
            if (acquiredItems == null)
            {
                yield break;
            }

            foreach (object item in acquiredItems)
            {
                if (item is ItemIndex itemIndex)
                {
                    yield return itemIndex;
                }
            }
        }
    }
}

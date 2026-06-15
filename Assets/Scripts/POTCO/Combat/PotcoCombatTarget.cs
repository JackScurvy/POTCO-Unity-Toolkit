using System.Collections.Generic;
using UnityEngine;

namespace POTCO.Combat
{
    [DisallowMultipleComponent]
    public sealed class PotcoCombatTarget : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth = 100f;

        private readonly List<PotcoWeaponStatusEffect> activeEffects = new List<PotcoWeaponStatusEffect>();

        public float MaxHealth => maxHealth;
        public float CurrentHealth => currentHealth;
        public bool IsAlive => currentHealth > 0f;
        public GameObject LastAttacker { get; private set; }
        public PotcoWeaponDefinition LastWeapon { get; private set; }
        public PotcoWeaponSkill LastSkill { get; private set; }
        public float LastDamageApplied { get; private set; }
        public float LastHealingApplied { get; private set; }
        public PotcoWeaponStatusEffect LastEffectApplied { get; private set; }
        public IReadOnlyList<PotcoWeaponStatusEffect> ActiveEffects
        {
            get
            {
                PurgeExpiredEffects();
                return activeEffects;
            }
        }

        private void Awake()
        {
            maxHealth = Mathf.Max(1f, maxHealth);
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        }

        public void ResetHealth(float health)
        {
            maxHealth = Mathf.Max(1f, health);
            currentHealth = maxHealth;
            LastAttacker = null;
            LastWeapon = null;
            LastSkill = null;
            LastDamageApplied = 0f;
            LastHealingApplied = 0f;
            LastEffectApplied = null;
            activeEffects.Clear();
        }

        public float ApplyWeaponDamage(float damage, GameObject attacker, PotcoWeaponDefinition weapon, PotcoWeaponSkill skill)
        {
            float applied = Mathf.Max(0f, damage);
            if (applied <= 0f || !IsAlive)
            {
                LastDamageApplied = 0f;
                LastHealingApplied = 0f;
                return 0f;
            }

            currentHealth = Mathf.Max(0f, currentHealth - applied);
            LastAttacker = attacker;
            LastWeapon = weapon;
            LastSkill = skill;
            LastDamageApplied = applied;
            LastHealingApplied = 0f;
            return applied;
        }

        public float ApplyWeaponHealing(float healing, GameObject attacker, PotcoWeaponDefinition weapon, PotcoWeaponSkill skill)
        {
            float requested = Mathf.Max(0f, healing);
            if (requested <= 0f || !IsAlive)
            {
                LastDamageApplied = 0f;
                LastHealingApplied = 0f;
                return 0f;
            }

            float previousHealth = currentHealth;
            currentHealth = Mathf.Min(maxHealth, currentHealth + requested);
            float applied = currentHealth - previousHealth;
            LastAttacker = attacker;
            LastWeapon = weapon;
            LastSkill = skill;
            LastDamageApplied = 0f;
            LastHealingApplied = applied;
            return applied;
        }

        public PotcoWeaponStatusEffect ApplyWeaponStatusEffect(int effectId, float durationSeconds, GameObject attacker, PotcoWeaponDefinition weapon, PotcoWeaponSkill skill)
        {
            if (effectId <= 0 || !IsAlive)
                return null;

            PurgeExpiredEffects();

            var effect = new PotcoWeaponStatusEffect(effectId, durationSeconds, Time.time, attacker, weapon, skill);
            LastAttacker = attacker;
            LastWeapon = weapon;
            LastSkill = skill;
            LastEffectApplied = effect;

            if (effect.DurationSeconds <= 0f)
                return effect;

            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                if (activeEffects[i].EffectId == effectId)
                    activeEffects.RemoveAt(i);
            }

            activeEffects.Add(effect);
            return effect;
        }

        public bool HasActiveEffect(int effectId)
        {
            if (effectId <= 0)
                return false;

            PurgeExpiredEffects();
            foreach (PotcoWeaponStatusEffect effect in activeEffects)
            {
                if (effect.EffectId == effectId)
                    return true;
            }

            return false;
        }

        private void PurgeExpiredEffects()
        {
            float now = Time.time;
            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                if (activeEffects[i].IsExpired(now))
                    activeEffects.RemoveAt(i);
            }
        }
    }
}

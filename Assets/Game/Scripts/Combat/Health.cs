using UnityEngine;

namespace MiniGTA
{
    /// <summary>Who or what caused damage. Lets reactions and crime reporting differ by source.</summary>
    public enum DamageSource { Unknown, Melee, Bullet, Vehicle, Fall }

    public struct DamageInfo
    {
        public float Amount;
        public Vector3 Point;
        /// <summary>Direction the hit travelled, i.e. away from the attacker.</summary>
        public Vector3 Direction;
        public DamageSource Source;
        public GameObject Attacker;

        public DamageInfo(float amount, Vector3 point, Vector3 direction,
                          DamageSource source, GameObject attacker)
        {
            Amount = amount;
            Point = point;
            Direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            Source = source;
            Attacker = attacker;
        }
    }

    /// <summary>
    /// Hit points and armour, shared by the player, pedestrians and police.
    ///
    /// One component for every damageable thing so that a bullet, a fist and a car bumper all
    /// go through the same path -- which is also the single place crime reporting can hook
    /// into without every weapon needing to know about the wanted system.
    /// </summary>
    public class Health : MonoBehaviour
    {
        [Header("Hit points")]
        public float MaxHealth = 100f;
        [Tooltip("Armour soaks a share of incoming damage until it is gone.")]
        public float MaxArmor = 100f;
        public float StartingArmor;

        [Tooltip("Fraction of a hit absorbed by armour while any remains.")]
        [Range(0f, 1f)] public float ArmorAbsorption = 0.65f;

        [Header("Regeneration")]
        [Tooltip("Health regained per second once out of danger. 0 disables regen.")]
        public float RegenPerSecond;
        public float RegenDelay = 6f;

        [Header("State")]
        [SerializeField] bool _invulnerable;

        float _health;
        float _armor;
        float _sinceDamage;

        public float CurrentHealth => _health;
        public float CurrentArmor => _armor;
        public float HealthFraction => Mathf.Clamp01(_health / Mathf.Max(1f, MaxHealth));
        public float ArmorFraction => MaxArmor <= 0f ? 0f : Mathf.Clamp01(_armor / MaxArmor);
        public bool IsDead { get; private set; }

        public bool Invulnerable { get => _invulnerable; set => _invulnerable = value; }

        public event System.Action<DamageInfo> Damaged;
        public event System.Action<DamageInfo> Died;
        public event System.Action Revived;

        void Awake()
        {
            _health = MaxHealth;
            _armor = Mathf.Clamp(StartingArmor, 0f, MaxArmor);
        }

        void Update()
        {
            if (IsDead || RegenPerSecond <= 0f) return;

            _sinceDamage += Time.deltaTime;
            if (_sinceDamage < RegenDelay || _health >= MaxHealth) return;

            _health = Mathf.Min(MaxHealth, _health + RegenPerSecond * Time.deltaTime);
        }

        /// <summary>Returns true if this hit killed the target.</summary>
        public bool Apply(DamageInfo info)
        {
            if (IsDead || _invulnerable || info.Amount <= 0f) return false;

            _sinceDamage = 0f;

            float remaining = info.Amount;
            if (_armor > 0f)
            {
                float soaked = Mathf.Min(_armor, remaining * ArmorAbsorption);
                _armor -= soaked;
                remaining -= soaked;
            }

            _health = Mathf.Max(0f, _health - remaining);
            Damaged?.Invoke(info);

            if (_health > 0f) return false;

            IsDead = true;
            Died?.Invoke(info);
            return true;
        }

        public void AddArmor(float amount) => _armor = Mathf.Clamp(_armor + amount, 0f, MaxArmor);
        public void Heal(float amount)
        {
            if (IsDead) return;
            _health = Mathf.Clamp(_health + amount, 0f, MaxHealth);
        }

        /// <summary>Full reset. Used on respawn after a bust or a wasting.</summary>
        public void Revive(float healthFraction = 1f, float armor = 0f)
        {
            IsDead = false;
            _health = MaxHealth * Mathf.Clamp01(healthFraction);
            _armor = Mathf.Clamp(armor, 0f, MaxArmor);
            _sinceDamage = 0f;
            Revived?.Invoke();
        }
    }
}

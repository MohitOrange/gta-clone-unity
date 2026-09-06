using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace HelicopterAttack
{
    public class DamageControl : MonoBehaviour
    {

        [HideInInspector]
        public float Damage = 100;

        public float MaxDamage = 100;

        [HideInInspector]
        public bool IsDead = false;

        [HideInInspector]
        public Vector3 LastDamageDirection;
        [HideInInspector]
        public float LastDamageFactor = 1;
        // Use this for initialization
        void Start()
        {
            Damage = MaxDamage;
            IsDead = false;
            LastDamageDirection = Vector3.forward;
        }

        // Update is called once per frame
        void Update()
        {

        }

        public void ApplyDamage(float dmg, Vector3 dir, float DamageFactor)
        {
            LastDamageDirection = dir;
            LastDamageDirection.Normalize();
            LastDamageFactor = DamageFactor;
            Damage -= dmg;
            if (Damage <= 0)
            {
                Damage = 0;
                IsDead = true;
            }
        }

        public void AddHealth(float h)
        {
            Damage = Mathf.Clamp(Damage + h, 0, MaxDamage);
        }
    }
}

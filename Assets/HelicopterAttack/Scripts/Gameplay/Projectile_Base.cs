using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace HelicopterAttack
{
    public class Projectile_Base : MonoBehaviour
    {

        public GameObject HitParticlePrefab1;
        [HideInInspector]
        public GameObject Creator;

        public float Speed = 100;
        public float Damage = 1;

        public GameObject m_DetachingParticle;
        // Use this for initialization
        void Start()
        {

        }

        void Update()
        {
            RaycastHit[] hits = Physics.SphereCastAll(transform.position, .2f, transform.forward, Speed * Time.deltaTime);
            foreach (RaycastHit hit in hits)
            {
                Collider col = hit.collider;
                if (Vector3.Dot(hit.normal, transform.forward) < 0)
                {
                    if (col.gameObject.tag == "Player")
                    {

                    }
                    else if (col.gameObject.tag == "Block")
                    {
                        Destroyed(hit.point);

                        Rigidbody rb = col.gameObject.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            //rb.AddForceAtPosition((Damage * 100) * transform.forward + new Vector3(0, 2000, 0), transform.position);
                        }

                        DamageControl d = col.gameObject.GetComponent<DamageControl>();
                        if (d != null)
                        {
                            d.ApplyDamage(Damage, transform.forward, 1);
                        }
                    }

                }
            }

            transform.position += Speed * Time.deltaTime * transform.forward;
        }

        public virtual void Destroyed(Vector3 pos)
        {

            GameObject obj = Instantiate(HitParticlePrefab1);
            obj.transform.position = pos;
            Destroy(obj, 6);
            //obj.transform.localScale = 0.4f * Vector3.one;

            if (m_DetachingParticle != null)
            {
                m_DetachingParticle.transform.SetParent(null);
                Destroy(m_DetachingParticle, 6);
            }


            Destroy(gameObject);
        }
    }
}
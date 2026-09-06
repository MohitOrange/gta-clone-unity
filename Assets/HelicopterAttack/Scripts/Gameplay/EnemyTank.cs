using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace HelicopterAttack
{
    public class EnemyTank : MonoBehaviour
    {
        public GameObject m_Explosion;
        bool dead = false;
        // Start is called before the first frame update
        void Start()
        {
            GameControl.m_Current.m_MaxTargetCount++;
        }

        // Update is called once per frame
        void Update()
        {
            if (!dead)
            {
                if (GetComponent<DamageControl>().IsDead)
                {
                    GameObject obj = Instantiate(m_Explosion);
                    obj.transform.position = transform.position;
                    obj.transform.localScale = 2 * Vector3.one;

                    GameControl.m_Current.m_TargetDestroyedCount++;
                    dead = true;
                    GetComponent<Collider>().enabled = false;
                    Destroy(gameObject);
                }
            }
        }
    }
}

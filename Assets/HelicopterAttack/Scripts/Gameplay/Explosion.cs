using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HelicopterAttack
{
    public class Explosion : MonoBehaviour
    {

        // Use this for initialization
        public float Radius = 5;
        public float M_Damage = 10;
        void Start()
        {
            //CameraControl.MainCameraControl.StartShake(1f, 1f);

            //if (transform.position.y <= 2)
            //{
            //    GameObject obj = Instantiate(GlobalContents.MainGlobalContent.DecalsPrefabs[0]);
            //    Vector3 pos = transform.position;
            //    pos.y = 0.1f;
            //    obj.transform.position = pos;
            //    Destroy(obj, 30);
            //}


            Collider[] colls = Physics.OverlapSphere(transform.position, Radius);
            foreach (Collider col in colls)
            {
                if (col.gameObject.tag == "Player")
                {

                }
                else if (col.gameObject.tag == "Block")
                {
                    Rigidbody rb = col.gameObject.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.AddForceAtPosition(col.gameObject.transform.position - transform.forward, transform.position);
                    }

                    DamageControl d = col.gameObject.GetComponent<DamageControl>();
                    if (d != null)
                    {
                        d.ApplyDamage(M_Damage, transform.forward, 1);
                    }
                }
            }

            //Destroy(gameObject);
        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}
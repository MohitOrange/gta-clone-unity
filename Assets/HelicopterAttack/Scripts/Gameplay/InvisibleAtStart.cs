using UnityEngine;
using System.Collections;
namespace HelicopterAttack
{
    public class InvisibleAtStart : MonoBehaviour
    {


        // Use this for initialization
        void Start()
        {
            GetComponent<Renderer>().enabled = false;

        }
    }
}
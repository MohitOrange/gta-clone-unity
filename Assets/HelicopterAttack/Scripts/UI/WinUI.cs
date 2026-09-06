using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HelicopterAttack
{
    public class WinUI : MonoBehaviour
    {

        void Start()
        {

        }

        void Update()
        {
        }

        public void Continue()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }
}

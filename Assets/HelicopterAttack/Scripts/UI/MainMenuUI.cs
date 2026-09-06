using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
namespace HelicopterAttack
{
    public class MainMenuUI : MonoBehaviour
    {
        [SerializeField, Space]
        private DataStorage m_DataStorage;
        // Start is called before the first frame update
        void Start()
        {
            m_DataStorage.Load();
        }

        // Update is called once per frame
        void Update()
        {
        }

        public void BtnExit()
        {
            Application.Quit();
        }

        public void BtnMap(int num)
        {
            switch (num)
            {
                case 0:
                    SceneManager.LoadScene("Scene_1");
                    break;

            }
        }
    }
}

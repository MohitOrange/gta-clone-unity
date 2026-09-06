using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;


namespace HelicopterAttack
{
    public class GameUI : MonoBehaviour
    {

        public Text m_TargetCount;
        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {
            m_TargetCount.text = GameControl.m_Current.m_TargetDestroyedCount.ToString() + " / " + GameControl.m_Current.m_MaxTargetCount.ToString();
        }


        public void BtnExit()
        {
            SceneManager.LoadScene("Scene_MainMenu");
        }

    }
}
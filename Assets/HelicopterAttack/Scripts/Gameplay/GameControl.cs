using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HelicopterAttack
{
    public class GameControl : MonoBehaviour
    {
        public static GameControl m_Current;

        [HideInInspector]
        public int m_GameState = 0;

        public int m_MaxTargetCount = 0;
        public int m_TargetDestroyedCount = 0;

        void Awake()
        {
            m_Current = this;
        }
        // Start is called before the first frame update
        void Start()
        {
            m_GameState = 0;

        }

        // Update is called once per frame
        void Update()
        {
            if (m_GameState == 0)
            {
                if (m_TargetDestroyedCount >= m_MaxTargetCount)
                {
                    HandleWin();
                }
            }

        }

        public void HandleGameOver()
        {
            m_GameState = 2;
            //CameraControl.Current.m_ShakeEnabled = false;
            UIControl.Current.m_InGameUI.SetActive(false);
            UIControl.Current.m_LoseUI.SetActive(true);
        }



        public void HandleWin()
        {
            m_GameState = 1;
            UIControl.Current.m_InGameUI.SetActive(false);
            UIControl.Current.m_WinUI.SetActive(true);
        }


    }
}
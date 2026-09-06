using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace HelicopterAttack
{
    public class Joystick_Stick : MonoBehaviour
    {
        public Image Back;
        public Image Stick;

        [HideInInspector]
        public bool Hold;
        [HideInInspector]
        public Vector3 HitPosition;
        [HideInInspector]
        public Vector3 StickDirection;

        [HideInInspector]
        public Vector3 m_OriginPosition;

        [HideInInspector]
        public Vector2 m_StartPosition;

        [HideInInspector]
        public bool m_PrevTouch = false;

        public RectTransform m_MainRect;

        public bool m_Left = true;

        // Use this for initialization
        void Start()
        {
            Hold = false;
            m_StartPosition = Back.rectTransform.anchoredPosition;
        }

        // Update is called once per frame
        void Update()
        {
            Hold = false;
            Vector3[] PointerPos = new Vector3[2];

            if (Application.platform == RuntimePlatform.Android)
            {
                PointerPos = new Vector3[Input.touchCount];
                for (int i = 0; i < Input.touchCount; i++)
                {
                    PointerPos[i] = Input.touches[i].position;
                }
            }
            else
            {
                if (Input.GetMouseButton(0))
                {
                    PointerPos = new Vector3[1];
                    PointerPos[0] = Input.mousePosition;
                }
                else
                {
                    PointerPos = new Vector3[0];
                    //PointerPos[0] = new Vector3(3000, -1000, 0);
                }
            }

            HitPosition = Vector3.zero;
            bool found = false;
            Vector3 foundPos = Vector3.zero;
            for (int i = 0; i < PointerPos.Length; i++)
            {
                Vector3 pos = PointerPos[i];
                pos.z = 0;
                pos.x = pos.x / Screen.width;
                pos.y = pos.y / Screen.height;

                Vector2 p2 = Vector2.zero;
                p2.x = pos.x * m_MainRect.sizeDelta.x;
                p2.y = pos.y * m_MainRect.sizeDelta.y;

                if (Vector2.Distance(p2, m_StartPosition) <= 300)
                {
                    foundPos = PointerPos[i];
                    found = true;
                    if (!m_PrevTouch)
                    {
                        m_PrevTouch = true;
                        //m_OriginPosition = PointerPos[i];
                    }
                    break;
                }
            }

            if (!found)
            {
                m_PrevTouch = false;
                StickDirection = Vector3.zero;

                Back.rectTransform.anchoredPosition = m_StartPosition;
                Stick.rectTransform.anchoredPosition = m_StartPosition;
                //Stick.enabled = false;
            }
            else
            {
                //back
                Vector3 pos = m_OriginPosition;
                pos.z = 0;
                pos.x = pos.x / Screen.width;
                pos.y = pos.y / Screen.height;

                Vector2 p2 = Vector2.zero;
                p2.x = pos.x * m_MainRect.sizeDelta.x;
                p2.y = pos.y * m_MainRect.sizeDelta.y;
                //Back.rectTransform.anchoredPosition = p2;

                //stick
                Stick.enabled = true;
                pos = foundPos;
                pos.z = 0;
                pos.x = pos.x / Screen.width;
                pos.y = pos.y / Screen.height;

                p2 = Vector2.zero;
                p2.x = pos.x * m_MainRect.sizeDelta.x;
                p2.y = pos.y * m_MainRect.sizeDelta.y;
                Stick.rectTransform.anchoredPosition = p2;

                float MaxDistance = Screen.height / 5f;

                StickDirection = Stick.rectTransform.anchoredPosition - m_StartPosition;
                StickDirection = StickDirection / MaxDistance;
                StickDirection = Vector3.ClampMagnitude(StickDirection, 1);

                Vector3 dir = foundPos - m_OriginPosition;
                if (dir.magnitude > MaxDistance)
                {
                    //m_OriginPosition = Vector3.Lerp(m_OriginPosition, foundPos, 5 * Time.deltaTime);
                }
            }

        }
    }
}

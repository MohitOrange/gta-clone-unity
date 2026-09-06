using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace HelicopterAttack
{
    public class InputControl : MonoBehaviour
    {
        [HideInInspector]
        public Vector3 m_WorldAimPosition;

        //--inputs
        [HideInInspector]
        public Vector3 m_Movement;
        [HideInInspector]
        public Vector3 m_Look;
        [HideInInspector]
        public bool m_Fire;
        [HideInInspector]
        public bool m_Fire2;


        public bool m_mobileControl = false;

        public static InputControl m_Main;

        void Awake()
        {
            m_Main = this;
        }
        // Start is called before the first frame update
        void Start()
        {
            m_WorldAimPosition = Vector3.zero;

            if (!m_mobileControl)
            {
                Cursor.lockState = CursorLockMode.Locked;
            }
        }

        // Update is called once per frame
        void Update()
        {
            m_Movement = Vector3.zero;
            m_Look = Vector3.zero;
            m_Fire = false;
            m_Fire2 = false;

            if (m_mobileControl)
            {
                if (Joystick.m_Main != null)
                {
                    m_Movement.x = Joystick.m_Main.LeftStick.StickDirection.x;
                    m_Movement.z = Joystick.m_Main.LeftStick.StickDirection.y;

                    m_Look.x = Joystick.m_Main.RightStick.StickDirection.x;
                    m_Look.y = Joystick.m_Main.RightStick.StickDirection.y;

                    if (Joystick.m_Main.ButtonA.Hold)
                        m_Fire = true;

                    if (Joystick.m_Main.ButtonB.Hold)
                        m_Fire2 = true;
                }
            }
            else
            {
                m_Movement.x = Input.GetAxis("Horizontal");
                m_Movement.z = Input.GetAxis("Vertical");

                m_Look.y = Input.GetAxis("Mouse Y");
                m_Look.x = Input.GetAxis("Mouse X");

                if (Input.GetMouseButton(0))
                    m_Fire = true;

                if (Input.GetMouseButton(1))
                    m_Fire2 = true;

            }

            m_Movement = Vector3.ClampMagnitude(m_Movement, 1.0f);
            m_Look = Vector3.ClampMagnitude(m_Look, 1.0f);
        }
    }
}
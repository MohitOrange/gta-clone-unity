using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;
namespace HelicopterAttack
{
    public class Joystick : MonoBehaviour
    {

        [HideInInspector]
        public Vector2 StickOffset;

        public Joystick_Stick LeftStick;
        public Joystick_Stick RightStick;
        public JoystickButton ButtonA;
        public JoystickButton ButtonB;
        public JoystickButton ButtonC;


        public GraphicRaycaster Canvas;
        public static Joystick m_Main;

        [HideInInspector]
        public List<RaycastResult> RayCastResults;
        [HideInInspector]
        public List<GameObject> RayCastObjects;
        void Awake()
        {
            m_Main = this;
        }

        void Start()
        {

        }

        void Update()
        {

        }

    }
}
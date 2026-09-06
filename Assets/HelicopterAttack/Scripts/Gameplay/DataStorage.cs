using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace HelicopterAttack
{
    [CreateAssetMenu(fileName = "DataStorage", menuName = "CustomObjects/DataStorage", order = 1)]
    public class DataStorage : ScriptableObject
    {

        [HideInInspector]
        public int MapNum;

        public void Save()
        {
            PlayerPrefs.SetInt("MapNum", MapNum);
            PlayerPrefs.Save();
        }

        public void Load()
        {
            MapNum = PlayerPrefs.GetInt("MapNum");
        }

    }
}
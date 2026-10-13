namespace URPLabStudio
{
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Lab_Exit : MonoBehaviour {

    void Awake()
    {
        //Screen.SetResolution(1920, 1080, true);
    }

    public void Btn_Close()
    {
        Btn_Ads();
        Application.Quit();
    }

    public void Btn_Ads()
    {
        Application.OpenURL("https://www.artstation.com/user-c17fba9f17ec1c3b");
    }
}
}

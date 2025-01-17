using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class LocalInteractToggle : UdonSharpBehaviour
{
    [SerializeField]
    private GameObject toggleTarget;
    
    public override void Interact()
    {
        // ターゲットのアクティブ状態を反転
        toggleTarget.SetActive(!toggleTarget.activeSelf);
    }
}


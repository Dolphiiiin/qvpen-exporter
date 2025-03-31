using UdonSharp;
using UnityEngine;

namespace QvPenExporter
{
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
}


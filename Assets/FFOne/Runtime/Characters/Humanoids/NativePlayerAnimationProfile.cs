using UnityEngine;

namespace FFOne.Content
{
    public sealed class NativePlayerAnimationProfile : ScriptableObject
    {
        public RuntimeAnimatorController Controller;
        public Avatar Avatar;
        public string[] ClipNames;
    }
}

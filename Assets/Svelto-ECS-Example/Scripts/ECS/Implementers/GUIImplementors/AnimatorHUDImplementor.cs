using Svelto.ECS.Example.Survive.Components.HUD;
using UnityEngine;

namespace Svelto.ECS.Example.Survive.Implementors.HUD
{
    public class AnimatorHUDImplementor: MonoBehaviour, IImplementor, IAnimatorHUDComponent
    {
        Animator animator;
        Animator IAnimatorHUDComponent.hudAnimator { get { return animator; } }

        void Awake()
        {
            animator = GetComponent<Animator>();
        }
    }
}
using UnityEngine;
using static RedFrontier.Outpost.OutpostHud;

namespace RedFrontier.Outpost
{
    public sealed class OutpostHudPreview : MonoBehaviour
    {
        [SerializeField] private OutpostHud hud;

        private void Start()
        {
            hud.SetResource(Resource.Energy, 30, 100);
            hud.SetResource(Resource.Water, 20, 100);
            hud.SetResource(Resource.Food, 20, 100);
            hud.SetResource(Resource.Oxygen, 20, 100);
            hud.SetResource(Resource.Metal, 100, 100, .5f);
        }
    }
}

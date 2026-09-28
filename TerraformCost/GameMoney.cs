using Game.City;
using Game.Simulation;
using Unity.Entities;

namespace TerraformCost
{
    /// <summary>Small helper for TestApi/TestStrokeSystem to read/nudge PlayerMoney directly (test-only
    /// convenience; the real charging path never writes money except from TerraformChargeSystem).</summary>
    public static class GameMoney
    {
        private static World World => World.DefaultGameObjectInjectionWorld;

        public static bool TryRead(out int money)
        {
            money = 0;
            World world = World;
            CitySystem citySystem = world?.GetExistingSystemManaged<CitySystem>();
            if (citySystem == null) return false;
            EntityManager em = world.EntityManager;
            Entity city = citySystem.City;
            if (city == Entity.Null || !em.HasComponent<PlayerMoney>(city)) return false;
            money = em.GetComponentData<PlayerMoney>(city).money;
            return true;
        }

        /// <summary>Test-only helper (TestApi.AddMoney): adds (or removes, if negative) money directly.</summary>
        public static bool TryAdd(int delta)
        {
            World world = World;
            CitySystem citySystem = world?.GetExistingSystemManaged<CitySystem>();
            if (citySystem == null) return false;
            EntityManager em = world.EntityManager;
            Entity city = citySystem.City;
            if (city == Entity.Null || !em.HasComponent<PlayerMoney>(city)) return false;
            PlayerMoney money = em.GetComponentData<PlayerMoney>(city);
            money.Add(delta);
            em.SetComponentData(city, money);
            return true;
        }
    }
}

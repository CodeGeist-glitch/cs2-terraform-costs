using System;
using Game;
using Game.City;
using Game.Simulation;
using Unity.Entities;

namespace TerraformCost.Systems
{
    /// <summary>
    /// Plan section 3.2. Runs at SystemUpdatePhase.PreTool (T-1 fix, 0.9.1 - see Mod.cs for why: this
    /// phase runs every real frame including while the game is paused, unlike the old GameSimulation
    /// registration). Once per real frame: publishes Budget.AvailableMoney/HasPlayerMoney from the real
    /// PlayerMoney (guarded by HasComponent, so the map editor - which has no CitySystem.City/PlayerMoney,
    /// research/40 A4 - is automatically free with no extra GameMode check), drains whatever
    /// ApplyBrushPatch/StrokeVolume approved this frame and subtracts it, then ticks StrokeVolume so a
    /// released stroke gets its settle-time final measurement even though no more ApplyBrush calls are
    /// coming. Money is only ever written here, never from inside the Harmony patch.
    /// </summary>
    public class TerraformChargeSystem : GameSystemBase
    {
        private CitySystem m_CitySystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
        }

        protected override void OnUpdate()
        {
            try
            {
                Entity city = m_CitySystem.City;
                if (city == Entity.Null || !EntityManager.HasComponent<PlayerMoney>(city))
                {
                    Budget.HasPlayerMoney = false;
                    Budget.AvailableMoney = int.MaxValue;
                    Budget.DrainPendingCharge(); // nothing to charge against; discard safely
                    return;
                }

                Budget.HasPlayerMoney = true;
                PlayerMoney money = EntityManager.GetComponentData<PlayerMoney>(city);

                int charge = Budget.DrainPendingCharge();
                if (charge > 0)
                {
                    money.Subtract(charge);
                    EntityManager.SetComponentData(city, money);
                }

                Budget.AvailableMoney = money.money;
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "TerraformChargeSystem.OnUpdate failed; disabling further charges this session");
                Budget.HasPlayerMoney = false;
                Budget.AvailableMoney = int.MaxValue;
                Enabled = false;
                return;
            }

            try
            {
                StrokeVolume.Tick(UnityEngine.Time.frameCount);
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "StrokeVolume.Tick failed");
            }
        }
    }
}

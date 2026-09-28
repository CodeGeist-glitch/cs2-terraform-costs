using System;
using System.Linq;

namespace TerraformCost
{
    /// <summary>
    /// Hard Mode Continued (Capili81) also charges for terraforming as part of a larger economy overhaul
    /// (plan section 3.3 / research/40 design implications). We don't conflict with it (it patches the
    /// same ApplyBrush call, Harmony allows multiple patches), but the user would likely be charged twice,
    /// so we just warn - our mod stays active and the user decides.
    /// </summary>
    public static class Compat
    {
        private static bool? s_HardModeContinuedLoaded;

        public static bool HardModeContinuedLoaded
        {
            get
            {
                if (s_HardModeContinuedLoaded == null)
                {
                    try
                    {
                        s_HardModeContinuedLoaded = AppDomain.CurrentDomain.GetAssemblies()
                            .Any(a => (a.GetName().Name ?? string.Empty).IndexOf("HardMode", StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                    catch (Exception e)
                    {
                        Mod.log.Warn("Compat.HardModeContinuedLoaded check failed: " + e.Message);
                        s_HardModeContinuedLoaded = false;
                    }
                }
                return s_HardModeContinuedLoaded.Value;
            }
        }
    }
}

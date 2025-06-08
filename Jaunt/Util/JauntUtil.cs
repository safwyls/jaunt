using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Jaunt.Util
{
    internal static class JauntUtil
    {
        public static float GetStaminaDeficitMultiplier(float currentStamina, float maxStamina, float threshold)
        {
            float midpoint = maxStamina * threshold;

            if (currentStamina >= midpoint)
                return 0f;

            float deficit = 1f - (currentStamina / midpoint);  // 0 at midpoint, 1 at 0 stamina
            return deficit * deficit;  // Quadratic curve for gradual increase
        }
    }
}

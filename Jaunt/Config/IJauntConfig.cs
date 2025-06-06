namespace Jaunt.Config
{
    public interface IJauntConfig
    {
        // Stamina
        bool EnableStamina { get; set; }

        // Hud
        bool HideStaminaOnFull { get; set; }
        string StaminaBarLocation { get; set; }
        float StaminaBarWidthMultiplier { get; set; }
        float StaminaBarXOffset { get; set; }
        float StaminaBarYOffset { get; set; }
        bool ShowGaitIcon { get; set; }
        float IconOffsetX { get; set; }
        float IconOffsetY { get; set; }
        float IconSize { get; set; }

        // Debugging
        bool DebugMode { get; set; }
    }

    public class JauntConfig
    {
        // Global Stamina Settings (these apply to all mods using Jaunt)
        public float GlobalMaxStaminaMultiplier { get; set; } = 1f;
        public float GlobalStaminaRegenMultiplier { get; set; } = 1f;

        // Global Stamina Costs (these apply to all mods using Jaunt)
        public float GlobalSwimStaminaCostMultiplier { get; set; } = 1f;
        public float GlobalSprintStaminaCostMultiplier { get; set; } = 1f;

        // Default values for stamina settings
        public float DefaultMaxStamina { get; set; } = 100f;
        public float DefaultSprintFatigue { get; set; } = 0.2f;
        public float DefaultSwimFatigue { get; set; } = 0.2f;
        public float DefaultBaseFatigueRate { get; set; } = 1f;
        public float DefaultStaminaRegenRate { get; set; } = 1f;
        public float DefaultRegenPenaltyWounded { get; set; } = 0.5f;
        public float DefaultRegenPenaltyMounted { get; set; } = 0.3f;

        public bool GlobalDebugMode { get; set; } = true;

        public static IJauntConfig ChildConfig { get; private set; }

        public static void RegisterConfig(IJauntConfig config)
        {
            ChildConfig = config;
        }
    }
}

namespace Jaunt.Config
{
    public interface IJauntConfig
    {
        // Stamina
        bool EnableStamina { get; set; }
        float GlobalMaxStaminaMultiplier { get; set; }
        float GlobalStaminaRegenMultiplier { get; set; }
        
        // Stamina Costs
        float GlobalSwimStaminaCostMultiplier { get; set; }
        float GlobalSprintStaminaCostMultiplier { get; set; }

        // Hud
        bool HideStaminaOnFull { get; set; }
        string StaminaBarLocation { get; set; }
        float StaminaBarWidthMultiplier { get; set; }
        float StaminaBarXOffset { get; set; }
        float StaminaBarYOffset { get; set; }
        bool ShowHudIcon { get; set; }
        float IconOffsetX { get; set; }
        float IconOffsetY { get; set; }
        float IconSize { get; set; }

        // Debugging
        bool DebugMode { get; set; }
    }

    public static class JauntConfig
    {
        public static IJauntConfig Config { get; private set; }

        public static void Register(IJauntConfig config)
        {
            Config = config;
        }
    }
}

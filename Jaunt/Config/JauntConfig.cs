namespace Jaunt.Config
{
    public class JauntConfig
    {
        // Global Stamina Settings (these apply to all mods using Jaunt)
        public float GlobalMaxStaminaMultiplier { get; set; } = 1f;
        public float GlobalStaminaRegenMultiplier { get; set; } = 1f;

        // Global Stamina Costs (these apply to all mods using Jaunt)
        public float GlobalSwimStaminaCostMultiplier { get; set; } = 1f;
        public float GlobalSprintStaminaCostMultiplier { get; set; } = 1f;

        // Stamina
        public bool EnableStamina { get; set; } = true;
        // Hud
        public bool HideStaminaOnFull { get; set; } = false;
        public string StaminaBarLocation { get; set; } = "AboveHealth";
        public float StaminaBarWidthMultiplier { get; set; } = 1f;
        public float StaminaBarXOffset { get; set; } = 0f;
        public float StaminaBarYOffset { get; set; } = 0f;
        public bool ShowGaitIcon { get; set; } = true;
        public float IconOffsetX { get; set; } = -400f;
        public float IconOffsetY { get; set; } = -99f;
        public float IconSize { get; set; } = 42f;

        // Default values for stamina settings
        public float DefaultMaxStamina { get; set; } = 100f;
        public float DefaultSprintFatigue { get; set; } = 0.2f;
        public float DefaultSwimFatigue { get; set; } = 0.2f;
        public float DefaultBaseFatigueRate { get; set; } = 1f;
        public float DefaultStaminaRegenRate { get; set; } = 1f;
        public float DefaultRegenPenaltyWounded { get; set; } = 0.5f;
        public float DefaultRegenPenaltyMounted { get; set; } = 0.3f;
        public float DefaultExhaustionThreshold { get; set; } = 0.1f;
        public bool DefaultDontFleeWhenExhausted { get; set; } = false;
        
        // Audio Settings
        public int MaxLoadedSounds { get; set; } = 3;

        public bool GlobalDebugMode { get; set; } = true;
    }
}

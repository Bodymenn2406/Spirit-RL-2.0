namespace Spirit.Core.Config
{
    public class NeedsConfig
    {
        // base drain per minute
        public float BaseHungerPerMin { get; init; } = 0.08f;
        public float BaseThirstPerMin { get; init; } = 0.16f;

        // activity multipliers
        public float IdleMult { get; init; } = 1.0f;
        public float WalkJogMult { get; init; } = 1.2f;
        public float SprintMult { get; init; } = 2.5f;
        public float SwimMult { get; init; } = 3.0f;
        public float InVehicleMult { get; init; } = 0.8f;
        public float AfkMult { get; init; } = 0.6f;

        // thresholds
        public float LowThreshold { get; init; } = 25f;
        public float MidThreshold { get; init; } = 15f;
        public float CriticalThreshold { get; init; } = 8f;

        // debuffs / effects
        public bool EnableSprintBlockOnCritical { get; init; } = true;
        public bool EnableHpTickOnCriticalThirst { get; init; } = true;
        public int HpTickAmount { get; init; } = 1;
        public int HpTickIntervalMs { get; init; } = 3_000;

        // respawn defaults (per design)
        public float RespawnHunger { get; init; } = 60f;
        public float RespawnThirst { get; init; } = 40f;

        // change notifications EPS
        public float UiEps { get; init; } = 0.05f;

        public static NeedsConfig Default { get; } = new NeedsConfig();
    }
}

using RoR2;

namespace GeneticsArtifact.SgdEngine
{
    /// <summary>
    /// Latest runtime telemetry for SGD DDA (minimal for now).
    /// </summary>
    public static class SgdRuntimeState
    {
        public static bool HasVirtualPower { get; private set; }
        public static SgdVirtualPowerSample VirtualPower { get; private set; }
        public static string VirtualPowerBodyName { get; private set; } = "";

        public static void Clear()
        {
            HasVirtualPower = false;
            VirtualPower = default;
            VirtualPowerBodyName = "";
        }

        public static void SetVirtualPower(SgdVirtualPowerSample sample, CharacterBody body)
        {
            HasVirtualPower = true;
            VirtualPower = sample;
            VirtualPowerBodyName = body != null ? body.GetDisplayName() : "";
        }
    }
}


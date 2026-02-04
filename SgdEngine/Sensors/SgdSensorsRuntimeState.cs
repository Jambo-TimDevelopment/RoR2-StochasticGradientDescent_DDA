using RoR2;

namespace GeneticsArtifact.SgdEngine
{
    public static class SgdSensorsRuntimeState
    {
        public static bool HasSample { get; private set; }
        public static SgdSensorsSample Sample { get; private set; }
        public static string PlayerBodyName { get; private set; } = "";

        public static void Clear()
        {
            HasSample = false;
            Sample = default;
            PlayerBodyName = "";
        }

        public static void Set(SgdSensorsSample sample, CharacterBody body)
        {
            HasSample = true;
            Sample = sample;
            PlayerBodyName = body != null ? body.GetDisplayName() : "";
        }
    }
}


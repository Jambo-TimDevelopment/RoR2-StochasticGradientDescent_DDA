using System;

namespace GeneticsArtifact.Telemetry
{
    internal static class H3GaDecisionObserver
    {
        private static GeneEngineDriver _subscribedDriver;

        public static int TotalLearnSteps { get; private set; }

        public static void Reset()
        {
            if (_subscribedDriver != null)
            {
                _subscribedDriver.GEDPostLearningEvent -= OnLearned;
            }

            _subscribedDriver = null;
            TotalLearnSteps = 0;
        }

        public static void TickSubscribe()
        {
            var driver = GeneEngineDriver.instance;
            if (driver == null || ReferenceEquals(driver, _subscribedDriver))
            {
                return;
            }

            if (_subscribedDriver != null)
            {
                _subscribedDriver.GEDPostLearningEvent -= OnLearned;
            }

            _subscribedDriver = driver;
            _subscribedDriver.GEDPostLearningEvent += OnLearned;
        }

        private static void OnLearned(object sender, EventArgs e)
        {
            TotalLearnSteps++;
        }
    }
}

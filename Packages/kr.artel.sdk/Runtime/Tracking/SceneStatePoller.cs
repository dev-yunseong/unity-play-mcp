using System;
using Artel.Protocol.Dto;
using Artel.Protocol.Mapping;

namespace Artel.Tracking
{
    internal interface ISceneSnapshotScanner
    {
        SceneScanResult Scan();
    }

    internal sealed class SceneStatePoller
    {
        private readonly ISceneSnapshotScanner scanner;
        private readonly SceneStateHashTracker hashTracker;
        private readonly float intervalSeconds;
        private float nextScanTime;

        public SceneStatePoller(
            ISceneSnapshotScanner scanner,
            SceneStateHashTracker hashTracker,
            float intervalSeconds)
        {
            this.scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            this.hashTracker = hashTracker ?? throw new ArgumentNullException(nameof(hashTracker));

            if (intervalSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "Scan interval must be greater than zero.");
            }

            this.intervalSeconds = intervalSeconds;
        }

        public void Reset(float currentTime)
        {
            hashTracker.Reset();
            nextScanTime = currentTime + intervalSeconds;
        }

        public ScenePollResult ScanNow()
        {
            var scanResult = scanner.Scan();
            var scene = SceneSnapshotMapper.ToDto(scanResult.Scene);
            hashTracker.Observe(scene);
            return new ScenePollResult(scanResult, scene);
        }

        public bool TryPoll(float currentTime, out ScenePollResult result)
        {
            result = null;
            if (currentTime < nextScanTime)
            {
                return false;
            }

            nextScanTime = currentTime + intervalSeconds;

            var scanResult = scanner.Scan();
            var scene = SceneSnapshotMapper.ToDto(scanResult.Scene);
            if (!hashTracker.Observe(scene))
            {
                return false;
            }

            result = new ScenePollResult(scanResult, scene);
            return true;
        }
    }

    internal sealed class ScenePollResult
    {
        public ScenePollResult(SceneScanResult scanResult, SceneDto scene)
        {
            ScanResult = scanResult;
            Scene = scene;
        }

        public SceneScanResult ScanResult { get; }
        public SceneDto Scene { get; }
    }
}

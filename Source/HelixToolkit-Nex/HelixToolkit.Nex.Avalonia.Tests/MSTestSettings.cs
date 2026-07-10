// Avalonia controls have thread-affinity expectations; run tests serially to keep the
// styled-property change-notification observations deterministic.
[assembly: DoNotParallelize]

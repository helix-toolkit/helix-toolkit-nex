// Tests in this assembly create ECS `World` instances via `World.CreateWorld()`. The ECS
// enforces a hard global cap on the number of simultaneously-live worlds (Limits.MaxWorldId,
// currently 15). Class-level parallelism ran many world-creating property-test classes
// concurrently (each holding one or more worlds per FsCheck iteration), which intermittently
// exceeded that global cap and failed with "Number of worlds exceeds maximum supported size".
// Serializing the assembly keeps the peak number of concurrently-live worlds low (a single
// test method creates and disposes its worlds sequentially), so the global cap is never hit.
[assembly: DoNotParallelize]

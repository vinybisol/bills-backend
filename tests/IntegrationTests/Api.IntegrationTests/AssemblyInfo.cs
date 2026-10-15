// Every fixture resets the shared test database (Respawn) once in [OneTimeSetUp]. Running
// fixtures concurrently would wipe data out from under fixtures that are still executing,
// so the whole assembly is pinned to sequential execution.
[assembly: NonParallelizable]
[assembly: LevelOfParallelism(1)]
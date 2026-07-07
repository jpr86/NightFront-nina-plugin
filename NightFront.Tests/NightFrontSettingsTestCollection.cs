using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    // Settings.Default.NightFrontDataFolder is a process-wide static. xUnit runs different test
    // classes in parallel by default, so every test class that mutates it must share this collection
    // - collection members always run sequentially relative to each other, even though they still run
    // in parallel with unrelated test classes.
    [CollectionDefinition("NightFrontSettings", DisableParallelization = true)]
    public class NightFrontSettingsTestCollection {
    }
}

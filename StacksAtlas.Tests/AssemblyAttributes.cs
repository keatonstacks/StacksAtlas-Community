using Xunit;

// Disable parallel test execution so that static state, temp database directories,
// and environment variables do not collide across test collections.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Eshop.Api.Tests;

/// <summary>
/// Tests that move the shared clock (<c>api.Clock.Advance</c>) go in this collection:
/// <c>[Collection(nameof(ClockMoving))]</c>. xUnit runs it on its own after the parallel tests, so moving
/// time never expires another test's SMS code or token mid-flight. Move time forward only.
/// </summary>
[CollectionDefinition(nameof(ClockMoving), DisableParallelization = true)]
public sealed class ClockMoving;

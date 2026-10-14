using Shouldly;

namespace OrderFlow.Testing;

/// <summary>
/// Waits for something that must happen. Use instead of the test harness's
/// <c>Consumed.Any</c> / <c>Published.Any</c> for positive checks: those give up as soon as
/// the harness looks idle, which on a busy machine can be before the message is handled
/// (seen week 7: about 1 run in 8). Negative checks ("nothing was published") still use
/// <c>Any</c>, because waiting for idle is exactly right there. Linked into each test project.
/// </summary>
internal static class Eventually
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public static async Task True(Func<bool> condition, string what, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new ShouldAssertException($"Timed out after {(timeout ?? DefaultTimeout).TotalSeconds} s waiting for: {what}");
            }

            await Task.Delay(20);
        }
    }
}

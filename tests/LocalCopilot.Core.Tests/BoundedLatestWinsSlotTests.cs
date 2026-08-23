using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class BoundedLatestWinsSlotTests
{
    [TestMethod]
    public void TryPublish_NewerItemReplacesOnlyPendingItem()
    {
        BoundedLatestWinsSlot<string> slot = new();

        Assert.IsTrue(slot.TryPublish("first", out string? firstReplaced));
        Assert.IsNull(firstReplaced);

        Assert.IsTrue(slot.TryPublish("second", out string? secondReplaced));
        Assert.AreEqual("first", secondReplaced);

        Assert.IsTrue(slot.TryTake(out string? pending));
        Assert.AreEqual("second", pending);
        Assert.IsFalse(slot.TryTake(out _));
    }

    [TestMethod]
    public void Complete_ReturnsPendingAndRejectsFuturePublications()
    {
        BoundedLatestWinsSlot<string> slot = new();

        Assert.IsTrue(slot.TryPublish("pending", out _));

        Assert.AreEqual("pending", slot.Complete());
        Assert.IsNull(slot.Complete());
        Assert.IsFalse(slot.TryPublish("late", out string? replaced));
        Assert.IsNull(replaced);
        Assert.IsFalse(slot.TryTake(out _));
    }

    [TestMethod]
    public void ConcurrentPublish_LeavesExactlyOnePendingItem()
    {
        BoundedLatestWinsSlot<object> slot = new();
        int replacements = 0;

        Parallel.For(
            0,
            128,
            _ =>
            {
                bool published = slot.TryPublish(
                    new object(),
                    out object? replaced);

                Assert.IsTrue(published);

                if (replaced is not null)
                {
                    Interlocked.Increment(ref replacements);
                }
            });

        Assert.AreEqual(127, replacements);
        Assert.IsTrue(slot.TryTake(out object? pending));
        Assert.IsNotNull(pending);
        Assert.IsFalse(slot.TryTake(out _));
    }

    [TestMethod]
    public void TryPublish_NullItem_Throws()
    {
        BoundedLatestWinsSlot<string> slot = new();

        Assert.ThrowsExactly<ArgumentNullException>(
            () => slot.TryPublish(null!, out _));
    }
}

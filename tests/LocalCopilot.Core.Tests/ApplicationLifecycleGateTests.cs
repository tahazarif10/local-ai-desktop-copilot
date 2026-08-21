using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class ApplicationLifecycleGateTests
{
    [TestMethod]
    public void NewGate_IsCreatedAndNotRunning()
    {
        ApplicationLifecycleGate gate =
            new();

        Assert.AreEqual(
            ApplicationLifecycleState.Created,
            gate.State);

        Assert.IsFalse(
            gate.IsRunning);
    }

    [TestMethod]
    public void TryStart_TransitionsExactlyOnce()
    {
        ApplicationLifecycleGate gate =
            new();

        Assert.IsTrue(
            gate.TryStart());

        Assert.IsFalse(
            gate.TryStart());

        Assert.AreEqual(
            ApplicationLifecycleState.Running,
            gate.State);

        Assert.IsTrue(
            gate.IsRunning);
    }

    [TestMethod]
    public void TryStop_FromRunning_TransitionsExactlyOnce()
    {
        ApplicationLifecycleGate gate =
            new();

        Assert.IsTrue(
            gate.TryStart());

        Assert.IsTrue(
            gate.TryStop());

        Assert.IsFalse(
            gate.TryStop());

        Assert.AreEqual(
            ApplicationLifecycleState.Stopped,
            gate.State);

        Assert.IsFalse(
            gate.IsRunning);
    }

    [TestMethod]
    public void TryStop_BeforeStart_PreventsLateStart()
    {
        ApplicationLifecycleGate gate =
            new();

        Assert.IsFalse(
            gate.TryStop());

        Assert.IsFalse(
            gate.TryStart());

        Assert.AreEqual(
            ApplicationLifecycleState.Stopped,
            gate.State);
    }

    [TestMethod]
    public void TryDispose_IsTerminalAndIdempotent()
    {
        ApplicationLifecycleGate gate =
            new();

        Assert.IsTrue(
            gate.TryStart());

        Assert.IsTrue(
            gate.TryDispose());

        Assert.IsFalse(
            gate.TryDispose());

        Assert.IsFalse(
            gate.TryStart());

        Assert.IsFalse(
            gate.TryStop());

        Assert.AreEqual(
            ApplicationLifecycleState.Disposed,
            gate.State);
    }

    [TestMethod]
    public void ConcurrentStart_AllowsOneWinner()
    {
        ApplicationLifecycleGate gate =
            new();

        int winners =
            0;

        Parallel.For(
            0,
            64,
            _ =>
            {
                if (gate.TryStart())
                {
                    Interlocked.Increment(
                        ref winners);
                }
            });

        Assert.AreEqual(
            1,
            winners);

        Assert.AreEqual(
            ApplicationLifecycleState.Running,
            gate.State);
    }
}

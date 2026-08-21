namespace LocalCopilot_App.Services;

public enum ApplicationLifecycleState
{
    Created,
    Running,
    Stopped,
    Disposed
}

public sealed class ApplicationLifecycleGate
{
    private readonly object
        _gate =
            new();

    private ApplicationLifecycleState
        _state =
            ApplicationLifecycleState.Created;

    public ApplicationLifecycleState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool IsRunning =>
        State ==
            ApplicationLifecycleState.Running;

    public bool TryStart()
    {
        lock (_gate)
        {
            if (_state !=
                ApplicationLifecycleState.Created)
            {
                return false;
            }

            _state =
                ApplicationLifecycleState.Running;

            return true;
        }
    }

    public bool TryStop()
    {
        lock (_gate)
        {
            if (_state ==
                ApplicationLifecycleState.Created)
            {
                _state =
                    ApplicationLifecycleState.Stopped;

                return false;
            }

            if (_state !=
                ApplicationLifecycleState.Running)
            {
                return false;
            }

            _state =
                ApplicationLifecycleState.Stopped;

            return true;
        }
    }

    public bool TryDispose()
    {
        lock (_gate)
        {
            if (_state ==
                ApplicationLifecycleState.Disposed)
            {
                return false;
            }

            _state =
                ApplicationLifecycleState.Disposed;

            return true;
        }
    }
}

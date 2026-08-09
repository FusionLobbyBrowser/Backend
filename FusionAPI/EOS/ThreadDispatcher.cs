using System.Collections.Concurrent;

namespace FusionAPI.EOS;

internal sealed class ThreadDispatcher : IDisposable
{
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    private readonly SynchronizationContext _context;
    private bool _disposed;

    internal SynchronizationContext Context => _context;

    internal ThreadDispatcher()
    {
        _context = new EOSSynchronizationContext(this);

        _thread = new Thread(RunLoop)
        {
            Name = "EOS-Thread",
            IsBackground = true,
        };
        _thread.Start();
    }

    internal bool IsOnThread => Thread.CurrentThread == _thread;

    internal Task RunOnThreadAsync(Action action)
    {
        if (IsOnThread)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    internal Task<T> RunOnThreadAsync<T>(Func<T> func)
    {
        if (IsOnThread)
            return Task.FromResult(func());

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    internal Task<T> RunOnThreadAsync<T>(Func<Task<T>> func)
    {
        if (IsOnThread)
            return func();

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(() =>
        {
            var task = func();
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    tcs.SetException(t.Exception);
                else
                    tcs.SetResult(t.Result);
            });
        });
        return tcs.Task;
    }

    internal void Post(Action action) => _queue.Enqueue(action);

    private void RunLoop()
    {
        SynchronizationContext.SetSynchronizationContext(_context);

        while (!_cts.IsCancellationRequested)
        {
            while (_queue.TryDequeue(out var action))
            {
                try { action(); }
                catch { }
            }

            Thread.Sleep(1);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }
}

internal sealed class EOSSynchronizationContext : SynchronizationContext
{
    private readonly ThreadDispatcher _dispatcher;

    internal EOSSynchronizationContext(ThreadDispatcher dispatcher)
        => _dispatcher = dispatcher;

    public override void Post(SendOrPostCallback d, object? state)
        => _dispatcher.Post(() => d(state));

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (_dispatcher.IsOnThread)
        {
            d(state);
            return;
        }

        var mre = new ManualResetEventSlim(false);
        Exception? ex = null;
        _dispatcher.Post(() =>
        {
            try { d(state); }
            catch (Exception e) { ex = e; }
            finally { mre.Set(); }
        });
        mre.Wait();
        if (ex != null) throw ex;
    }

    public override SynchronizationContext CreateCopy() => new EOSSynchronizationContext(_dispatcher);
}
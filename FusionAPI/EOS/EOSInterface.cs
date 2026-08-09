using FusionAPI.Interfaces;

namespace FusionAPI.EOS;

internal abstract class EOSInterface
{
    internal virtual Task<bool> InitializeAsync(ILogger logger, ThreadDispatcher dispatched)
    {
        return Task.FromResult(true);
    }

    internal virtual void Tick()
    {
    }

    internal virtual void Shutdown()
    {
    }
}
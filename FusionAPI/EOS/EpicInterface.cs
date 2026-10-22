using FusionAPI.Data.Interfaces;

namespace FusionAPI.EOS;

internal abstract class EpicInterface
{
    internal virtual Task<bool> InitializeAsync(ILogger logger, ThreadDispatcher dispatcher)
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
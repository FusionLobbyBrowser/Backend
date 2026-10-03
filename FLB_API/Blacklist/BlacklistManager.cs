using System.Reflection;
using System.Text.Json;

using FLB_API;

namespace FLB_API.Blacklist
{
    public class BlacklistManager(FusionAPI.Interfaces.ILogger logger)
    {
        public FusionAPI.Interfaces.ILogger Logger { get; } = logger;

        private readonly List<IBlacklistModule> _modules = [];

        public IReadOnlyList<IBlacklistModule> Modules => _modules.AsReadOnly();

        public async Task<bool> IsAllowed(CustomLobbyInfo info)
        {
            foreach (var module in Modules)
            {
                if (!await module.IsLobbyAllowed(info))
                    return false;
            }
            return true;
        }

        public async Task RegisterModule<TModule>() where TModule : IBlacklistModule
            => await RegisterModule(typeof(TModule));

        public async Task RegisterModule(Type type)
        {
            if (!typeof(IBlacklistModule).IsAssignableFrom(type))
                throw new ArgumentException("Type is not a security module.");

            if (Modules.Any(m => m.GetType() == type))
                throw new ArgumentException("Module of this type is already registered.");

            var obj = Activator.CreateInstance(type);
            if (obj != null)
            {
                var module = (IBlacklistModule)obj;
                if (await module.InitAsync(this, Logger))
                    _modules.Add(module);
            }
            else
            {
                throw new ArgumentException("The created object from type is null"); // this shouldn't happen
            }
        }

        public async Task RegisterFromAssembly(Assembly assembly)
        {
            var all = assembly.GetTypes().Where(t => typeof(IBlacklistModule).IsAssignableFrom(t) && t.IsClass);
            foreach (var type in all)
                await RegisterModule(type);
        }

        public void UnregisterModule<TModule>() where TModule : IBlacklistModule
            => UnregisterModule(typeof(TModule));

        public void UnregisterModule(Type type)
        {
            if (!type.IsSubclassOf(typeof(IBlacklistModule)))
                throw new ArgumentException("Type does not inherit from ISecurityModule");

            var module = Modules.FirstOrDefault(m => m.GetType() == type);
            if (module != null)
                _modules.Remove(module);
            else
                throw new ArgumentException("Module of this type is not registered.");
        }

        public void UnregisterFromAssembly(Assembly assembly)
        {
            var all = assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(IBlacklistModule)));
            foreach (var type in all)
                UnregisterModule(type);
        }
    }
}
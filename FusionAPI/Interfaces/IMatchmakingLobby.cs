using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FusionAPI.Interfaces
{
    public interface IMatchmakingLobby
    {
        public ulong Owner { get; }

        public bool IsOwnerMe { get; }

        public string GetData(string key);
    }
}

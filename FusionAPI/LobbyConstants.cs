using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FusionAPI
{
    public static class LobbyConstants
    {
        private const string _internalPrefix = "BONELAB_FUSION_";
        public const string HasServerOpenKey = _internalPrefix + "HasServerOpen";

        public const string KeyCollectionKey = _internalPrefix + "KeyCollection";
    }
}
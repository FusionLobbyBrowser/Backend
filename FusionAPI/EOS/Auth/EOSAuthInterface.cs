using Epic.OnlineServices;

namespace FusionAPI.EOS.Auth;

internal abstract class EOSAuthInterface
{
    internal abstract ExternalAccountType AccountType { get; }

    internal abstract ExternalCredentialType CredentialType { get; }

    /// <summary>
    /// Indicates whether this interface can return a null authentication token.
    /// </summary>
    internal virtual bool AllowNullToken => false;

    /// <summary>
    /// Indicates whether DisplayName should be passed into UserLoginInfo on login.
    /// </summary>
    internal virtual bool LoginWithDisplayName => false;

    internal abstract Task<string?> GetLoginTicketAsync();

    internal abstract Task<string?> GetDisplayNameAsync();

    internal virtual void OnShutdown()
    {
    }
}
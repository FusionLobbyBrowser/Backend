namespace FusionAPI.Data.Enums
{
    public enum PermissionLevel : sbyte
    {
        /// <summary>
        /// Someone with less permissions than the normal user.
        /// </summary>
        GUEST = -1,

        /// <summary>
        /// The default permission level for a user.
        /// </summary>
        DEFAULT = 0,

        /// <summary>
        /// Permissions of a moderator or operator on the server.
        /// </summary>
        OPERATOR = 1,

        /// <summary>
        /// Permissions of an owner on the server.
        /// </summary>
        OWNER = 2,
    }
}
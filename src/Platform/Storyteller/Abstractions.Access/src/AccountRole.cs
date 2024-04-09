using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum AccountRole
    {
        /// <summary>
        /// None role, no access.
        /// </summary>
        None = 0,

        /// <summary>
        /// Role of a reader, only able to read annotations.
        /// </summary>
        Reader = 1,

        /// <summary>
        /// Role of a contributor, able to read, create, modify and remove annotations.
        /// </summary>
        Contributor = 2,

        /// <summary>
        /// Role of an administrator, able to read, create, modify and remove annotations, as well as manage accessibility.
        /// </summary>
        Administrator = 3,

        /// <summary>
        /// Role of an owner, full control, can revoke ownership.
        /// </summary>
        Owner = 4,
    }
}

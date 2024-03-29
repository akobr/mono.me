/*
 * 2S-API
 *
 * The 2S-API is a RESTful API for interacting with the 2S Platform.
 *
 * The version of the OpenAPI document: 0.8.21.54913
 * Contact: kobr.ales@outlook.com
 * Generated by: https://github.com/openapitools/openapi-generator.git
 */


using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;
using OpenAPIDateConverter = _42.Platform.Sdk.Client.OpenAPIDateConverter;

namespace _42.Platform.Sdk.Model
{
    /// <summary>
    /// Account
    /// </summary>
    [DataContract(Name = "account")]
    public partial class Account : IValidatableObject
    {
        /// <summary>
        /// Defines Inner
        /// </summary>
        public enum InnerEnum
        {
            /// <summary>
            /// Enum None for value: 0
            /// </summary>
            None = 0,

            /// <summary>
            /// Enum Reader for value: 1
            /// </summary>
            Reader = 1,

            /// <summary>
            /// Enum Contributor for value: 2
            /// </summary>
            Contributor = 2,

            /// <summary>
            /// Enum Administrator for value: 3
            /// </summary>
            Administrator = 3,

            /// <summary>
            /// Enum Owner for value: 4
            /// </summary>
            Owner = 4
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Account" /> class.
        /// </summary>
        /// <param name="partitionKey">partitionKey.</param>
        /// <param name="id">id.</param>
        /// <param name="key">key.</param>
        /// <param name="name">name.</param>
        /// <param name="accessMap">accessMap.</param>
        public Account(string partitionKey = default(string), string id = default(string), string key = default(string), string name = default(string), Dictionary<string, InnerEnum> accessMap = default(Dictionary<string, InnerEnum>))
        {
            this.PartitionKey = partitionKey;
            this.Id = id;
            this.Key = key;
            this.Name = name;
            this.AccessMap = accessMap;
        }

        /// <summary>
        /// Gets or Sets PartitionKey
        /// </summary>
        [DataMember(Name = "partitionKey", EmitDefaultValue = false)]
        public string PartitionKey { get; set; }

        /// <summary>
        /// Gets or Sets Id
        /// </summary>
        [DataMember(Name = "id", EmitDefaultValue = false)]
        public string Id { get; set; }

        /// <summary>
        /// Gets or Sets Key
        /// </summary>
        [DataMember(Name = "key", EmitDefaultValue = false)]
        public string Key { get; set; }

        /// <summary>
        /// Gets or Sets Name
        /// </summary>
        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string Name { get; set; }

        /// <summary>
        /// Gets or Sets AccessMap
        /// </summary>
        [DataMember(Name = "accessMap", EmitDefaultValue = false)]
        public Dictionary<string, Account.InnerEnum> AccessMap { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class Account {\n");
            sb.Append("  PartitionKey: ").Append(PartitionKey).Append("\n");
            sb.Append("  Id: ").Append(Id).Append("\n");
            sb.Append("  Key: ").Append(Key).Append("\n");
            sb.Append("  Name: ").Append(Name).Append("\n");
            sb.Append("  AccessMap: ").Append(AccessMap).Append("\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>
        /// Returns the JSON string presentation of the object
        /// </summary>
        /// <returns>JSON string presentation of the object</returns>
        public virtual string ToJson()
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented);
        }

        /// <summary>
        /// To validate all properties of the instance
        /// </summary>
        /// <param name="validationContext">Validation context</param>
        /// <returns>Validation Result</returns>
        IEnumerable<System.ComponentModel.DataAnnotations.ValidationResult> IValidatableObject.Validate(ValidationContext validationContext)
        {
            yield break;
        }
    }

}

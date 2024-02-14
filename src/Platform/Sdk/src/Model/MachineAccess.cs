/*
 * 2S-API
 *
 * The 2S-API is a RESTful API for interacting with the 2S Platform.
 *
 * The version of the OpenAPI document: 0.8.14.63984
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
    /// MachineAccess
    /// </summary>
    [DataContract(Name = "machineAccess")]
    public partial class MachineAccess : IValidatableObject
    {
        /// <summary>
        /// Defines Scope
        /// </summary>
        public enum ScopeEnum
        {
            /// <summary>
            /// Enum DefaultRead for value: 0
            /// </summary>
            DefaultRead = 0,

            /// <summary>
            /// Enum AnnotationRead for value: 1
            /// </summary>
            AnnotationRead = 1,

            /// <summary>
            /// Enum ConfigurationRead for value: 2
            /// </summary>
            ConfigurationRead = 2,

            /// <summary>
            /// Enum DefaultReadWrite for value: 3
            /// </summary>
            DefaultReadWrite = 3,

            /// <summary>
            /// Enum AnnotationReadWrite for value: 4
            /// </summary>
            AnnotationReadWrite = 4,

            /// <summary>
            /// Enum ConfigurationReadWrite for value: 5
            /// </summary>
            ConfigurationReadWrite = 5
        }


        /// <summary>
        /// Gets or Sets Scope
        /// </summary>
        [DataMember(Name = "scope", EmitDefaultValue = false)]
        public ScopeEnum? Scope { get; set; }
        /// <summary>
        /// Initializes a new instance of the <see cref="MachineAccess" /> class.
        /// </summary>
        /// <param name="partitionKey">partitionKey.</param>
        /// <param name="id">id.</param>
        /// <param name="accessKey">accessKey.</param>
        /// <param name="scope">scope (default to ScopeEnum.NUMBER_0).</param>
        /// <param name="annotationKey">annotationKey.</param>
        public MachineAccess(string partitionKey = default(string), string id = default(string), string accessKey = default(string), ScopeEnum? scope = ScopeEnum.DefaultRead, string annotationKey = default(string))
        {
            this.PartitionKey = partitionKey;
            this.Id = id;
            this.AccessKey = accessKey;
            this.Scope = scope;
            this.AnnotationKey = annotationKey;
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
        /// Gets or Sets AccessKey
        /// </summary>
        [DataMember(Name = "accessKey", EmitDefaultValue = false)]
        public string AccessKey { get; set; }

        /// <summary>
        /// Gets or Sets AnnotationKey
        /// </summary>
        [DataMember(Name = "annotationKey", EmitDefaultValue = false)]
        public string AnnotationKey { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class MachineAccess {\n");
            sb.Append("  PartitionKey: ").Append(PartitionKey).Append("\n");
            sb.Append("  Id: ").Append(Id).Append("\n");
            sb.Append("  AccessKey: ").Append(AccessKey).Append("\n");
            sb.Append("  Scope: ").Append(Scope).Append("\n");
            sb.Append("  AnnotationKey: ").Append(AnnotationKey).Append("\n");
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

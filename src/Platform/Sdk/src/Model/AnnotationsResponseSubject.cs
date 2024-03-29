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
    /// AnnotationsResponseSubject
    /// </summary>
    [DataContract(Name = "annotationsResponse_subject")]
    public partial class AnnotationsResponseSubject : IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AnnotationsResponseSubject" /> class.
        /// </summary>
        /// <param name="annotations">annotations.</param>
        /// <param name="continuationToken">continuationToken.</param>
        /// <param name="count">count.</param>
        public AnnotationsResponseSubject(List<Subject> annotations = default(List<Subject>), string continuationToken = default(string), int count = default(int))
        {
            this.Annotations = annotations;
            this.ContinuationToken = continuationToken;
            this.Count = count;
        }

        /// <summary>
        /// Gets or Sets Annotations
        /// </summary>
        [DataMember(Name = "annotations", EmitDefaultValue = false)]
        public List<Subject> Annotations { get; set; }

        /// <summary>
        /// Gets or Sets ContinuationToken
        /// </summary>
        [DataMember(Name = "continuationToken", EmitDefaultValue = false)]
        public string ContinuationToken { get; set; }

        /// <summary>
        /// Gets or Sets Count
        /// </summary>
        [DataMember(Name = "count", EmitDefaultValue = false)]
        public int Count { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class AnnotationsResponseSubject {\n");
            sb.Append("  Annotations: ").Append(Annotations).Append("\n");
            sb.Append("  ContinuationToken: ").Append(ContinuationToken).Append("\n");
            sb.Append("  Count: ").Append(Count).Append("\n");
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

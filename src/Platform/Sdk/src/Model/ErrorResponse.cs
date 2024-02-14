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
    /// ErrorResponse
    /// </summary>
    [DataContract(Name = "errorResponse")]
    public partial class ErrorResponse : IValidatableObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorResponse" /> class.
        /// </summary>
        /// <param name="message">message.</param>
        /// <param name="hint">hint.</param>
        /// <param name="errorCode">errorCode.</param>
        /// <param name="error">error.</param>
        public ErrorResponse(string message = default(string), string hint = default(string), string errorCode = default(string), Object error = default(Object))
        {
            this.Message = message;
            this.Hint = hint;
            this.ErrorCode = errorCode;
            this.Error = error;
        }

        /// <summary>
        /// Gets or Sets Message
        /// </summary>
        [DataMember(Name = "message", EmitDefaultValue = false)]
        public string Message { get; set; }

        /// <summary>
        /// Gets or Sets Hint
        /// </summary>
        [DataMember(Name = "hint", EmitDefaultValue = false)]
        public string Hint { get; set; }

        /// <summary>
        /// Gets or Sets ErrorCode
        /// </summary>
        [DataMember(Name = "errorCode", EmitDefaultValue = false)]
        public string ErrorCode { get; set; }

        /// <summary>
        /// Gets or Sets Error
        /// </summary>
        [DataMember(Name = "error", EmitDefaultValue = false)]
        public Object Error { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ErrorResponse {\n");
            sb.Append("  Message: ").Append(Message).Append("\n");
            sb.Append("  Hint: ").Append(Hint).Append("\n");
            sb.Append("  ErrorCode: ").Append(ErrorCode).Append("\n");
            sb.Append("  Error: ").Append(Error).Append("\n");
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
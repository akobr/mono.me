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
    /// Execution
    /// </summary>
    [DataContract(Name = "execution")]
    public partial class Execution : IValidatableObject
    {
        /// <summary>
        /// Defines AnnotationType
        /// </summary>
        public enum AnnotationTypeEnum
        {
            /// <summary>
            /// Enum Responsibility for value: 0
            /// </summary>
            Responsibility = 0,

            /// <summary>
            /// Enum Job for value: 1
            /// </summary>
            Job = 1,

            /// <summary>
            /// Enum Subject for value: 2
            /// </summary>
            Subject = 2,

            /// <summary>
            /// Enum Usage for value: 3
            /// </summary>
            Usage = 3,

            /// <summary>
            /// Enum Context for value: 4
            /// </summary>
            Context = 4,

            /// <summary>
            /// Enum Execution for value: 5
            /// </summary>
            Execution = 5
        }


        /// <summary>
        /// Gets or Sets AnnotationType
        /// </summary>
        [DataMember(Name = "annotationType", EmitDefaultValue = false)]
        public AnnotationTypeEnum? AnnotationType { get; set; }
        /// <summary>
        /// Initializes a new instance of the <see cref="Execution" /> class.
        /// </summary>
        /// <param name="responsibilityKey">responsibilityKey.</param>
        /// <param name="subjectKey">subjectKey.</param>
        /// <param name="contextKey">contextKey.</param>
        /// <param name="subjectName">subjectName.</param>
        /// <param name="responsibilityName">responsibilityName.</param>
        /// <param name="contextName">contextName.</param>
        /// <param name="annotationType">annotationType (default to AnnotationTypeEnum.Execution).</param>
        /// <param name="title">title.</param>
        /// <param name="description">description.</param>
        /// <param name="documentationLink">documentationLink.</param>
        /// <param name="isDisabled">isDisabled.</param>
        /// <param name="validFrom">validFrom.</param>
        /// <param name="expiresAt">expiresAt.</param>
        /// <param name="varTimeZone">varTimeZone.</param>
        /// <param name="labels">labels.</param>
        /// <param name="values">values.</param>
        /// <param name="partitionKey">partitionKey.</param>
        /// <param name="id">id.</param>
        /// <param name="projectName">projectName.</param>
        /// <param name="viewName">viewName.</param>
        /// <param name="annotationKey">annotationKey.</param>
        /// <param name="name">name.</param>
        public Execution(string responsibilityKey = default(string), string subjectKey = default(string), string contextKey = default(string), string subjectName = default(string), string responsibilityName = default(string), string contextName = default(string), AnnotationTypeEnum? annotationType = AnnotationTypeEnum.Execution, string title = default(string), string description = default(string), string documentationLink = default(string), bool? isDisabled = default(bool?), DateTime? validFrom = default(DateTime?), DateTime? expiresAt = default(DateTime?), string varTimeZone = default(string), List<string> labels = default(List<string>), Dictionary<string, Object> values = default(Dictionary<string, Object>), string partitionKey = default(string), string id = default(string), string projectName = default(string), string viewName = default(string), string annotationKey = default(string), string name = default(string))
        {
            this.ResponsibilityKey = responsibilityKey;
            this.SubjectKey = subjectKey;
            this.ContextKey = contextKey;
            this.SubjectName = subjectName;
            this.ResponsibilityName = responsibilityName;
            this.ContextName = contextName;
            this.AnnotationType = annotationType;
            this.Title = title;
            this.Description = description;
            this.DocumentationLink = documentationLink;
            this.IsDisabled = isDisabled;
            this.ValidFrom = validFrom;
            this.ExpiresAt = expiresAt;
            this.VarTimeZone = varTimeZone;
            this.Labels = labels;
            this.Values = values;
            this.PartitionKey = partitionKey;
            this.Id = id;
            this.ProjectName = projectName;
            this.ViewName = viewName;
            this.AnnotationKey = annotationKey;
            this.Name = name;
        }

        /// <summary>
        /// Gets or Sets ResponsibilityKey
        /// </summary>
        [DataMember(Name = "responsibilityKey", EmitDefaultValue = false)]
        public string ResponsibilityKey { get; set; }

        /// <summary>
        /// Gets or Sets SubjectKey
        /// </summary>
        [DataMember(Name = "subjectKey", EmitDefaultValue = false)]
        public string SubjectKey { get; set; }

        /// <summary>
        /// Gets or Sets ContextKey
        /// </summary>
        [DataMember(Name = "contextKey", EmitDefaultValue = false)]
        public string ContextKey { get; set; }

        /// <summary>
        /// Gets or Sets SubjectName
        /// </summary>
        [DataMember(Name = "subjectName", EmitDefaultValue = false)]
        public string SubjectName { get; set; }

        /// <summary>
        /// Gets or Sets ResponsibilityName
        /// </summary>
        [DataMember(Name = "responsibilityName", EmitDefaultValue = false)]
        public string ResponsibilityName { get; set; }

        /// <summary>
        /// Gets or Sets ContextName
        /// </summary>
        [DataMember(Name = "contextName", EmitDefaultValue = false)]
        public string ContextName { get; set; }

        /// <summary>
        /// Gets or Sets Title
        /// </summary>
        [DataMember(Name = "title", EmitDefaultValue = false)]
        public string Title { get; set; }

        /// <summary>
        /// Gets or Sets Description
        /// </summary>
        [DataMember(Name = "description", EmitDefaultValue = false)]
        public string Description { get; set; }

        /// <summary>
        /// Gets or Sets DocumentationLink
        /// </summary>
        [DataMember(Name = "documentationLink", EmitDefaultValue = false)]
        public string DocumentationLink { get; set; }

        /// <summary>
        /// Gets or Sets IsDisabled
        /// </summary>
        [DataMember(Name = "isDisabled", EmitDefaultValue = true)]
        public bool? IsDisabled { get; set; }

        /// <summary>
        /// Gets or Sets ValidFrom
        /// </summary>
        [DataMember(Name = "validFrom", EmitDefaultValue = true)]
        public DateTime? ValidFrom { get; set; }

        /// <summary>
        /// Gets or Sets ExpiresAt
        /// </summary>
        [DataMember(Name = "expiresAt", EmitDefaultValue = true)]
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// Gets or Sets VarTimeZone
        /// </summary>
        [DataMember(Name = "timeZone", EmitDefaultValue = false)]
        public string VarTimeZone { get; set; }

        /// <summary>
        /// Gets or Sets Labels
        /// </summary>
        [DataMember(Name = "labels", EmitDefaultValue = false)]
        public List<string> Labels { get; set; }

        /// <summary>
        /// Gets or Sets Values
        /// </summary>
        [DataMember(Name = "values", EmitDefaultValue = false)]
        public Dictionary<string, Object> Values { get; set; }

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
        /// Gets or Sets ProjectName
        /// </summary>
        [DataMember(Name = "projectName", EmitDefaultValue = false)]
        public string ProjectName { get; set; }

        /// <summary>
        /// Gets or Sets ViewName
        /// </summary>
        [DataMember(Name = "viewName", EmitDefaultValue = false)]
        public string ViewName { get; set; }

        /// <summary>
        /// Gets or Sets AnnotationKey
        /// </summary>
        [DataMember(Name = "annotationKey", EmitDefaultValue = false)]
        public string AnnotationKey { get; set; }

        /// <summary>
        /// Gets or Sets Name
        /// </summary>
        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string Name { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class Execution {\n");
            sb.Append("  ResponsibilityKey: ").Append(ResponsibilityKey).Append("\n");
            sb.Append("  SubjectKey: ").Append(SubjectKey).Append("\n");
            sb.Append("  ContextKey: ").Append(ContextKey).Append("\n");
            sb.Append("  SubjectName: ").Append(SubjectName).Append("\n");
            sb.Append("  ResponsibilityName: ").Append(ResponsibilityName).Append("\n");
            sb.Append("  ContextName: ").Append(ContextName).Append("\n");
            sb.Append("  AnnotationType: ").Append(AnnotationType).Append("\n");
            sb.Append("  Title: ").Append(Title).Append("\n");
            sb.Append("  Description: ").Append(Description).Append("\n");
            sb.Append("  DocumentationLink: ").Append(DocumentationLink).Append("\n");
            sb.Append("  IsDisabled: ").Append(IsDisabled).Append("\n");
            sb.Append("  ValidFrom: ").Append(ValidFrom).Append("\n");
            sb.Append("  ExpiresAt: ").Append(ExpiresAt).Append("\n");
            sb.Append("  VarTimeZone: ").Append(VarTimeZone).Append("\n");
            sb.Append("  Labels: ").Append(Labels).Append("\n");
            sb.Append("  Values: ").Append(Values).Append("\n");
            sb.Append("  PartitionKey: ").Append(PartitionKey).Append("\n");
            sb.Append("  Id: ").Append(Id).Append("\n");
            sb.Append("  ProjectName: ").Append(ProjectName).Append("\n");
            sb.Append("  ViewName: ").Append(ViewName).Append("\n");
            sb.Append("  AnnotationKey: ").Append(AnnotationKey).Append("\n");
            sb.Append("  Name: ").Append(Name).Append("\n");
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

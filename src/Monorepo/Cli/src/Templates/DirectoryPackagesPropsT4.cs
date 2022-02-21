// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version: 17.0.0.0
//  
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------
namespace _42.Monorepo.Cli.Templates
{
    using System;
    
    /// <summary>
    /// Class to produce the template output
    /// </summary>
    
    #line 1 "C:\working\mono.me\src\Monorepo\Cli\src\Templates\DirectoryPackagesPropsT4.tt"
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.TextTemplating", "17.0.0.0")]
    public partial class DirectoryPackagesPropsT4 : DirectoryPackagesPropsT4Base
    {
#line hidden
        /// <summary>
        /// Create the template output
        /// </summary>
        public virtual string TransformText()
        {
            this.Write(@"<?xml version=""1.0"" encoding=""utf-8""?>
<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <ItemGroup>

    <!-- Please create any group of dependencies if you need -->
    <!-- <PackageVersion Include=""package-name"" Version=""package-version"" /> -->

    <!-- abstractions for .NET libraries -->
    <PackageVersion Include=""Microsoft.Extensions.Configuration.Abstractions"" Version=""6.0.0"" />
    <PackageVersion Include=""Microsoft.Extensions.DependencyInjection.Abstractions"" Version=""6.0.0"" />
    <PackageVersion Include=""Microsoft.Extensions.Hosting.Abstractions"" Version=""6.0.0"" />
    <PackageVersion Include=""Microsoft.Extensions.Logging.Abstractions"" Version=""6.0.0"" />

    <!-- .NET libraries -->
    <PackageVersion Include=""Microsoft.Extensions.Configuration"" Version=""6.0.0"" />
    <PackageVersion Include=""Microsoft.Extensions.DependencyInjection"" Version=""6.0"" />
    <PackageVersion Include=""Microsoft.Extensions.Hosting"" Version=""6.0.0"" />
    <PackageVersion Include=""Microsoft.Extensions.Logging"" Version=""6.0.0"" />
      
    <!-- unit testing -->
    <PackageVersion Include=""coverlet.collector"" Version=""3.1.2"" />
    <PackageVersion Include=""FluentAssertions"" Version=""6.5.1"" />
    <PackageVersion Include=""Microsoft.NET.Test.Sdk"" Version=""17.1.0"" />
    <PackageVersion Include=""Moq"" Version=""4.16.1"" />
");
            
            #line 26 "C:\working\mono.me\src\Monorepo\Cli\src\Templates\DirectoryPackagesPropsT4.tt"
 if (_featureProvider.IsEnabled("tests-xunit")) { 
            
            #line default
            #line hidden
            this.Write("    <PackageVersion Include=\"xunit\" Version=\"2.4.1\" />\r\n    <PackageVersion Inclu" +
                    "de=\"xunit.runner.visualstudio\" Version=\"2.4.3\" />\r\n");
            
            #line 29 "C:\working\mono.me\src\Monorepo\Cli\src\Templates\DirectoryPackagesPropsT4.tt"
 } 
            
            #line default
            #line hidden
            
            #line 29 "C:\working\mono.me\src\Monorepo\Cli\src\Templates\DirectoryPackagesPropsT4.tt"
 if (_featureProvider.IsEnabled("tests-nunit")) { 
            
            #line default
            #line hidden
            this.Write("    <PackageVersion Include=\"NUnit\" Version=\"3.13.2\" />\r\n    <PackageVersion Incl" +
                    "ude=\"NUnit3TestAdapter\" Version=\"4.2.1\" />\r\n");
            
            #line 32 "C:\working\mono.me\src\Monorepo\Cli\src\Templates\DirectoryPackagesPropsT4.tt"
 } 
            
            #line default
            #line hidden
            
            #line 32 "C:\working\mono.me\src\Monorepo\Cli\src\Templates\DirectoryPackagesPropsT4.tt"
 if (_featureProvider.IsEnabled("git-version")) { 
            
            #line default
            #line hidden
            this.Write("\r\n    <!-- versioning -->\r\n    <PackageVersion Include=\"42.Monorepo.GitVersioning" +
                    "\" Version=\"3.5.82-alpha-gc03e12373f\" />\r\n");
            
            #line 36 "C:\working\mono.me\src\Monorepo\Cli\src\Templates\DirectoryPackagesPropsT4.tt"
 } 
            
            #line default
            #line hidden
            
            #line 36 "C:\working\mono.me\src\Monorepo\Cli\src\Templates\DirectoryPackagesPropsT4.tt"
 if (_featureProvider.IsEnabled("stylecop")) { 
            
            #line default
            #line hidden
            this.Write("\r\n    <!-- code analysis -->\r\n    <PackageVersion Include=\"StyleCop.Analyzers\" Ve" +
                    "rsion=\"1.2.0-beta.406\" />\r\n");
            
            #line 40 "C:\working\mono.me\src\Monorepo\Cli\src\Templates\DirectoryPackagesPropsT4.tt"
 } 
            
            #line default
            #line hidden
            this.Write("\r\n  </ItemGroup>\r\n</Project>\r\n");
            return this.GenerationEnvironment.ToString();
        }
    }
    
    #line default
    #line hidden
    #region Base class
    /// <summary>
    /// Base class for this transformation
    /// </summary>
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.TextTemplating", "17.0.0.0")]
    public class DirectoryPackagesPropsT4Base
    {
        #region Fields
        private global::System.Text.StringBuilder generationEnvironmentField;
        private global::System.CodeDom.Compiler.CompilerErrorCollection errorsField;
        private global::System.Collections.Generic.List<int> indentLengthsField;
        private string currentIndentField = "";
        private bool endsWithNewline;
        private global::System.Collections.Generic.IDictionary<string, object> sessionField;
        #endregion
        #region Properties
        /// <summary>
        /// The string builder that generation-time code is using to assemble generated output
        /// </summary>
        protected System.Text.StringBuilder GenerationEnvironment
        {
            get
            {
                if ((this.generationEnvironmentField == null))
                {
                    this.generationEnvironmentField = new global::System.Text.StringBuilder();
                }
                return this.generationEnvironmentField;
            }
            set
            {
                this.generationEnvironmentField = value;
            }
        }
        /// <summary>
        /// The error collection for the generation process
        /// </summary>
        public System.CodeDom.Compiler.CompilerErrorCollection Errors
        {
            get
            {
                if ((this.errorsField == null))
                {
                    this.errorsField = new global::System.CodeDom.Compiler.CompilerErrorCollection();
                }
                return this.errorsField;
            }
        }
        /// <summary>
        /// A list of the lengths of each indent that was added with PushIndent
        /// </summary>
        private System.Collections.Generic.List<int> indentLengths
        {
            get
            {
                if ((this.indentLengthsField == null))
                {
                    this.indentLengthsField = new global::System.Collections.Generic.List<int>();
                }
                return this.indentLengthsField;
            }
        }
        /// <summary>
        /// Gets the current indent we use when adding lines to the output
        /// </summary>
        public string CurrentIndent
        {
            get
            {
                return this.currentIndentField;
            }
        }
        /// <summary>
        /// Current transformation session
        /// </summary>
        public virtual global::System.Collections.Generic.IDictionary<string, object> Session
        {
            get
            {
                return this.sessionField;
            }
            set
            {
                this.sessionField = value;
            }
        }
        #endregion
        #region Transform-time helpers
        /// <summary>
        /// Write text directly into the generated output
        /// </summary>
        public void Write(string textToAppend)
        {
            if (string.IsNullOrEmpty(textToAppend))
            {
                return;
            }
            // If we're starting off, or if the previous text ended with a newline,
            // we have to append the current indent first.
            if (((this.GenerationEnvironment.Length == 0) 
                        || this.endsWithNewline))
            {
                this.GenerationEnvironment.Append(this.currentIndentField);
                this.endsWithNewline = false;
            }
            // Check if the current text ends with a newline
            if (textToAppend.EndsWith(global::System.Environment.NewLine, global::System.StringComparison.CurrentCulture))
            {
                this.endsWithNewline = true;
            }
            // This is an optimization. If the current indent is "", then we don't have to do any
            // of the more complex stuff further down.
            if ((this.currentIndentField.Length == 0))
            {
                this.GenerationEnvironment.Append(textToAppend);
                return;
            }
            // Everywhere there is a newline in the text, add an indent after it
            textToAppend = textToAppend.Replace(global::System.Environment.NewLine, (global::System.Environment.NewLine + this.currentIndentField));
            // If the text ends with a newline, then we should strip off the indent added at the very end
            // because the appropriate indent will be added when the next time Write() is called
            if (this.endsWithNewline)
            {
                this.GenerationEnvironment.Append(textToAppend, 0, (textToAppend.Length - this.currentIndentField.Length));
            }
            else
            {
                this.GenerationEnvironment.Append(textToAppend);
            }
        }
        /// <summary>
        /// Write text directly into the generated output
        /// </summary>
        public void WriteLine(string textToAppend)
        {
            this.Write(textToAppend);
            this.GenerationEnvironment.AppendLine();
            this.endsWithNewline = true;
        }
        /// <summary>
        /// Write formatted text directly into the generated output
        /// </summary>
        public void Write(string format, params object[] args)
        {
            this.Write(string.Format(global::System.Globalization.CultureInfo.CurrentCulture, format, args));
        }
        /// <summary>
        /// Write formatted text directly into the generated output
        /// </summary>
        public void WriteLine(string format, params object[] args)
        {
            this.WriteLine(string.Format(global::System.Globalization.CultureInfo.CurrentCulture, format, args));
        }
        /// <summary>
        /// Raise an error
        /// </summary>
        public void Error(string message)
        {
            System.CodeDom.Compiler.CompilerError error = new global::System.CodeDom.Compiler.CompilerError();
            error.ErrorText = message;
            this.Errors.Add(error);
        }
        /// <summary>
        /// Raise a warning
        /// </summary>
        public void Warning(string message)
        {
            System.CodeDom.Compiler.CompilerError error = new global::System.CodeDom.Compiler.CompilerError();
            error.ErrorText = message;
            error.IsWarning = true;
            this.Errors.Add(error);
        }
        /// <summary>
        /// Increase the indent
        /// </summary>
        public void PushIndent(string indent)
        {
            if ((indent == null))
            {
                throw new global::System.ArgumentNullException("indent");
            }
            this.currentIndentField = (this.currentIndentField + indent);
            this.indentLengths.Add(indent.Length);
        }
        /// <summary>
        /// Remove the last indent that was added with PushIndent
        /// </summary>
        public string PopIndent()
        {
            string returnValue = "";
            if ((this.indentLengths.Count > 0))
            {
                int indentLength = this.indentLengths[(this.indentLengths.Count - 1)];
                this.indentLengths.RemoveAt((this.indentLengths.Count - 1));
                if ((indentLength > 0))
                {
                    returnValue = this.currentIndentField.Substring((this.currentIndentField.Length - indentLength));
                    this.currentIndentField = this.currentIndentField.Remove((this.currentIndentField.Length - indentLength));
                }
            }
            return returnValue;
        }
        /// <summary>
        /// Remove any indentation
        /// </summary>
        public void ClearIndent()
        {
            this.indentLengths.Clear();
            this.currentIndentField = "";
        }
        #endregion
        #region ToString Helpers
        /// <summary>
        /// Utility class to produce culture-oriented representation of an object as a string.
        /// </summary>
        public class ToStringInstanceHelper
        {
            private System.IFormatProvider formatProviderField  = global::System.Globalization.CultureInfo.InvariantCulture;
            /// <summary>
            /// Gets or sets format provider to be used by ToStringWithCulture method.
            /// </summary>
            public System.IFormatProvider FormatProvider
            {
                get
                {
                    return this.formatProviderField ;
                }
                set
                {
                    if ((value != null))
                    {
                        this.formatProviderField  = value;
                    }
                }
            }
            /// <summary>
            /// This is called from the compile/run appdomain to convert objects within an expression block to a string
            /// </summary>
            public string ToStringWithCulture(object objectToConvert)
            {
                if ((objectToConvert == null))
                {
                    throw new global::System.ArgumentNullException("objectToConvert");
                }
                System.Type t = objectToConvert.GetType();
                System.Reflection.MethodInfo method = t.GetMethod("ToString", new System.Type[] {
                            typeof(System.IFormatProvider)});
                if ((method == null))
                {
                    return objectToConvert.ToString();
                }
                else
                {
                    return ((string)(method.Invoke(objectToConvert, new object[] {
                                this.formatProviderField })));
                }
            }
        }
        private ToStringInstanceHelper toStringHelperField = new ToStringInstanceHelper();
        /// <summary>
        /// Helper to produce culture-oriented representation of an object as a string
        /// </summary>
        public ToStringInstanceHelper ToStringHelper
        {
            get
            {
                return this.toStringHelperField;
            }
        }
        #endregion
    }
    #endregion
}

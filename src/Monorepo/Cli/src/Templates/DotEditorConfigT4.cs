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
    
    #line 1 "C:\working\mono.me\src\Monorepo\Cli\src\Templates\DotEditorConfigT4.tt"
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.TextTemplating", "17.0.0.0")]
    public partial class DotEditorConfigT4 : DotEditorConfigT4Base
    {
#line hidden
        /// <summary>
        /// Create the template output
        /// </summary>
        public virtual string TransformText()
        {
            this.Write("# http://editorconfig.org\r\nroot = true\r\n\r\n\r\n[*]\r\nend_of_line = crlf\r\nindent_style" +
                    " = space\r\ncharset = utf-8\r\ntrim_trailing_whitespace = true\r\ninsert_final_newline" +
                    " = true\r\n\r\n\r\n[*.cs]\r\nindent_size = 4\r\n\r\n# puts system usings before others\r\ndotn" +
                    "et_sort_system_directives_first = true\r\n# don\'t use this. qualifier\r\ndotnet_styl" +
                    "e_qualification_for_field = false:suggestion\r\ndotnet_style_qualification_for_pro" +
                    "perty = false:suggestion\r\n# use int x = .. over Int32\r\ndotnet_style_predefined_t" +
                    "ype_for_locals_parameters_members = true:suggestion\r\n# use int.MaxValue over Int" +
                    "32.MaxValue\r\ndotnet_style_predefined_type_for_member_access = true:suggestion\r\n#" +
                    " require var all the time\r\ncsharp_style_var_for_built_in_types = true:suggestion" +
                    "\r\ncsharp_style_var_when_type_is_apparent = true:suggestion\r\ncsharp_style_var_els" +
                    "ewhere = true:suggestion\r\n# disallow throw expressions\r\ncsharp_style_throw_expre" +
                    "ssion = false:suggestion\r\n# newline settings \r\ncsharp_new_line_before_open_brace" +
                    " = all\r\ncsharp_new_line_before_else = true\r\ncsharp_new_line_before_catch = true\r" +
                    "\ncsharp_new_line_before_finally = true\r\ncsharp_new_line_before_members_in_object" +
                    "_initializers = true\r\ncsharp_new_line_before_members_in_anonymous_types = true\r\n" +
                    "\r\n\r\n[*.{xml,config,*proj,nuspec,props,resx,targets,yml,tasks}]\r\nindent_size = 2\r" +
                    "\n\r\n\r\n[*.{props,targets,ruleset,config,nuspec,resx,vsixmanifest,vsct}]\r\nindent_si" +
                    "ze = 2\r\n\r\n\r\n[*.json]\r\nindent_size = 2\r\n\r\n\r\n[*.{ps1,psm1}]\r\nindent_size = 4\r\n\r\n\r\n" +
                    "[*.sh]\r\nindent_size = 4\r\nend_of_line = lf\r\n\r\n\r\n[*.{razor,cshtml}]\r\ncharset = utf" +
                    "-8-bom\r\n\r\n\r\n[*.{cs,vb}]\r\n# CA1018: Mark attributes with AttributeUsageAttribute\r" +
                    "\ndotnet_diagnostic.CA1018.severity = warning\r\n# CA1047: Do not declare protected" +
                    " member in sealed type\r\ndotnet_diagnostic.CA1047.severity = warning\r\n# CA1305: S" +
                    "pecify IFormatProvider\r\ndotnet_diagnostic.CA1305.severity = suggestion\r\n# CA1507" +
                    ": Use nameof to express symbol names\r\ndotnet_diagnostic.CA1507.severity = warnin" +
                    "g\r\n# CA1725: Parameter names should match base declaration\r\ndotnet_diagnostic.CA" +
                    "1725.severity = suggestion\r\n# CA1802: Use literals where appropriate\r\ndotnet_dia" +
                    "gnostic.CA1802.severity = warning\r\n# CA1805: Do not initialize unnecessarily\r\ndo" +
                    "tnet_diagnostic.CA1805.severity = warning\r\n# CA1810: Do not initialize unnecessa" +
                    "rily\r\ndotnet_diagnostic.CA1810.severity = suggestion\r\n# CA1821: Remove empty Fin" +
                    "alizers\r\ndotnet_diagnostic.CA1821.severity = warning\r\n# CA1822: Make member stat" +
                    "ic\r\ndotnet_diagnostic.CA1822.severity = suggestion\r\n# CA1823: Avoid unused priva" +
                    "te fields\r\ndotnet_diagnostic.CA1823.severity = warning\r\n# CA1825: Avoid zero-len" +
                    "gth array allocations\r\ndotnet_diagnostic.CA1825.severity = warning\r\n# CA1826: Do" +
                    " not use Enumerable methods on indexable collections. Instead use the collection" +
                    " directly\r\ndotnet_diagnostic.CA1826.severity = warning\r\n# CA1827: Do not use Cou" +
                    "nt() or LongCount() when Any() can be used\r\ndotnet_diagnostic.CA1827.severity = " +
                    "warning\r\n# CA1828: Do not use CountAsync() or LongCountAsync() when AnyAsync() c" +
                    "an be used\r\ndotnet_diagnostic.CA1828.severity = warning\r\n# CA1829: Use Length/Co" +
                    "unt property instead of Count() when available\r\ndotnet_diagnostic.CA1829.severit" +
                    "y = warning\r\n# CA1830: Prefer strongly-typed Append and Insert method overloads " +
                    "on StringBuilder\r\ndotnet_diagnostic.CA1830.severity = warning\r\n# CA1831: Use AsS" +
                    "pan or AsMemory instead of Range-based indexers when appropriate\r\ndotnet_diagnos" +
                    "tic.CA1831.severity = warning\r\n# CA1832: Use AsSpan or AsMemory instead of Range" +
                    "-based indexers when appropriate\r\ndotnet_diagnostic.CA1832.severity = warning\r\n#" +
                    " CA1833: Use AsSpan or AsMemory instead of Range-based indexers when appropriate" +
                    "\r\ndotnet_diagnostic.CA1833.severity = warning\r\n# CA1834: Consider using \'StringB" +
                    "uilder.Append(char)\' when applicable\r\ndotnet_diagnostic.CA1834.severity = warnin" +
                    "g\r\n# CA1835: Prefer the \'Memory\'-based overloads for \'ReadAsync\' and \'WriteAsync" +
                    "\'\r\ndotnet_diagnostic.CA1835.severity = warning\r\n# CA1836: Prefer IsEmpty over Co" +
                    "unt\r\ndotnet_diagnostic.CA1836.severity = warning\r\n# CA1837: Use \'Environment.Pro" +
                    "cessId\'\r\ndotnet_diagnostic.CA1837.severity = warning\r\n# CA1838: Avoid \'StringBui" +
                    "lder\' parameters for P/Invokes\r\ndotnet_diagnostic.CA1838.severity = warning\r\n# C" +
                    "A1839: Use \'Environment.ProcessPath\'\r\ndotnet_diagnostic.CA1839.severity = warnin" +
                    "g\r\n# CA1840: Use \'Environment.CurrentManagedThreadId\'\r\ndotnet_diagnostic.CA1840." +
                    "severity = warning\r\n# CA1841: Prefer Dictionary.Contains methods\r\ndotnet_diagnos" +
                    "tic.CA1841.severity = warning\r\n# CA1842: Do not use \'WhenAll\' with a single task" +
                    "\r\ndotnet_diagnostic.CA1842.severity = warning\r\n# CA1843: Do not use \'WaitAll\' wi" +
                    "th a single task\r\ndotnet_diagnostic.CA1843.severity = warning\r\n# CA1845: Use spa" +
                    "n-based \'string.Concat\'\r\ndotnet_diagnostic.CA1845.severity = warning\r\n# CA1846: " +
                    "Prefer AsSpan over Substring\r\ndotnet_diagnostic.CA1846.severity = warning\r\n# CA2" +
                    "008: Do not create tasks without passing a TaskScheduler\r\ndotnet_diagnostic.CA20" +
                    "08.severity = warning\r\n# CA2009: Do not call ToImmutableCollection on an Immutab" +
                    "leCollection value\r\ndotnet_diagnostic.CA2009.severity = warning\r\n# CA2011: Avoid" +
                    " infinite recursion\r\ndotnet_diagnostic.CA2011.severity = warning\r\n# CA2012: Use " +
                    "ValueTask correctly\r\ndotnet_diagnostic.CA2012.severity = warning\r\n# CA2013: Do n" +
                    "ot use ReferenceEquals with value types\r\ndotnet_diagnostic.CA2013.severity = war" +
                    "ning\r\n# CA2014: Do not use stackalloc in loops.\r\ndotnet_diagnostic.CA2014.severi" +
                    "ty = warning\r\n# CA2016: Forward the \'CancellationToken\' parameter to methods tha" +
                    "t take one\r\ndotnet_diagnostic.CA2016.severity = warning\r\n# CA2200: Rethrow to pr" +
                    "eserve stack details\r\ndotnet_diagnostic.CA2200.severity = warning\r\n# CA2208: Ins" +
                    "tantiate argument exceptions correctly\r\ndotnet_diagnostic.CA2208.severity = warn" +
                    "ing\r\n# IDE0035: Remove unreachable code\r\ndotnet_diagnostic.IDE0035.severity = wa" +
                    "rning\r\n# IDE0036: Order modifiers\r\ncsharp_preferred_modifier_order = public,priv" +
                    "ate,protected,internal,static,extern,new,virtual,abstract,sealed,override,readon" +
                    "ly,unsafe,volatile,async:suggestion\r\ndotnet_diagnostic.IDE0036.severity = warnin" +
                    "g\r\n# IDE0043: Format string contains invalid placeholder\r\ndotnet_diagnostic.IDE0" +
                    "043.severity = warning\r\n# IDE0044: Make field readonly\r\ndotnet_diagnostic.IDE004" +
                    "4.severity = warning\r\n\r\n\r\n[**/{test,samples,perf}/**.{cs,vb}]\r\n# CA1018: Mark at" +
                    "tributes with AttributeUsageAttribute\r\ndotnet_diagnostic.CA1018.severity = sugge" +
                    "stion\r\n# CA1507: Use nameof to express symbol names\r\ndotnet_diagnostic.CA1507.se" +
                    "verity = suggestion\r\n# CA1802: Use literals where appropriate\r\ndotnet_diagnostic" +
                    ".CA1802.severity = suggestion\r\n# CA1805: Do not initialize unnecessarily\r\ndotnet" +
                    "_diagnostic.CA1805.severity = suggestion\r\n# CA1823: Avoid zero-length array allo" +
                    "cations\r\ndotnet_diagnostic.CA1825.severity = suggestion\r\n# CA1826: Do not use En" +
                    "umerable methods on indexable collections. Instead use the collection directly\r\n" +
                    "dotnet_diagnostic.CA1826.severity = suggestion\r\n# CA1827: Do not use Count() or " +
                    "LongCount() when Any() can be used\r\ndotnet_diagnostic.CA1827.severity = suggesti" +
                    "on\r\n# CA1829: Use Length/Count property instead of Count() when available\r\ndotne" +
                    "t_diagnostic.CA1829.severity = suggestion\r\n# CA1834: Consider using \'StringBuild" +
                    "er.Append(char)\' when applicable\r\ndotnet_diagnostic.CA1834.severity = suggestion" +
                    "\r\n# CA1835: Prefer the \'Memory\'-based overloads for \'ReadAsync\' and \'WriteAsync\'" +
                    "\r\ndotnet_diagnostic.CA1835.severity = suggestion\r\n# CA1837: Use \'Environment.Pro" +
                    "cessId\'\r\ndotnet_diagnostic.CA1837.severity = suggestion\r\n# CA1838: Avoid \'String" +
                    "Builder\' parameters for P/Invokes\r\ndotnet_diagnostic.CA1838.severity = suggestio" +
                    "n\r\n# CA1841: Prefer Dictionary.Contains methods\r\ndotnet_diagnostic.CA1841.severi" +
                    "ty = suggestion\r\n# CA1844: Provide memory-based overrides of async methods when " +
                    "subclassing \'Stream\'\r\ndotnet_diagnostic.CA1844.severity = suggestion\r\n# CA1845: " +
                    "Use span-based \'string.Concat\'\r\ndotnet_diagnostic.CA1845.severity = suggestion\r\n" +
                    "# CA1846: Prefer AsSpan over Substring\r\ndotnet_diagnostic.CA1846.severity = sugg" +
                    "estion\r\n# CA2008: Do not create tasks without passing a TaskScheduler\r\ndotnet_di" +
                    "agnostic.CA2008.severity = suggestion\r\n# CA2012: Use ValueTask correctly\r\ndotnet" +
                    "_diagnostic.CA2012.severity = suggestion\r\n# IDE0044: Make field readonly\r\ndotnet" +
                    "_diagnostic.IDE0044.severity = suggestion\r\n# CA2016: Forward the \'CancellationTo" +
                    "ken\' parameter to methods that take one\r\ndotnet_diagnostic.CA2016.severity = sug" +
                    "gestion\r\n");
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
    public class DotEditorConfigT4Base
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

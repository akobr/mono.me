using System;

namespace _42.Roslyn.Compose.Attributes
{
    internal abstract class ArgumentValidationAttribute : Attribute
    {
        public abstract void Validate(object value, string argumentName);
    }
}

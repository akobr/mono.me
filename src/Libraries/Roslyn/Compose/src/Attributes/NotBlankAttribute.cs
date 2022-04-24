using System;

namespace _42.Roslyn.Compose.Attributes
{
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = true)]
    internal class NotBlankAttribute : ArgumentValidationAttribute
    {
        public override void Validate(object value, string argumentName)
        {
            var strValue = value as string;

            if (string.IsNullOrWhiteSpace(strValue))
            {
                throw new ArgumentException("String should not be null nor empty.", argumentName);
            }
        }
    }
}

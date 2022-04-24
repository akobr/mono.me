using System;

namespace _42.Roslyn.Compose.Attributes
{
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = true)]
    internal class NotNullAttribute : ArgumentValidationAttribute
    {
        public override void Validate(object value, string argumentName)
        {
            if(value == null)
            {
                throw new ArgumentNullException(argumentName);
            }
        }
    }
}

using System.Collections.Generic;

namespace _42.Roslyn.Compose.Model
{
    public class MethodOptions : AccessModifierOptions
    {
        public string MethodName { get; set; }

        public string ReturnType { get; set; }

        public List<RLParameter> Parameters { get; set; }
    }
}

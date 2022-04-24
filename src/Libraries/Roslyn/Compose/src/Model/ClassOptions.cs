namespace _42.Roslyn.Compose.Model
{
    public class ClassOptions : AccessModifierOptions
    {
        public string ClassName { get; set; }

        public bool IsPartial { get; set; }

        public bool IsStatic { get; set; }
    }
}

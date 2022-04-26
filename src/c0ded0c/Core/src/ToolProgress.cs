namespace c0ded0c.Core
{
    public class ToolProgress : IToolProgress
    {
        public ToolProgress()
            : this(string.Empty)
        {
            // no operation
        }

        public ToolProgress(string message)
        {
            Message = message;
        }

        public ToolProgress(string message, double percentages)
        {
            Message = message;
            Percentages = percentages;
        }

        public ToolProgress(string message, double percentages, string category)
        {
            Message = message;
            Percentages = percentages;
            Category = category;
        }

        public double? Percentages { get; set; }

        public string? Category { get; set; }

        public string Message { get; set; }
    }
}

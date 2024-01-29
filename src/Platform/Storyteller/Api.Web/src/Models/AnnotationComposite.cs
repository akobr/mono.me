namespace _42.Platform.Storyteller.Api.Models
{
    public class AnnotationComposite
    {
        public string Name { get; set; }

        public List<AnnotationComposite> Descendants { get; set; }
    }

}

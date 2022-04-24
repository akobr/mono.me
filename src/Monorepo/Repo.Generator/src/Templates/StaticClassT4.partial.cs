namespace _42.Monorepo.Repo.Generator.Templates;

public partial class StaticClassT4
{
    public StaticClassT4(StaticClassModel model)
    {
        Model = model;
    }

    public StaticClassModel Model { get; }
}

public class StaticClassModel
{
    public string Namespace { get; set; }

    public string ClassName { get; set; }
}

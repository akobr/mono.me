namespace _42.Platform.Storyteller.DbCreator.DataSets;

// A set of the catalog: its manifest, its folder (a full path) and where it comes from.
public sealed record class DataSet(DataSetManifest Manifest, string Folder, DataSetSource Source)
{
    public string Name => Manifest.Name;
}

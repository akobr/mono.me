namespace _42.Platform.Storyteller.DbCreator.DataSets;

// A file of a set: its path below the set's folder with '/' separators, and its full path.
public sealed record class DataSetFile(string Path, string FullPath);

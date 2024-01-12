namespace _42.Monorepo.Cli.Model.Records;

internal class SpecialRecord(string path, IRecord? parent) : Record(path, parent), ISpecialRecord
{
    // no operation

    public override RecordType Type => RecordType.Special;
}

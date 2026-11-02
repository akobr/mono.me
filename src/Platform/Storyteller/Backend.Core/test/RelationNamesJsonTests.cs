using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using _42.Platform.Storyteller.Annotating;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

// The relation arrays of the models are IReadOnlySet<string>, which System.Text.Json cannot create on its own: every
// body with one of them, even [], threw NotSupportedException (424-129).
public class RelationNamesJsonTests
{
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    // Every relation array: the key of an annotation that has it, and its member.
    public static TheoryData<string, string> RelationArrays => new()
    {
        { "rst.invoicing", nameof(Responsibility.UnitNames) },
        { "sbt.northwind", nameof(Subject.ContextNames) },
        { "sbt.northwind", nameof(Subject.ResponsibilityNames) },
        { "usg.northwind.invoicing", nameof(Usage.ContextNames) },
        { "cnt.northwind.prod", nameof(Context.ResponsibilityNames) },
        { "exe.northwind.invoicing.prod", nameof(Execution.UnitNames) },
    };

    // A value of the wrong type, in place of the array or of one of its names.
    public static TheoryData<string, string, string> WrongValues
    {
        get
        {
            var data = new TheoryData<string, string, string>();

            foreach (var row in RelationArrays)
            {
                var (key, member) = row.Data;

                foreach (var value in new[] { "\"prod\"", "5", "true", "{}", "[1]", "[null]", "[{}]", "[[\"prod\"]]", "[\"prod\",false]" })
                {
                    data.Add(key, member, value);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(RelationArrays))]
    public void Read_AnArray_GivesItsNames(string key, string member)
    {
        var annotation = Read(key, member, """["prod","staging"]""");

        Names(annotation, member).ShouldBe(["prod", "staging"], ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(RelationArrays))]
    public void Read_Duplicates_CollapseAndKeepTheirCase(string key, string member)
    {
        var names = Names(Read(key, member, """["prod","prod","Prod"]"""), member);

        names.Count.ShouldBe(2);
        names.ShouldContain("prod");
        names.ShouldContain("Prod");
    }

    [Theory]
    [MemberData(nameof(RelationArrays))]
    public void Read_Null_GivesAnEmptySet(string key, string member)
    {
        Names(Read(key, member, "null"), member).ShouldNotBeNull().ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(RelationArrays))]
    public void Read_AnEmptyOrAbsentArray_GivesAnEmptySet(string key, string member)
    {
        Names(Read(key, member, "[]"), member).ShouldBeEmpty();
        Names(Read(key, member, null), member).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(RelationArrays))]
    public void Read_KeepsReadingTheMembersAfterTheArray(string key, string member)
    {
        var body = Body(key, member, """["prod"]""");
        body.Remove(nameof(Annotation.Title));
        body[nameof(Annotation.Title)] = "after the array";

        var annotation = (Annotation)body.Deserialize(RuntimeType(key))!;

        Names(annotation, member).ShouldBe(["prod"]);
        annotation.Title.ShouldBe("after the array");
    }

    // A body names its members in any case, as the API reads them.
    [Theory]
    [MemberData(nameof(RelationArrays))]
    public void Read_AMemberInAnotherCase_UsesTheConverter(string key, string member)
    {
        var body = Body(key, member, null);
        body[member.ToLowerInvariant()] = JsonNode.Parse("""["prod"]""");

        var annotation = (Annotation)body.Deserialize(RuntimeType(key), CaseInsensitive)!;

        Names(annotation, member).ShouldBe(["prod"]);
    }

    [Theory]
    [MemberData(nameof(WrongValues))]
    public void Read_AValueOfTheWrongType_FailsWithItsPath(string key, string member, string value)
    {
        var json = Body(key, member, value).ToJsonString();

        var exception = Should.Throw<JsonException>(() => JsonSerializer.Deserialize(json, RuntimeType(key)));

        exception.Path.ShouldBe($"$.{member}");
        exception.Message.ShouldContain($"Path: $.{member}");
    }

    [Theory]
    [MemberData(nameof(RelationArrays))]
    public void Write_TheNames_AsAnArrayOfStrings(string key, string member)
    {
        var annotation = Read(key, member, """["prod","a \"quoted\" <name>"]""");

        var json = JsonSerializer.Serialize(annotation, annotation.GetType());

        var names = JsonNode.Parse(json)![member].ShouldBeOfType<JsonArray>();
        names.Select(name => name!.GetValue<string>()).ShouldBe(["prod", "a \"quoted\" <name>"], ignoreOrder: true);
        Names((Annotation)JsonSerializer.Deserialize(json, annotation.GetType())!, member).ShouldBe(Names(annotation, member), ignoreOrder: true);
    }

    // The bytes are those of the serializer's own arrays.
    [Theory]
    [MemberData(nameof(RelationArrays))]
    public void Write_OneOrNoName_AsTheSerializerWritesAList(string key, string member)
    {
        var one = Read(key, member, """["prod"]""");
        var none = Read(key, member, "[]");

        JsonSerializer.Serialize(one, one.GetType()).ShouldContain($"\"{member}\":{JsonSerializer.Serialize(new List<string> { "prod" })}");
        JsonSerializer.Serialize(none, none.GetType()).ShouldContain($"\"{member}\":[]");
    }

    [Theory]
    [MemberData(nameof(RelationArrays))]
    public void Write_ANullSet_AsNull(string key, string member)
    {
        var annotation = AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), "billing", "default");
        annotation.GetType().GetProperty(member)!.SetValue(annotation, null);

        var json = JsonSerializer.Serialize(annotation, annotation.GetType());

        json.ShouldContain($"\"{member}\":null");
        JsonSerializer.Serialize(annotation, annotation.GetType(), new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull })
            .ShouldNotContain($"\"{member}\"");
    }

    // A new relation array needs the converter too, or its bodies fail again.
    [Fact]
    public void EverySetOfTheModels_HasTheConverter()
    {
        var sets = typeof(Annotation).Assembly.GetTypes()
            .Where(type => type.IsClass && typeof(Annotation).IsAssignableFrom(type))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(property => property.PropertyType == typeof(IReadOnlySet<string>))
            .ToList();

        sets.Select(property => $"{property.DeclaringType!.Name}.{property.Name}").ShouldBe(
            RelationArrays.Select(row => $"{RuntimeType(row.Data.Item1).Name}.{row.Data.Item2}"),
            ignoreOrder: true);
        sets.ShouldAllBe(property => property.GetCustomAttribute<JsonConverterAttribute>() != null
            && property.GetCustomAttribute<JsonConverterAttribute>()!.ConverterType!.Name == "RelationNamesJsonConverter");
    }

    // The annotation of the key with the member set to the raw JSON value, or without the member for null.
    private static JsonObject Body(string key, string member, string? value)
    {
        var annotation = AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), "billing", "default");
        var body = JsonSerializer.SerializeToNode(annotation, annotation.GetType())!.AsObject();
        body.Remove(member);

        if (value is not null)
        {
            body[member] = JsonNode.Parse(value);
        }

        return body;
    }

    private static Annotation Read(string key, string member, string? value)
    {
        return (Annotation)JsonSerializer.Deserialize(Body(key, member, value).ToJsonString(), RuntimeType(key))!;
    }

    private static Type RuntimeType(string key)
    {
        return AnnotationTypes.GetRuntimeType(AnnotationKey.Parse(key).Type);
    }

    private static IReadOnlySet<string> Names(Annotation annotation, string member)
    {
        return (IReadOnlySet<string>)annotation.GetType().GetProperty(member)!.GetValue(annotation)!;
    }
}

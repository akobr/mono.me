using _42.Platform.Storyteller.Mcp.Tools;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// Explains an effective configuration from its composition (docs/Platform/Storyteller/inheritance.md, "Composition
// read"): the step that set each value and the branch it came through, the other documents that hold a value at the same
// place, the documents that supply nothing, the bindings and the machines that can read the values. It reads nothing
// itself: the tool reads the API and passes the results in.
public static class ConfigurationExplainer
{
    public const int MaxValues = 200;
    public const int MaxKeyValueLookups = 50;

    public const string StaleNote =
        "The effective read and this composition differ (Hash): the effective cache has not caught up yet, or a document "
        + "changed between the two reads. Explain it again to see whether they agree.";

    private static readonly string[] FixedNotes =
    [
        "Bindings are not evaluated in this read: an Expression is the binding as stored. check_configuration_bindings evaluates them, when the server offers the secret tools.",
        "A template is merged at every node of its type, after that node's ancestors and before the node's own content. In this document it can still lose to the content and templates of deeper nodes, and to an ancestor brought back by a later branch (see Via and AlsoSetBy).",
        "A shared ancestor is merged once per branch, so a later branch can bring its value back over what an earlier branch set (see Via and AlsoSetBy).",
        "Views never inherit from each other.",
    ];

    // The bindings of the effective document at and under the pointer.
    public static IReadOnlyList<BindingOccurrence> BindingsOf(Sdk.ConfigurationComposition composition, string pointer)
    {
        return BindingScanner.Scan(ContentOf(composition))
            .Where(binding => JsonPointer.IsAtOrUnder(binding.Pointer, pointer))
            .ToList();
    }

    // The Values store keys the bindings read, each once (the store compares keys without case), in document order; the
    // keys only a $definition holds come last, so the lookup budget goes to the certain reads first.
    public static IReadOnlyList<string> ValuesStoreKeys(IEnumerable<BindingOccurrence> bindings)
    {
        return bindings
            .OrderBy(binding => binding.InDefinition)
            .SelectMany(binding => binding.Parsed.References)
            .Where(reference => reference.IsValuesStore)
            .Select(reference => reference.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // The machines whose scopes allow configuration reads, grouped by how their annotation restriction reaches the key. A
    // machine is allowed on its restriction and every descendant of it (MachineAnnotationRestriction), and the effective
    // document of a descendant of the key holds the key's values.
    public static ConfigurationConsumers Consumers(AnnotationKey key, IEnumerable<Sdk.MachineAccess> machines)
    {
        var direct = new List<ConfigurationConsumers.Machine>();
        var throughDescendants = new List<ConfigurationConsumers.Machine>();
        var throughSharedDescendants = new List<ConfigurationConsumers.Machine>();
        var keyText = key.ToString();

        foreach (var machine in machines)
        {
            var scopes = machine.Scopes?.ToList() ?? [];

            if (!scopes.Contains(Sdk.MachineScope.ConfigurationRead) && !scopes.Contains(Sdk.MachineScope.ConfigurationWrite))
            {
                continue;
            }

            var restriction = string.IsNullOrWhiteSpace(machine.AnnotationKey) ? null : machine.AnnotationKey;
            var entry = new ConfigurationConsumers.Machine(machine.Id, restriction, scopes, machine.CredentialKind, scopes.Contains(Sdk.MachineScope.SecretsRead));

            if (restriction is null)
            {
                direct.Add(entry);
            }
            else if (AnnotationKey.TryParse(restriction, out var restrictionKey))
            {
                if (string.Equals(restrictionKey.ToString(), keyText, StringComparison.Ordinal) || key.IsDescendantOf(restrictionKey))
                {
                    direct.Add(entry);
                }
                else if (restrictionKey.IsDescendantOf(key))
                {
                    throughDescendants.Add(entry);
                }
                else if (SharesDescendants(key, restrictionKey))
                {
                    throughSharedDescendants.Add(entry);
                }
            }
        }

        return new ConfigurationConsumers
        {
            Machines = new ConfigurationConsumers.MachineGroups(direct, throughDescendants, throughSharedDescendants),
        };
    }

    public static ConfigurationConsumers ConsumersUnavailable(string reason)
    {
        return new ConfigurationConsumers { Reason = reason };
    }

    // keyValues holds the metadata of the Values store keys that were looked up.
    public static ConfigurationExplanation Explain(
        Sdk.ConfigurationComposition composition,
        string? effectiveHash,
        string pointer,
        IReadOnlyDictionary<string, KeyValueMetadata> keyValues,
        ConfigurationConsumers consumers)
    {
        var content = ContentOf(composition);
        var nodes = (composition.Nodes ?? []).ToList();
        var steps = (composition.Steps ?? []).OrderBy(step => step.Order).ToList();
        var stepsByOrder = steps.ToDictionary(step => step.Order);
        var sources = (composition.Sources ?? []).ToDictionary(source => source.Id, StringComparer.Ordinal);
        var origins = (composition.Origins ?? []).Where(origin => JsonPointer.IsAtOrUnder(origin.Path, pointer)).ToList();

        var values = origins
            .Take(MaxValues)
            .Select(origin => ExplainValue(origin, content, nodes, steps, stepsByOrder, sources))
            .ToList();

        var counts = origins
            .Select(origin => stepsByOrder.TryGetValue(origin.Step, out var step) ? step.SourceId : null)
            .OfType<string>()
            .GroupBy(id => id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var lookedUp = new Dictionary<string, KeyValueMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, metadata) in keyValues)
        {
            lookedUp.TryAdd(key, metadata);
        }

        var bindings = BindingsOf(composition, pointer);
        var isStale = !string.Equals(composition.Hash, effectiveHash, StringComparison.Ordinal);

        return new ConfigurationExplanation
        {
            AnnotationKey = composition.AnnotationKey,
            Pointer = pointer.Length == 0 ? null : pointer,
            Hash = composition.Hash,
            EffectiveHash = effectiveHash,
            IsStale = isStale,
            Values = values,
            IsTruncated = origins.Count > MaxValues,
            Sources = (composition.Sources ?? []).Select(source => ExplainedSource.From(source, counts.GetValueOrDefault(source.Id))).ToList(),
            Bindings = bindings.Select(binding => ExplainBinding(binding, lookedUp)).ToList(),
            KeyValueLookupsTruncated = ValuesStoreKeys(bindings).Count > MaxKeyValueLookups,
            Consumers = consumers,
            Notes = isStale ? [StaleNote, .. FixedNotes] : FixedNotes,
        };
    }

    private static JObject ContentOf(Sdk.ConfigurationComposition composition)
    {
        return ToolResults.ToToken(composition.Content) as JObject ?? new JObject();
    }

    // Two keys share descendants when every name both of them have is the same: the unit of execution with the names of
    // both descends from each (rst.billing and sbt.northwind share usg.northwind.billing). A name a type lacks is "".
    private static bool SharesDescendants(AnnotationKey first, AnnotationKey second)
    {
        return Agree(first.SubjectName, second.SubjectName)
            && Agree(first.ResponsibilityName, second.ResponsibilityName)
            && Agree(first.ContextName, second.ContextName)
            && Agree(first.UnitName, second.UnitName);

        static bool Agree(string name, string other)
        {
            return name.Length == 0 || other.Length == 0 || string.Equals(name, other, StringComparison.Ordinal);
        }
    }

    private static ExplainedValue ExplainValue(
        Sdk.ConfigurationCompositionOrigin origin,
        JObject content,
        List<Sdk.ConfigurationCompositionNode> nodes,
        List<Sdk.ConfigurationCompositionStep> steps,
        Dictionary<int, Sdk.ConfigurationCompositionStep> stepsByOrder,
        Dictionary<string, Sdk.ConfigurationCompositionSource> sources)
    {
        var token = JsonPointer.Find(content, origin.Path);
        var (value, kind) = Describe(token);

        if (!stepsByOrder.TryGetValue(origin.Step, out var step))
        {
            return new ExplainedValue { Pointer = origin.Path, Value = value, ValueKind = kind, Step = origin.Step, Via = [] };
        }

        var source = sources.GetValueOrDefault(step.SourceId);
        var node = nodes.FirstOrDefault(item => item.Index == step.Node);

        // only a scalar lists the other documents: an object or array merges with an earlier one of its kind and keeps the
        // first step, so a later document holding one there was merged in, not left out, and its members and items carry
        // their own steps. Arrays merge by union and positions shift, so their items are attributed by their origins only.
        var setters = token is JContainer || JsonPointer.IsInArray(content, origin.Path)
            ? null
            : steps.Where(other => other.Order != step.Order)
                .Select(other => Setter(other, step, origin.Path, nodes, sources))
                .OfType<ExplainedValue.Setter>()
                .ToList();

        return new ExplainedValue
        {
            Pointer = origin.Path,
            Value = value,
            ValueKind = kind,
            Step = origin.Step,
            Source = source is null ? null : ExplainedSource.From(source),
            Node = node is null ? null : new ExplainedValue.ValueNode(node.AnnotationKey, node.Depth),
            Via = CompositionReplay.Via(nodes, step.Node),
            AlsoSetBy = setters is { Count: > 0 } ? setters : null,
        };
    }

    // Another step whose own document holds a value at the path; a null never sets a value.
    private static ExplainedValue.Setter? Setter(
        Sdk.ConfigurationCompositionStep other,
        Sdk.ConfigurationCompositionStep winner,
        string path,
        List<Sdk.ConfigurationCompositionNode> nodes,
        Dictionary<string, Sdk.ConfigurationCompositionSource> sources)
    {
        var token = JsonPointer.FindInObjects(CompositionReplay.ContentOf(sources.GetValueOrDefault(other.SourceId)), path);

        if (token is null || token.Type == JTokenType.Null)
        {
            return null;
        }

        var (value, kind) = Describe(token);

        return new ExplainedValue.Setter
        {
            Step = other.Order,
            SourceId = other.SourceId,
            Value = value,
            ValueKind = kind,
            Via = CompositionReplay.Via(nodes, other.Node),
            BeforeWinner = other.Order < winner.Order,
        };
    }

    private static (JToken? Value, string? Kind) Describe(JToken? token)
    {
        return token switch
        {
            null => (null, null),
            JObject => (null, "object"),
            JArray => (null, "array"),
            _ => (token.DeepClone(), null),
        };
    }

    private static ExplainedBinding ExplainBinding(BindingOccurrence binding, Dictionary<string, KeyValueMetadata> keyValues)
    {
        var reads = new List<string>();

        if (binding.Parsed.ReadsDocument)
        {
            reads.Add("this document (@config)");
        }

        if (binding.Parsed.ReadsAnnotations)
        {
            reads.Add("annotation values (@annotation)");
        }

        string? note = null;

        if (!binding.IsValid)
        {
            var emitted = binding.InDefinition ? " if the program of its object binding emits it" : string.Empty;

            note = binding.Parsed switch
            {
                { Form: BindingForm.Object } =>
                    $"A malformed object binding (an unknown kind, no $definition, another property, or a $context that cannot resolve to an object): the resolved read fails on it{emitted}.",
                { ExceedsLimits: true } =>
                    $"Not read here: longer than {BindingExpressions.MaxLength} characters or nested more than {BindingExpressions.MaxDepth} levels deep, so what it reads is not known.",
                _ => $"Not a valid binding expression: the resolved read fails on it{emitted}.",
            };
        }
        else if (binding.InDefinition)
        {
            note = "In the $definition of an object binding: read only if the program emits this string, because the resolver binds the program's result again.";
        }
        else if (binding.EnvelopePointer is not null)
        {
            note = $"Resolved first as the $context of the object binding at '{binding.EnvelopePointer}'.";
        }

        return new ExplainedBinding
        {
            Pointer = binding.Pointer,
            Expression = binding.Expression,
            Form = binding.Parsed.Form,
            Name = binding.Parsed.Name,
            IsValid = binding.IsValid,
            EnvelopePointer = binding.EnvelopePointer,
            InDefinition = binding.InDefinition ? true : null,
            References = binding.Parsed.References.Select(reference => ExplainReference(reference, keyValues)).ToList(),
            Reads = reads.Count > 0 ? reads : null,
            Note = note,
        };
    }

    private static ExplainedBinding.Reference ExplainReference(BindingReference reference, Dictionary<string, KeyValueMetadata> keyValues)
    {
        if (!reference.IsValuesStore)
        {
            return new ExplainedBinding.Reference
            {
                Key = reference.Key,
                Source = reference.Source,
                Note = $"'{reference.Source}' is not the Values store but a source registered on the server (get_binding_catalog), for example a Key Vault; it is not looked up.",
            };
        }

        if (!keyValues.TryGetValue(reference.Key, out var metadata))
        {
            return new ExplainedBinding.Reference
            {
                Key = reference.Key,
                Source = reference.Source,
                IsValuesStore = true,
                Note = $"Not looked up: an explanation looks up at most {MaxKeyValueLookups} pairs.",
            };
        }

        return new ExplainedBinding.Reference
        {
            Key = reference.Key,
            Source = reference.Source,
            IsValuesStore = true,
            KeyValue = metadata,
            Note = NoteFor(reference, metadata),
        };
    }

    private static string? NoteFor(BindingReference reference, KeyValueMetadata metadata)
    {
        return metadata switch
        {
            { Exists: null } => $"The pair could not be read (status {metadata.Status}).",
            { Exists: false } => "No such pair in this view: the binding stays as stored.",
            { IsDeleted: true } => "The pair is deleted: the binding stays as stored until the pair is restored.",
            { IsSecret: true } when reference.Source == "public" => "The public source declines a secret: the binding stays as stored.",
            { IsSecret: false } when reference.Source == "secret" => "The secret source declines a public pair: the binding stays as stored.",
            { IsSecret: true } => "A secret: a resolved read fills it in only for a caller that may read secrets.",
            _ => null,
        };
    }
}

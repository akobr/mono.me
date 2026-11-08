using System.IO.Abstractions;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace _42.Platform.Cli.AutomationTests.Coverage;

// Reads the command tree of 42.Platform.Cli.dll with System.Reflection.MetadataLoadContext: types and attributes as
// metadata, so no code of the CLI (or of McMaster) runs. It follows what McMaster.Extensions.CommandLineUtils 5.1 does with
// reflection (sform's build has no generated command metadata):
//   - [Command(name, aliases…)] names a command (Name = … too); without a name, the class name without "Command" in kebab
//     case. [Subcommand(typeof(…), …)] nests commands, starting at the root _42.Platform.Cli.Commands.PlatformCommand.
//   - [Option] and [VersionOption] on properties (and fields, should a version of McMaster allow them) of the class and its
//     base classes: the forms of the template ("-p|--projectKey <KEY>", also "--name=<V>" and "-n|--name:<V>"); without a
//     template, ShortName and LongName, else the member name in kebab case (--foo-bar) and its first letter (-f), as
//     McMaster's reflection derives them. [HelpOption] and the -?|-h|--help of McMaster's conventions are not items.
//   - [Argument(order, name)] on properties and fields, sorted by order: arg0, arg1, ….
//   - A group that only prints its help: its OnExecute or OnExecuteAsync calls a method named ShowHelp (read from the IL
//     through System.Reflection.Metadata).
public static class CommandTreeReader
{
    public const string RootType = "_42.Platform.Cli.Commands.PlatformCommand";

    private const string McMaster = "McMaster.Extensions.CommandLineUtils.";
    private const string CommandAttribute = McMaster + "CommandAttribute";
    private const string SubcommandAttribute = McMaster + "SubcommandAttribute";
    private const string OptionAttribute = McMaster + "OptionAttribute";
    private const string VersionOptionAttribute = McMaster + "VersionOptionAttribute";
    private const string ArgumentAttribute = McMaster + "ArgumentAttribute";

    private const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    // cliAssemblyPath: 42.Platform.Cli.dll in its build output (with McMaster and the SDK next to it). enumTypes: full names
    // of enums whose member names CommandTree.Enums should hold.
    public static CommandTree Read(string cliAssemblyPath, IEnumerable<string>? enumTypes = null, IFileSystem? fileSystem = null)
    {
        fileSystem ??= new FileSystem();
        ArgumentException.ThrowIfNullOrWhiteSpace(cliAssemblyPath);
        if (!fileSystem.File.Exists(cliAssemblyPath))
        {
            throw new InvalidOperationException($"42.Platform.Cli.dll is not at '{cliAssemblyPath}'; build the tests, which build src/Platform/Cli/src too.");
        }

        var directory = fileSystem.Path.GetDirectoryName(cliAssemblyPath)!;
        var runtime = fileSystem.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var candidates = fileSystem.Directory.EnumerateFiles(directory, "*.dll")
            .Concat(fileSystem.Directory.EnumerateFiles(runtime, "*.dll"))
            .ToList();

        using var context = new MetadataLoadContext(new PathAssemblyResolver(candidates), "System.Private.CoreLib");
        using var stream = fileSystem.File.OpenRead(cliAssemblyPath);
        using var pe = new PEReader(stream);
        var reader = new Reader(context.LoadFromAssemblyPath(cliAssemblyPath), pe);

        var root = reader.Build(reader.RootType(), null, []);
        var enums = (enumTypes ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(name => name, name => reader.EnumNames(name, context, directory, fileSystem), StringComparer.Ordinal);
        return new CommandTree(root, cliAssemblyPath, enums);
    }

    // "FooBar" → "foo-bar", "URLValue" → "url-value", as McMaster's kebab case.
    public static string KebabCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];
            if (char.IsUpper(current) && index > 0)
            {
                var previous = name[index - 1];
                var nextIsLower = index + 1 < name.Length && char.IsLower(name[index + 1]);
                if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
                {
                    builder.Append('-');
                }
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }

    // The forms of an option template: "-p|--projectKey <KEY>" → "-p", "--projectKey".
    public static IReadOnlyList<string> ParseTemplate(string template)
    {
        var forms = new List<string>();
        foreach (var part in template.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var start = part.StartsWith("--", StringComparison.Ordinal) ? 2 : part.StartsWith('-') ? 1 : 0;
            if (start == 0)
            {
                continue;
            }

            var end = part.IndexOfAny([' ', '<', ':', '='], start);
            var form = end < 0 ? part : part[..end];
            if (form.Length > start)
            {
                forms.Add(form);
            }
        }

        return forms;
    }

    private static string? TypeNameOf(Type type)
    {
        return type.FullName ?? type.Name;
    }

    private static IEnumerable<object?> Values(CustomAttributeTypedArgument argument)
    {
        if (argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> items)
        {
            return items.Select(item => item.Value);
        }

        return [argument.Value];
    }

    private sealed class Reader(Assembly assembly, PEReader pe)
    {
        private readonly MetadataReader metadata = pe.GetMetadataReader();
        private HashSet<int>? showHelpTokens;

        public Type RootType()
        {
            if (assembly.GetType(CommandTreeReader.RootType) is { } root)
            {
                return root;
            }

            // Fallback: the only [Command] type no [Subcommand] names.
            var commands = assembly.GetTypes().Where(type => Attribute(type, CommandAttribute) is not null).ToList();
            var nested = commands.SelectMany(type => Subcommands(type)).Select(TypeNameOf).ToHashSet(StringComparer.Ordinal);
            var roots = commands.Where(type => !nested.Contains(TypeNameOf(type)) && Subcommands(type).Any()).ToList();
            return roots.Count == 1
                ? roots[0]
                : throw new InvalidOperationException($"{assembly.GetName().Name} has no {CommandTreeReader.RootType}, and the root of its commands is not clear: {string.Join(", ", roots.Select(TypeNameOf))}.");
        }

        public CliCommand Build(Type type, CliCommand? parent, HashSet<string> path)
        {
            var typeName = TypeNameOf(type)!;
            if (!path.Add(typeName))
            {
                throw new InvalidOperationException($"The [Subcommand] attributes of sform loop at {typeName}.");
            }

            var (name, aliases) = NamesOf(type);
            var command = new CliCommand(name, aliases, typeName, parent)
            {
                Options = OptionsOf(type),
                Arguments = ArgumentsOf(type),
            };

            var subcommands = Subcommands(type).ToList();
            command.IsHelpOnly = subcommands.Count > 0 && CallsShowHelp(type);
            foreach (var subcommand in subcommands)
            {
                Build(subcommand, command, path);
            }

            path.Remove(typeName);
            return command;
        }

        public IReadOnlyList<string>? EnumNames(string fullName, MetadataLoadContext context, string directory, IFileSystem fileSystem)
        {
            var type = assembly.GetType(fullName);
            if (type is null)
            {
                foreach (var file in fileSystem.Directory.EnumerateFiles(directory, "42.*.dll"))
                {
                    try
                    {
                        type = context.LoadFromAssemblyPath(file).GetType(fullName);
                    }
                    catch (Exception exception) when (exception is BadImageFormatException or FileLoadException)
                    {
                        continue;
                    }

                    if (type is not null)
                    {
                        break;
                    }
                }
            }

            return type is { IsEnum: true } ? NamesOfEnum(type) : null;
        }

        private static IReadOnlyList<string> NamesOfEnum(Type type)
        {
            return [.. type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.IsLiteral).Select(field => field.Name)];
        }

        private static CustomAttributeData? Attribute(MemberInfo member, string fullName)
        {
            return member.GetCustomAttributesData().FirstOrDefault(data => data.AttributeType.FullName == fullName);
        }

        private static IEnumerable<Type> Subcommands(Type type)
        {
            return type.GetCustomAttributesData()
                .Where(data => data.AttributeType.FullName == SubcommandAttribute)
                .SelectMany(data => data.ConstructorArguments.SelectMany(Values))
                .OfType<Type>();
        }

        private static (string Name, IReadOnlyList<string> Aliases) NamesOf(Type type)
        {
            var data = Attribute(type, CommandAttribute);
            var names = data is null
                ? []
                : data.ConstructorArguments.SelectMany(Values).OfType<string>()
                    .Concat(data.NamedArguments.Where(argument => argument.MemberName == "Name").Select(argument => argument.TypedValue.Value).OfType<string>())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

            if (names.Count > 0)
            {
                return (names[0], names.Skip(1).ToList());
            }

            var className = type.Name.EndsWith("Command", StringComparison.Ordinal) && type.Name.Length > "Command".Length
                ? type.Name[..^"Command".Length]
                : type.Name;
            return (KebabCase(className), []);
        }

        private static IEnumerable<Type> Chain(Type type)
        {
            for (var current = type; current is not null && current.FullName != "System.Object"; current = current.BaseType)
            {
                yield return current;
            }
        }

        // Properties and fields of the class and its base classes, the most derived first; a member hidden by a derived one
        // of the same name is skipped.
        private static IEnumerable<(MemberInfo Member, Type MemberType)> Members(Type type)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var current in Chain(type))
            {
                foreach (var member in current.GetMembers(Declared))
                {
                    var memberType = member switch
                    {
                        PropertyInfo property => property.PropertyType,
                        FieldInfo field => field.FieldType,
                        _ => null,
                    };

                    if (memberType is not null && seen.Add(member.Name))
                    {
                        yield return (member, memberType);
                    }
                }
            }
        }

        private static IReadOnlyList<CliOption> OptionsOf(Type type)
        {
            var options = new List<CliOption>();
            foreach (var (member, memberType) in Members(type))
            {
                var data = Attribute(member, OptionAttribute) ?? Attribute(member, VersionOptionAttribute);
                if (data is null)
                {
                    continue;
                }

                var template = data.ConstructorArguments.Select(argument => argument.Value).OfType<string>().FirstOrDefault();
                var forms = template is not null ? ParseTemplate(template) : DerivedForms(member, data);
                var enumType = EnumOf(memberType);
                options.Add(new CliOption(
                    forms,
                    TypeNameOf(member.DeclaringType!)!,
                    member.Name,
                    Display(memberType),
                    enumType is null ? null : TypeNameOf(enumType),
                    enumType is null ? null : NamesOfEnum(enumType)));
            }

            return options;
        }

        private static IReadOnlyList<string> DerivedForms(MemberInfo member, CustomAttributeData data)
        {
            string? Named(string name) => data.NamedArguments.Where(argument => argument.MemberName == name).Select(argument => argument.TypedValue.Value).OfType<string>().FirstOrDefault();

            var longName = Named("LongName") ?? KebabCase(member.Name);
            var shortName = Named("ShortName") ?? longName[..1];
            return [$"-{shortName}", $"--{longName}"];
        }

        private static IReadOnlyList<CliArgument> ArgumentsOf(Type type)
        {
            var declared = new List<(int Order, string Name, string DeclaringType, string Member)>();
            foreach (var (member, _) in Members(type))
            {
                if (Attribute(member, ArgumentAttribute) is not { } data)
                {
                    continue;
                }

                var order = data.ConstructorArguments.Select(argument => argument.Value).OfType<int>().FirstOrDefault();
                var name = data.ConstructorArguments.Select(argument => argument.Value).OfType<string>().FirstOrDefault()
                    ?? data.NamedArguments.Where(argument => argument.MemberName == "Name").Select(argument => argument.TypedValue.Value).OfType<string>().FirstOrDefault()
                    ?? member.Name;
                declared.Add((order, name, TypeNameOf(member.DeclaringType!)!, member.Name));
            }

            return [.. declared.OrderBy(argument => argument.Order).Select((argument, index) => new CliArgument(index, argument.Name, argument.DeclaringType, argument.Member))];
        }

        // The enum of T, T?, T[] or a generic collection of T; null otherwise.
        private static Type? EnumOf(Type type)
        {
            if (type.IsEnum)
            {
                return type;
            }

            if (type.IsArray)
            {
                return EnumOf(type.GetElementType()!);
            }

            return type.IsGenericType && type.GetGenericArguments() is [var single] ? EnumOf(single) : null;
        }

        private static string Display(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1")
            {
                return Display(type.GetGenericArguments()[0]) + "?";
            }

            if (type.IsArray)
            {
                return Display(type.GetElementType()!) + "[]";
            }

            if (type.IsGenericType)
            {
                return $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", type.GetGenericArguments().Select(Display))}>";
            }

            return type.Name;
        }

        private bool CallsShowHelp(Type type)
        {
            showHelpTokens ??= metadata.MemberReferences
                .Where(handle => metadata.GetString(metadata.GetMemberReference(handle).Name) == "ShowHelp")
                .Select(handle => MetadataTokens.GetToken(handle))
                .ToHashSet();

            if (showHelpTokens.Count == 0 || type.Assembly != assembly)
            {
                return false;
            }

            foreach (var method in type.GetMethods(Declared).Where(method => method.Name is "OnExecute" or "OnExecuteAsync"))
            {
                var definition = metadata.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(method.MetadataToken));
                if (definition.RelativeVirtualAddress == 0)
                {
                    continue;
                }

                var il = pe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes();
                for (var index = 0; il is not null && index + 4 < il.Length; index++)
                {
                    // call (0x28) or callvirt (0x6F) with a member reference token
                    if ((il[index] == 0x28 || il[index] == 0x6F) && showHelpTokens.Contains(BitConverter.ToInt32(il, index + 1)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}

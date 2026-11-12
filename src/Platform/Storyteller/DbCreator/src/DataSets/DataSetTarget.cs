using System;
using System.Collections.Generic;
using System.Linq;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// The organization, project and view one run seeds, every set and its dependencies alike.
public sealed record class DataSetTarget(string Organization, string Project, string View)
{
    public bool IsDefaultView => View == Storyteller.Constants.DefaultViewName;

    public string ProjectKey => $"{Organization}.{Project}";

    // The given names win; otherwise the manifests of the named sets give them and must agree (a set without one does
    // not disagree). The view is "default" when nothing names one. DataSetException when an organization or a project
    // is named nowhere, or the sets disagree.
    public static DataSetTarget Resolve(IReadOnlyList<DataSet> sets, string? organization = null, string? project = null, string? view = null)
    {
        return new DataSetTarget(
            Pick(sets, organization, manifest => manifest.Organization, "organization", "--organization")
                ?? throw new DataSetException("No organization to seed into: give --organization, or name one in the set.json of the set (Organization)."),
            Pick(sets, project, manifest => manifest.Project, "project", "--project")
                ?? throw new DataSetException("No project to seed into: give --project, or name one in the set.json of the set (Project)."),
            Pick(sets, view, manifest => manifest.View, "view", "--view") ?? Storyteller.Constants.DefaultViewName);
    }

    // The rules of new names, before anything is written: the organization when the seed creates it (an existing one
    // may predate the rules) or when it has a dot, the project always, the view unless it is "default". A dotted name
    // is never an organization, even when it is the key of an existing project (the access model refuses it too).
    // DataSetException otherwise.
    public void EnsureNames(bool isNewOrganization)
    {
        try
        {
            if (isNewOrganization || Organization.Contains('.'))
            {
                NameRules.EnsureOrganizationName(Organization);
            }

            NameRules.EnsureProjectName(Project);

            if (!IsDefaultView)
            {
                NameRules.EnsureViewName(View);
            }
        }
        catch (InvalidInputException exception)
        {
            throw new DataSetException(exception.Message, exception);
        }
    }

    public override string ToString()
    {
        return $"{Organization}/{Project}/{View}";
    }

    private static string? Pick(IReadOnlyList<DataSet> sets, string? given, Func<DataSetManifest, string?> select, string what, string option)
    {
        if (!string.IsNullOrWhiteSpace(given))
        {
            return given.Trim();
        }

        var named = sets
            .Select(set => (set.Name, Value: select(set.Manifest)))
            .Where(pair => pair.Value is not null)
            .ToList();
        var values = named.Select(pair => pair.Value!).Distinct(StringComparer.Ordinal).ToList();

        if (values.Count > 1)
        {
            var sources = string.Join(", ", named.Select(pair => $"'{pair.Value}' in {pair.Name}"));
            throw new DataSetException($"The sets name different {what}s to seed into ({sources}): give {option}.");
        }

        return values.SingleOrDefault();
    }
}

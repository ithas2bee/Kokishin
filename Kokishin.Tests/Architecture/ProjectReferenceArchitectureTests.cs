using System.Xml.Linq;

namespace Kokishin.Tests.Architecture;

public class ProjectReferenceArchitectureTests
{
    [Fact]
    public void ActualProjectReferences_ComplyWithArchitectureRules()
    {
        var solutionRoot = ProjectReferenceArchitecture.FindSolutionRoot();
        var references = ProjectReferenceArchitecture.ReadSolutionReferences(solutionRoot);

        var violations = ProjectReferenceArchitecture.ValidateReferences(references);

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ActualProjectReferences_ContainNoCircularDependencies()
    {
        var solutionRoot = ProjectReferenceArchitecture.FindSolutionRoot();
        var references = ProjectReferenceArchitecture.ReadSolutionReferences(solutionRoot);

        var cycle = ProjectReferenceArchitecture.FindCircularDependency(references);

        Assert.True(
            cycle is null,
            cycle is null ? null : ProjectReferenceArchitecture.FormatCycleViolation(cycle));
    }

    [Theory]
    [InlineData(
        "Kokishin.Domain",
        "Kokishin.Infrastructure",
        "Kokishin.Domain must remain independent of other Kokishin projects.")]
    [InlineData(
        "Kokishin.Application",
        "Kokishin.Api",
        "Kokishin.Application may reference Kokishin.Domain only.")]
    [InlineData(
        "Kokishin.Infrastructure",
        "Kokishin.Api",
        "Kokishin.Infrastructure may reference Kokishin.Application and Kokishin.Domain only.")]
    [InlineData(
        "Kokishin.Api",
        "Kokishin.Tests",
        "Kokishin.Api may reference Kokishin.Application and Kokishin.Infrastructure only.")]
    [InlineData(
        "Kokishin.Tests",
        "Kokishin.Domain",
        "Kokishin.Tests may reference Kokishin.Api and Kokishin.Infrastructure only; its tests use the API host and directly test infrastructure registration.")]
    public void InvalidProjectReference_ReportsTheOffendingDependency(
        string project,
        string dependency,
        string rule)
    {
        var references = ProjectReferenceArchitecture.CreateExpectedReferenceGraph();
        references[project].Add(dependency);
        using var fixture = ProjectFileFixture.Create(references);

        var actualReferences = ProjectReferenceArchitecture.ReadSolutionReferences(fixture.Root);

        var violation = Assert.Single(ProjectReferenceArchitecture.ValidateReferences(actualReferences));

        Assert.Contains($"{project} references {dependency}", violation);
        Assert.Contains(rule, violation);
    }

    [Fact]
    public void MissingRequiredProjectReference_ReportsTheMissingDependency()
    {
        var references = ProjectReferenceArchitecture.CreateExpectedReferenceGraph();
        references["Kokishin.Application"].Remove("Kokishin.Domain");
        using var fixture = ProjectFileFixture.Create(references);

        var actualReferences = ProjectReferenceArchitecture.ReadSolutionReferences(fixture.Root);

        var violation = Assert.Single(ProjectReferenceArchitecture.ValidateReferences(actualReferences));

        Assert.Contains("Kokishin.Application is missing required reference to Kokishin.Domain", violation);
        Assert.Contains("Kokishin.Application may reference Kokishin.Domain only", violation);
    }

    [Fact]
    public void DirectCircularDependency_ReportsTheCycle()
    {
        var references = ProjectReferenceArchitecture.CreateExpectedReferenceGraph();
        foreach (var dependencies in references.Values)
        {
            dependencies.Clear();
        }

        references["Kokishin.Domain"].Add("Kokishin.Application");
        references["Kokishin.Application"].Add("Kokishin.Domain");
        using var fixture = ProjectFileFixture.Create(references);

        var actualReferences = ProjectReferenceArchitecture.ReadSolutionReferences(fixture.Root);

        var cycle = ProjectReferenceArchitecture.FindCircularDependency(actualReferences);

        Assert.NotNull(cycle);
        Assert.Contains("Kokishin.Application -> Kokishin.Domain -> Kokishin.Application",
            ProjectReferenceArchitecture.FormatCycleViolation(cycle));
    }

    [Fact]
    public void IndirectCircularDependency_ReportsTheCycle()
    {
        var references = ProjectReferenceArchitecture.CreateExpectedReferenceGraph();
        foreach (var dependencies in references.Values)
        {
            dependencies.Clear();
        }

        references["Kokishin.Domain"].Add("Kokishin.Infrastructure");
        references["Kokishin.Infrastructure"].Add("Kokishin.Application");
        references["Kokishin.Application"].Add("Kokishin.Domain");
        using var fixture = ProjectFileFixture.Create(references);

        var actualReferences = ProjectReferenceArchitecture.ReadSolutionReferences(fixture.Root);

        var cycle = ProjectReferenceArchitecture.FindCircularDependency(actualReferences);

        Assert.NotNull(cycle);
        Assert.Contains("Kokishin.Application -> Kokishin.Domain -> Kokishin.Infrastructure -> Kokishin.Application",
            ProjectReferenceArchitecture.FormatCycleViolation(cycle));
    }
}

internal static class ProjectReferenceArchitecture
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Kokishin.Domain"] = [],
            ["Kokishin.Application"] = ["Kokishin.Domain"],
            ["Kokishin.Infrastructure"] = ["Kokishin.Application", "Kokishin.Domain"],
            ["Kokishin.Api"] = ["Kokishin.Application", "Kokishin.Infrastructure"],
            ["Kokishin.Tests"] = ["Kokishin.Api", "Kokishin.Infrastructure"]
        };

    private static readonly IReadOnlyDictionary<string, string> RuleDescriptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Kokishin.Domain"] = "Kokishin.Domain must remain independent of other Kokishin projects.",
            ["Kokishin.Application"] = "Kokishin.Application may reference Kokishin.Domain only.",
            ["Kokishin.Infrastructure"] =
                "Kokishin.Infrastructure may reference Kokishin.Application and Kokishin.Domain only.",
            ["Kokishin.Api"] =
                "Kokishin.Api may reference Kokishin.Application and Kokishin.Infrastructure only.",
            ["Kokishin.Tests"] =
                "Kokishin.Tests may reference Kokishin.Api and Kokishin.Infrastructure only; its tests use the API host and directly test infrastructure registration."
        };

    public static string FindSolutionRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kokishin.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate Kokishin.sln above the test output directory '{AppContext.BaseDirectory}'.");
    }

    public static Dictionary<string, HashSet<string>> ReadSolutionReferences(string solutionRoot)
    {
        var references = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var project in AllowedReferences.Keys)
        {
            var projectFile = Path.Combine(solutionRoot, project, $"{project}.csproj");
            if (!File.Exists(projectFile))
            {
                throw new FileNotFoundException(
                    $"Could not find the project file for {project}.", projectFile);
            }

            references.Add(project, ReadProjectReferences(projectFile));
        }

        return references;
    }

    public static Dictionary<string, HashSet<string>> CreateExpectedReferenceGraph() =>
        AllowedReferences.ToDictionary(
            project => project.Key,
            project => project.Value.ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

    public static List<string> ValidateReferences(
        IReadOnlyDictionary<string, HashSet<string>> actualReferences)
    {
        var violations = new List<string>();

        foreach (var project in actualReferences.Keys.Except(AllowedReferences.Keys).Order())
        {
            violations.Add(
                $"Architecture violation: {project} has no approved Kokishin project reference rule.");
        }

        foreach (var (project, allowedReferences) in AllowedReferences)
        {
            if (!actualReferences.TryGetValue(project, out var actual))
            {
                violations.Add($"Architecture violation: project file for {project} was not found.");
                continue;
            }

            foreach (var dependency in actual.Except(allowedReferences).Order())
            {
                violations.Add(
                    $"Architecture violation: {project} references {dependency}, which is not allowed. " +
                    RuleDescriptions[project]);
            }

            foreach (var dependency in allowedReferences.Except(actual).Order())
            {
                violations.Add(
                    $"Architecture violation: {project} is missing required reference to {dependency}. " +
                    RuleDescriptions[project]);
            }
        }

        return violations;
    }

    public static IReadOnlyList<string>? FindCircularDependency(
        IReadOnlyDictionary<string, HashSet<string>> references)
    {
        var states = references.Keys.ToDictionary(project => project, _ => 0, StringComparer.Ordinal);
        var path = new List<string>();

        foreach (var project in references.Keys.Order())
        {
            if (states[project] == 0 && Visit(project) is { } cycle)
            {
                return cycle;
            }
        }

        return null;

        IReadOnlyList<string>? Visit(string project)
        {
            states[project] = 1;
            path.Add(project);

            if (references.TryGetValue(project, out var dependencies))
            {
                foreach (var dependency in dependencies.Order())
                {
                    if (!states.TryGetValue(dependency, out var state))
                    {
                        continue;
                    }

                    if (state == 1)
                    {
                        var cycleStart = path.IndexOf(dependency);
                        return path.Skip(cycleStart).Append(dependency).ToArray();
                    }

                    if (state == 0 && Visit(dependency) is { } cycle)
                    {
                        return cycle;
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            states[project] = 2;
            return null;
        }
    }

    public static string FormatCycleViolation(IEnumerable<string> cycle) =>
        $"Architecture violation: circular project dependency detected: {string.Join(" -> ", cycle)}.";

    private static HashSet<string> ReadProjectReferences(string projectFile)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile)!;

        return XDocument.Load(projectFile)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value
                ?? throw new InvalidDataException(
                    $"ProjectReference in '{projectFile}' is missing its Include attribute."))
            .Select(include => Path.GetFileNameWithoutExtension(Path.GetFullPath(
                include.Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar),
                projectDirectory)))
            .ToHashSet(StringComparer.Ordinal);
    }
}

internal sealed class ProjectFileFixture : IDisposable
{
    private ProjectFileFixture(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public static ProjectFileFixture Create(
        IReadOnlyDictionary<string, HashSet<string>> references)
    {
        var root = Path.Combine(Path.GetTempPath(), $"Kokishin.ArchitectureTests.{Guid.NewGuid():N}");

        foreach (var project in references)
        {
            var projectDirectory = Path.Combine(root, project.Key);
            Directory.CreateDirectory(projectDirectory);
            var projectReferences = project.Value
                .Order()
                .Select(dependency => new XElement(
                    "ProjectReference",
                    new XAttribute("Include", Path.Combine("..", dependency, $"{dependency}.csproj"))));
            var projectFile = new XDocument(
                new XElement(
                    "Project",
                    new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    new XElement("ItemGroup", projectReferences)));

            projectFile.Save(Path.Combine(projectDirectory, $"{project.Key}.csproj"));
        }

        return new ProjectFileFixture(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

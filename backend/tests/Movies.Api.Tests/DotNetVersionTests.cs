using System.Text.Json;
using System.Text.RegularExpressions;

namespace Movies.Api.Tests;

// The .NET version is set in Directory.Build.props, global.json and the Dockerfile ARG (Docker cannot
// read the first two). These tests fail when one is bumped without the others.
public partial class DotNetVersionTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void global_json_sdk_matches_the_target_framework()
    {
        // Arrange
        var targetFramework = TargetFrameworkVersion();

        // Act
        var sdk = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "global.json")))
            .RootElement.GetProperty("sdk").GetProperty("version").GetString()!;

        // Assert
        Assert.StartsWith(targetFramework + ".", sdk);
    }

    [Theory]
    [InlineData("backend/Dockerfile")]
    [InlineData("backend/Dockerfile.identity")]
    public void dockerfile_image_version_matches_the_target_framework(string dockerfile)
    {
        // Arrange
        var targetFramework = TargetFrameworkVersion();

        // Act
        var match = DockerArg().Match(File.ReadAllText(Path.Combine(RepoRoot, dockerfile)));

        // Assert
        Assert.True(match.Success, $"{dockerfile} has no ARG DOTNET_VERSION");
        Assert.Equal(targetFramework, match.Groups[1].Value);
    }

    // "net9.0" -> "9.0"
    private static string TargetFrameworkVersion() =>
        TargetFramework().Match(File.ReadAllText(Path.Combine(RepoRoot, "backend/Directory.Build.props"))).Groups[1].Value;

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "global.json")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("global.json not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"<TargetFramework>net(\d+\.\d+)</TargetFramework>")]
    private static partial Regex TargetFramework();

    [GeneratedRegex(@"^ARG DOTNET_VERSION=(\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex DockerArg();
}

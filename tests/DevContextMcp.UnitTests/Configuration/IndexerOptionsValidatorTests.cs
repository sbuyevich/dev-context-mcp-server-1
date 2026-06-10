using System.Text.Json;
using DevContextMcp.Indexer.Cli;
using DevContextMcp.Indexer.Cli.Configuration;
using Microsoft.Extensions.Options;

namespace DevContextMcp.UnitTests.Configuration;

public sealed class IndexerOptionsValidatorTests
{
    [Fact]
    public void ValidFeedAndPackageConfigurationSucceeds()
    {
        using var folder = PackageFolder.Create(
            ("Formula.json", Package("public", "Formula.SimpleRepo")));

        var result = Validate(new IndexerOptions
        {
            NuGetSourcesPath = folder.Path,
            Environments = [Feed("public", "nuget.org")]
        });

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void DuplicateSourceNamesFail()
    {
        using var folder = PackageFolder.Create();
        var result = Validate(new IndexerOptions
        {
            NuGetSourcesPath = folder.Path,
            Environments =
            [
                Feed("qa", "internal"),
                Feed("production", "Internal")
            ]
        });

        AssertFailure(result, "configured more than once");
    }

    [Fact]
    public void InvalidFeedAndLimitValuesFail()
    {
        using var folder = PackageFolder.Create(
            ("Invalid.json", Package("bad environment", "Company.Package", 0)));
        var result = Validate(new IndexerOptions
        {
            NuGetSourcesPath = folder.Path,
            Environments =
            [
                new NuGetEnvironmentOptions
                {
                    Name = "internal",
                    Environment = "bad environment",
                    ServiceIndex = "ftp://packages.example/index.json",
                    MaxPackages = 0
                }
            ],
            Indexing = new IndexingOptions
            {
                MaxPackageBytes = 0,
                MaxDocumentBytes = 1
            }
        });

        AssertFailure(result, "ServiceIndex URI");
        AssertFailure(result, "Environment");
        AssertFailure(result, "MaxPackageBytes");
        AssertFailure(result, "MaxVersionsPerPackage");
        AssertFailure(result, "MaxPackages");
    }

    [Fact]
    public void MissingFolderAndMalformedFileFailWithPaths()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var missing = Validate(new IndexerOptions { NuGetSourcesPath = missingPath });
        AssertFailure(missing, missingPath);

        using var folder = PackageFolder.Create(("Broken.json", "{"));
        var malformed = Validate(new IndexerOptions { NuGetSourcesPath = folder.Path });
        AssertFailure(malformed, "Broken.json");
    }

    [Fact]
    public void ObsoletePackageFileFieldsFailWithFilename()
    {
        using var folder = PackageFolder.Create(
            ("Legacy.json",
                """
                {
                  "Environment": "qa",
                  "PackageId": "Company.Package",
                  "PackagePrefixes": [ "Company." ]
                }
                """));

        var result = Validate(new IndexerOptions
        {
            NuGetSourcesPath = folder.Path,
            Environments = [Feed("qa", "internal")]
        });

        AssertFailure(result, "Legacy.json");
    }

    [Fact]
    public void DuplicateAndUnknownPackagesFail()
    {
        using var folder = PackageFolder.Create(
            ("A.json", Package("qa", "Company.Package")),
            ("B.json", Package("QA", "company.package")),
            ("C.json", Package("production", "Other.Package")));
        var result = Validate(new IndexerOptions
        {
            NuGetSourcesPath = folder.Path,
            Environments = [Feed("qa", "internal")]
        });

        AssertFailure(result, "configured more than once");
        AssertFailure(result, "undefined environment 'production'");
    }

    [Fact]
    public void FeedPackageLimitIsValidated()
    {
        using var folder = PackageFolder.Create(
            ("A.json", Package("qa", "A")),
            ("B.json", Package("qa", "B")));
        var feed = Feed("qa", "internal");
        feed.MaxPackages = 1;

        var result = Validate(new IndexerOptions
        {
            NuGetSourcesPath = folder.Path,
            Environments = [feed]
        });

        AssertFailure(result, "exceeding MaxPackages 1");
    }

    [Fact]
    public void MultipleFeedsMayShareEnvironment()
    {
        using var folder = PackageFolder.Create(
            ("Package.json", Package("qa", "Company.Package")));
        var result = Validate(new IndexerOptions
        {
            NuGetSourcesPath = folder.Path,
            Environments =
            [
                Feed("qa", "qa-primary"),
                Feed("QA", "qa-secondary")
            ]
        });

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void LoaderResolvesRelativePathsAndCachesFilenameOrderedFiles()
    {
        var name = $"nuget-options-{Guid.NewGuid():N}";
        var path = Path.Combine(AppContext.BaseDirectory, name);
        Directory.CreateDirectory(path);

        try
        {
            File.WriteAllText(Path.Combine(path, "B.json"), Package("qa", "B"));
            File.WriteAllText(Path.Combine(path, "A.json"), Package("qa", "A"));
            var loader = new NuGetPackageOptionsLoader();

            var first = loader.Load(name);
            File.WriteAllText(Path.Combine(path, "C.json"), Package("qa", "C"));
            var second = loader.Load(name);

            Assert.Equal(["A", "B"], first.Select(package => package.PackageId));
            Assert.Same(first, second);
            Assert.Equal(Path.GetFullPath(path), loader.ResolvePath(name));
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static ValidateOptionsResult Validate(IndexerOptions options) =>
        new IndexerOptionsValidator(new NuGetPackageOptionsLoader())
            .Validate(null, options);

    private static NuGetEnvironmentOptions Feed(string environment, string name) =>
        new()
        {
            Name = name,
            Environment = environment,
            ServiceIndex = "https://packages.example/v3/index.json",
            MaxPackages = 100
        };

    private static string Package(
        string environment,
        string packageId,
        int maxVersions = 3) =>
        JsonSerializer.Serialize(new
        {
            Environment = environment,
            PackageId = packageId,
            MaxVersionsPerPackage = maxVersions,
            IncludePrerelease = false,
            IncludeUnlisted = false
        });

    private static void AssertFailure(
        ValidateOptionsResult result,
        string expectedText)
    {
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure =>
            failure.Contains(expectedText, StringComparison.Ordinal));
    }

    private sealed class PackageFolder : IDisposable
    {
        private PackageFolder(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static PackageFolder Create(params (string Name, string Content)[] files)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"nuget-options-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            foreach (var file in files)
            {
                File.WriteAllText(System.IO.Path.Combine(path, file.Name), file.Content);
            }

            return new(path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

using System.Text.Json;
using DevContextMcp.Indexer;
using DevContextMcp.Indexer.Configuration;
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
            NugetsPath = folder.Path,
            Environments = [Feed("public")]
        });

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void DuplicateSourceNamesFail()
    {
        using var folder = PackageFolder.Create();
        var result = Validate(new IndexerOptions
        {
            NugetsPath = folder.Path,
            Environments =
            [
                Feed("internal"),
                Feed("Internal")
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
            NugetsPath = folder.Path,
            Environments =
            [
                new NuGetEnvironmentOptions
                {
                    Name = "bad environment",
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
        AssertFailure(result, "Name");
        AssertFailure(result, "MaxPackageBytes");
        AssertFailure(result, "MaxVersionsPerPackage");
        AssertFailure(result, "MaxPackages");
    }

    [Fact]
    public void MissingFolderAndMalformedFileFailWithPaths()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var missing = Validate(new IndexerOptions
        {
            NugetsPath = missingPath,
            Environments = [Feed("qa")]
        });
        AssertFailure(missing, missingPath);

        using var folder = PackageFolder.Create(("Broken.json", "{"));
        var malformed = Validate(new IndexerOptions
        {
            NugetsPath = folder.Path,
            Environments = [Feed("qa")]
        });
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
            NugetsPath = folder.Path,
            Environments = [Feed("qa")]
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
            NugetsPath = folder.Path,
            Environments = [Feed("qa")]
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
        var feed = Feed("qa");
        feed.MaxPackages = 1;

        var result = Validate(new IndexerOptions
        {
            NugetsPath = folder.Path,
            Environments = [feed]
        });

        AssertFailure(result, "exceeding MaxPackages 1");
    }

    [Fact]
    public void DeletePackageRequiresOnlyEnvironmentAndPackageId()
    {
        using var folder = PackageFolder.Create(
            ("Delete.json",
                """
                {
                  "Delete": true,
                  "Environment": "qa",
                  "PackageId": "Company.Package",
                  "MaxVersionsPerPackage": 0
                }
                """));

        var result = Validate(new IndexerOptions
        {
            NugetsPath = folder.Path,
            Environments = [Feed("qa")]
        });
        var package = Assert.Single(new NuGetPackageOptionsLoader().Load(folder.Path));

        Assert.Equal(ValidateOptionsResult.Success, result);
        Assert.True(package.Delete);
    }

    [Fact]
    public void NormalPackageDefaultsDeleteToFalse()
    {
        using var folder = PackageFolder.Create(
            ("Package.json", Package("qa", "Company.Package")));

        var package = Assert.Single(new NuGetPackageOptionsLoader().Load(folder.Path));

        Assert.False(package.Delete);
    }

    [Fact]
    public void LoaderRecursivelyLoadsPathOrderedFilesAndCachesResults()
    {
        var name = $"nuget-options-{Guid.NewGuid():N}";
        var path = Path.Combine(AppContext.BaseDirectory, name);
        Directory.CreateDirectory(path);

        try
        {
            var firstFolder = Directory.CreateDirectory(Path.Combine(path, "a"));
            var secondFolder = Directory.CreateDirectory(Path.Combine(path, "z"));
            File.WriteAllText(
                Path.Combine(secondFolder.FullName, "B.json"),
                Package("qa", "B"));
            File.WriteAllText(
                Path.Combine(firstFolder.FullName, "A.json"),
                Package("qa", "A"));
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

    [Fact]
    public void DocumentationConfigurationRequiresExistingRootAndValidExtensions()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-docs-{Guid.NewGuid():N}");
        var missing = Validate(new IndexerOptions
        {
            Documentation = new DocumentationOptions
            {
                RootPath = missingPath,
                Extensions = [".md"]
            }
        });
        AssertFailure(missing, "does not exist");

        using var folder = PackageFolder.Create();
        var invalid = Validate(new IndexerOptions
        {
            Documentation = new DocumentationOptions
            {
                RootPath = folder.Path,
                Extensions = [".md", "MD", "*"]
            }
        });
        AssertFailure(invalid, "configured more than once");
        AssertFailure(invalid, "is invalid");
    }

    [Fact]
    public void DocumentationOnlyConfigurationSucceeds()
    {
        using var folder = PackageFolder.Create();
        var result = Validate(new IndexerOptions
        {
            Documentation = new DocumentationOptions
            {
                RootPath = folder.Path,
                Extensions = [".md", ".txt"]
            }
        });

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    private static ValidateOptionsResult Validate(IndexerOptions options) =>
        new IndexerOptionsValidator(new NuGetPackageOptionsLoader())
            .Validate(null, options);

    private static NuGetEnvironmentOptions Feed(string name) =>
        new()
        {
            Name = name,
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
                var filePath = System.IO.Path.Combine(path, file.Name);
                Directory.CreateDirectory(
                    System.IO.Path.GetDirectoryName(filePath)!);
                File.WriteAllText(filePath, file.Content);
            }

            return new(path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

using DevContextMcp.Server.Core.Services;

namespace DevContextMcp.UnitTests.Retrieval;

public sealed class LibraryIdTests
{
    [Theory]
    [InlineData("nuget:Company.Package", null, "Company.Package")]
    [InlineData("NUGET:company.package", null, "company.package")]
    [InlineData("nuget:qa/Company.Package", "qa", "Company.Package")]
    [InlineData("nuget:QA-1/Company.Package", "QA-1", "Company.Package")]
    public void ParsesNuGetLibraryId(
        string value,
        string? expectedEnvironment,
        string expectedPackage)
    {
        Assert.True(LibraryId.TryParse(value, out var libraryId));
        Assert.Equal(expectedPackage, libraryId.PackageId);
        Assert.Equal(expectedEnvironment, libraryId.Environment);
        Assert.StartsWith("nuget:", libraryId.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Company.Package")]
    [InlineData("nuget:")]
    [InlineData("nuget:/Company.Package")]
    [InlineData("nuget:qa/")]
    [InlineData("nuget:bad environment/Company.Package")]
    [InlineData("nuget:qa/Company/Package")]
    public void RejectsInvalidLibraryId(string value)
    {
        Assert.False(LibraryId.TryParse(value, out _));
    }
}

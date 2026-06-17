# Indexer Configuration
 
## appsettings.json

```json
 "DevContextMcp": {
    "DatabasePath": "../../../../../database/docs.db",
    "IndexerSource": {
      "NugetsPath": "../../../../../demo/data/indexer/nugets",
      "Documents": {
        "RootPath": "../../../../../demo/data/indexer/company-docs",
        "Extensions": [ ".md", ".txt" ]
      }
    },
    "NugetPackages": [
      {
        "Name": "publicNuget",
        "Environment": "public",
        "ServiceIndex": "https://api.nuget.org/v3/index.json",
        "MaxPackages": 100
      },
      {
        "Name": "prodNuget",
        "Environment": "prod",
        "ServiceIndex": "../../../../../demo/data/nuget-repos/prod",
        "MaxPackages": 100
      },
      {
        "Name": "qaNuget",
        "Environment": "qa",
        "ServiceIndex": "../../../../../demo/data/nuget-repos/qa",
        "MaxPackages": 100
      }
    ],
    "Indexing": {
      "MaxPackageBytes": 104857600,
      "MaxDocumentBytes": 20971520,
      "MaxArchiveEntries": 10000,
      "MaxExtractedBytes": 524288000,
      "MaxCompressionRatio": 200,
      "MaxDocumentChars": 4000,
      "PackageDownloadTimeout": "00:02:00"
    }
  }
```

where:

- `DatabasePath`: path to the SQLite database file used by the indexer.

- `IndexerSource`: source configuration
  - `NugetsPath`: root folder containing NuGet JSON configuration files
  - `Documents`: 
    - `RootPath`: root directory containing documentation files to index.
    - `Extensions`: array of allowed file extensions for documentation files. Only these extensions are indexed.

- `NugetPackages`: list of configured NuGet sources by `Environment`
  - `Name`: unique identifier for the NuGet source.
  - `Environment`: environment slug for the source used in library IDs and package selection.
  - `ServiceIndex`: NuGet v3 service endpoint URI or local folder path containing `.nupkg` files.
  - `MaxPackages`: maximum number of package policy entries that may be applied to this source.

- `Indexing`: list of parameters for the indexing process
    - `MaxPackageBytes`: maximum allowed size for a downloaded package archive.
    - `MaxDocumentBytes`: maximum allowed size for a documentation file.
    - `MaxArchiveEntries`: maximum number of entries allowed inside an archive.
    - `MaxExtractedBytes`: maximum total bytes extracted from archives or documents during indexing.
    - `MaxCompressionRatio`: maximum allowed compression ratio for archive entries.
    - `MaxDocumentChars`: maximum number of characters extracted from a document for indexing.
    - `PackageDownloadTimeout`: maximum time allowed to download a package.

## NuGet Configuration

Each indexed NuGet source should have a JSON configuration file.

```json
{
  "Delete": false,
  "Environment": "public",
  "PackageId": "Formula.SimpleRepo",
  "MaxVersionsPerPackage": 10,
  "IncludePrerelease": false,
  "IncludeUnlisted": false
}
```

where:

- `Delete`: Boolean, default: false. If true, the indexer deletes the specified `PackageId` from the database.
- `Environment`: is one of the values defined in `NugetPackages` in `appsettings.json` (for example: `public`, `prod`, `qa`).
- `PackageId`: full NuGet package name.
- `MaxVersionsPerPackage`: maximum number of versions for indexing of the package.
- `IncludePrerelease`: Boolean, default: false. If true, then include prerelease versions of this package.
- `IncludeUnlisted`: Boolean, default: false. If true, then include unlisted package versions.

# DevContextMcp Country Explorer Specification

## Summary

Create a Windows-only .NET 10 MAUI Blazor Hybrid application using MudBlazor. The demo proves Codex can use DevContextMcp to correctly integrate `Formula.SimpleRepo` and environment-specific generated REST Countries clients.

The app loads countries from QA in Debug builds and production in Release builds. A user selects a country, saves it to a local SQLite database, and views saved countries independently of the API environment.

## Application Behavior

- Provide one Country Explorer page with:
  - Active environment badge: `QA` or `Production`.
  - Searchable country dropdown populated from the REST Countries client.
  - Save button enabled after selection.
  - Saved-country dropdown populated from SQLite.
  - Remove-favorite action.
  - Loading, empty, success, and error states.
- Persist `CountryCode`, `DisplayName`, and `SavedAtUtc`.
- Reject duplicate country codes and report “Already saved.”
- Sort API and saved-country lists by display name.
- Keep saved favorites available when the remote API is unavailable.
- Store the database at `FileSystem.AppDataDirectory/data/favorites.db`; `assets` remains reserved for packaged static content.

## Implementation Contracts

- Reference `RestCountries.Client.Qa` in Debug and `RestCountries.Client.Prod` in Release. The packages must have distinct IDs because their generated code differs.
- Wrap generated clients behind:

```csharp
public interface ICountryCatalog
{
    Task<IReadOnlyList<CountryOption>> GetAllAsync(
        CancellationToken cancellationToken);
}

public sealed record CountryOption(string Code, string DisplayName);
```

- Use environment-specific adapters so generated API differences do not leak into UI components.
- Define the persisted model:

```csharp
[ConnectionDetails("Favorites", typeof(SqliteConnection), Dialect.SQLite)]
[Table("FavoriteCountries")]
public sealed class FavoriteCountry
{
    [Key]
    public int Id { get; set; }

    public string CountryCode { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTime SavedAtUtc { get; set; }
}
```

- Add `[Repo] FavoriteCountryRepository : RepositoryBase<FavoriteCountry, FavoriteCountry>`.
- Create the table at startup with idempotent SQL and a unique index on `CountryCode`.
- Register MudBlazor, `ICountryCatalog`, repository services, and an application service that coordinates API lookup and persistence.
- Keep raw generated-client models and SimpleRepo calls outside Razor components.

## DevContextMcp Acceptance

Include repeatable Codex prompts:

1. “Use DevContextMcp to create the SQLite SimpleRepo model and repository.”
2. “Use the QA REST Countries client to load all countries for the Debug build.”
3. “Use the production REST Countries client for the Release build.”
4. “Verify the generated client method signatures before implementing adapters.”

Acceptance requires Codex to resolve and cite:

- `nuget:public/Formula.SimpleRepo`
- `nuget:qa/RestCountries.Client.Qa`
- `nuget:prod/RestCountries.Client.Prod`

Codex must preserve `not_found` or `insufficient_evidence` responses instead of inventing client APIs.

## Test Plan

- Unit-test country model mapping, sorting, duplicate detection, and environment adapter selection.
- Integration-test SQLite table initialization and SimpleRepo insert, list, and delete behavior.
- Component-test loading, save, duplicate, remove, API failure, and empty states.
- Build both Debug and Release and verify only the matching generated package and adapter are used.
- Manually verify favorites persist across restarts and remain shared between QA and production builds.

## Assumptions

- Windows 11 is the only initial acceptance platform.
- Debug means QA; Release means production. There is no runtime environment switch.
- QA and production packages contain different generated code and therefore use distinct package IDs.
- Package READMEs and XML documentation describe registration, client methods, models, and examples before indexing.
- The specification will replace the empty `demo/design/spec.md`.

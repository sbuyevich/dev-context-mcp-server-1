# Use these templates when generating or extending unit tests 

## Core Structure Requirements
- Use Moq when the class under test receives interface collaborators through
  constructor dependency injection.
- Do not introduce Moq when the class under test has no injected collaborators,
  such as pure functions, static validators, value objects, or simple in-memory
  stream wrappers.
- All injected interface collaborators must be `private readonly Mock<...>`
  fields named with an `_` prefix (for example, `_testService`) and initialized
  with `new()`.
- Always declare `_target` as a private readonly field on the test class.
- Instantiate `_target` inside the test class constructor after initializing private readonly `Mock<>` fields (one per dependency).
- Act should be a single statement invoking a method on `_target`.
- Use `actual` for the result variable in the act section.
- Always use default parameters for mock setups unless a specific value is required for the test.
- Add verification for every expected mock call in the assert section.
- Use `Times.Once`, `Times.Exactly`, or another precise count for expected
  interactions.
- Use `Times.Never` for collaborator methods that must not be called.
- Call `VerifyNoOtherCalls()` on every mock after all expected and forbidden
  interactions have been verified.
- Use `It.IsAny<>()` for parameters unless a specific value is required for the test.
- Use `It.Is<>()` when an argument's content is part of the behavior under test.
 
## Test Commenting
- Add `arrange`, `act`, `assert` comments in each test method.
- Add a comment above each test method name to explain the test's purpose.  
Pattern: // Purpose:   
Example:
``` csharp
 // Purpose: propagates failure when underlying exporter load fails
 [Fact]
 public async Task GetProofsAsync_WhenLoadByRawFileIdFails_ReturnsFailureStatus()
```

## Test Naming Convention
Pattern: MethodUnderTest_Condition_ExpectedResult  
Examples:
- `ProcessAsync_MobileDeposit_SuccessUpdatesMobileDepositRepository`
- `ProcessAsync_WhenCreateFails_ReturnsFailureStatus`
- `ProcessTellerDrawerAsync_WhenBranchNotAvailable_ReturnsFailure`


## Mock Formula.SimpleRepo
Use following interfaces definitions from [SimpleRepo](https://github.com/NephosIntegration/Formula.SimpleRepo)

- `IReadOnlyRepository<TModel>` for read-only operations

``` csharp

namespace Formula.SimpleRepo;

public interface IReadOnlyRepository<TModel> : IBuilder
{
    IBasicQuery<TModel> Basic { get; }
    List<string> GetIdFields();
    Hashtable GetPopulatedIdFields(object value);

    Task<IEnumerable<TModel>> GetAsync(List<Constraint> finalConstraints, IDbTransaction transaction = null, int? commandTimeout = null);
    Task<IEnumerable<TModel>> GetAsync(Hashtable constraints, IDbTransaction transaction = null, int? commandTimeout = null);
    Task<IEnumerable<TModel>> GetAsync(JObject json, IDbTransaction transaction = null, int? commandTimeout = null);
    Task<IEnumerable<TModel>> GetAsync(string json, IDbTransaction transaction = null, int? commandTimeout = null);
    Task<TModel> GetAsync(object id, IDbTransaction transaction = null, int? commandTimeout = null);
    Task<IEnumerable<TModel>> GetAsync(IDbTransaction transaction = null, int? commandTimeout = null);

    [Obsolete("Use GetPagedListAsync")]
    Task<IEnumerable<TModel>> GetListPagedAsync(int pageNumber, int rowsPerPage, string conditions, string orderby, object parameters = null, IDbTransaction transaction = null, int? commandTimeout = null);
    [Obsolete("Use GetPagedListAsync")]
    Task<IEnumerable<TModel>> GetListPagedAsync(int pageNumber, int rowsPerPage, Hashtable constraints, string orderBy = null, object parameters = null, IDbTransaction transaction = null, int? commandTimeout = null);
    [Obsolete("Use GetPagedListAsync")]
    Task<IEnumerable<TModel>> GetListPagedAsync(int pageNumber, int rowsPerPage, List<Constraint> constraints, string orderBy, object parameters = null, IDbTransaction transaction = null, int? commandTimeout = null);
    [Obsolete("Use GetPagedListAsync")]
    Task<IEnumerable<TModel>> GetListPagedAsync(int pageNumber, int rowsPerPage, JObject constraints, string orderBy, object parameters = null, IDbTransaction transaction = null, int? commandTimeout = null);

    Task<IEnumerable<TModel>> GetPagedListAsync(int pageNumber, int rowsPerPage, string conditions, string orderby, IDbTransaction transaction = null, int? commandTimeout = null);
    Task<IEnumerable<TModel>> GetPagedListAsync(int pageNumber, int rowsPerPage, Hashtable constraints, string orderBy = null, IDbTransaction transaction = null, int? commandTimeout = null);
    Task<IEnumerable<TModel>> GetPagedListAsync(int pageNumber, int rowsPerPage, List<Constraint> constraints, string orderBy, IDbTransaction transaction = null, int? commandTimeout = null);
    Task<IEnumerable<TModel>> GetPagedListAsync(int pageNumber, int rowsPerPage, JObject constraints, string orderBy, IDbTransaction transaction = null, int? commandTimeout = null);

    Task<int> GetRecordCountAsync(Hashtable constraints, IDbTransaction transaction = null, int? commandTimeout = null);
    Task<int> GetRecordCountAsync(string conditions = "", object parameters = null, IDbTransaction transaction = null, int? commandTimeout = null);
    Task<int> GetRecordCountAsync(object whereConditions, IDbTransaction transaction = null, int? commandTimeout = null);

    void ClearParameters();
    void AddParameter(string name, object value);
}
``` 

- `IRepository<TModel>` for read-write operations   

``` csharp

namespace Formula.SimpleRepo;

public interface IRepository<TModel> : IReadOnlyRepository<TModel>
{
    new IBasicCRUD<T> Basic { get; }

    Task<int?> InsertAsync(T entityToInsert, IDbTransaction transaction = null, int? commandTimeout = null);

    Task<int> UpdateAsync(T entityToUpdate, IDbTransaction transaction = null, int? commandTimeout = null, CancellationToken? token = null);

    Task<int> DeleteAsync(object id, IDbTransaction transaction = null, int? commandTimeout = null);
}
```

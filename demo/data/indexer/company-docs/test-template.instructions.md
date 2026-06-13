# Unit Test Generation Standard

Use this standard when generating or extending unit tests. Every requirement
in this document is mandatory.

## Required Test Stack

- Use xUnit for test methods.
- Use Moq for interface collaborators received through constructor dependency
  injection.
- Do not introduce Moq for pure functions, static validators, value objects, or
  other classes without injected collaborators.

## Required Test Class Structure

- Declare every injected collaborator as a `private readonly Mock<T>` field.
- Prefix mock field names with `_` and initialize them with `new()`.
- Declare the system under test as a `private readonly` field named `_target`.
- Construct `_target` in the test class constructor after initializing its
  mock dependencies.
- Keep reusable constants and fixture values at class level when they are
  shared by multiple tests.

```csharp
public sealed class OrderServiceTests
{
    private readonly Mock<IOrderRepository> _repository = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly OrderService _target;

    public OrderServiceTests()
    {
        _target = new OrderService(
            _repository.Object,
            _notifications.Object);
    }
}
```

## Required Test Naming

Name tests with this pattern:

`MethodUnderTest_Condition_ExpectedResult`

Examples:

- `GetAsync_OrderExists_ReturnsOrder`
- `CreateAsync_WhenRepositoryFails_ReturnsFailure`
- `DeleteAsync_OrderNotFound_DoesNotSendNotification`

## Required Test Comments and Layout

- Add a `// Purpose:` comment immediately above every `[Fact]` or `[Theory]`.
- Add `// arrange`, `// act`, and `// assert` comments inside every test.
- Keep the act section to one statement that invokes `_target`.
- Name the returned value `actual`.

```csharp
// Purpose: returns the existing order
[Fact]
public async Task GetAsync_OrderExists_ReturnsOrder()
{
    // arrange

    // act
    var actual = await _target.GetAsync(orderId, CancellationToken.None);

    // assert
}
```

## Required Mock Setup

- Use default or broad argument matchers unless an exact value is part of the
  behavior under test.
- Use `It.IsAny<T>()` for arguments whose content is not relevant.
- Use `It.Is<T>()` when the argument value or shape is part of the assertion.
- Match cancellation tokens with `It.IsAny<CancellationToken>()` unless token
  identity or cancellation behavior is under test.
- Configure only interactions required by the scenario.

## Required Verification

- Verify every expected collaborator call in the assert section.
- Use a precise call count such as `Times.Once`, `Times.Exactly`, or
  `Times.Never`.
- Verify forbidden calls with `Times.Never`.
- Call `VerifyNoOtherCalls()` on every mock after expected and forbidden calls
  have been verified.
- A private helper may group `VerifyNoOtherCalls()` calls when the test class
  has multiple mocks.

```csharp
private void VerifyNoOtherCalls()
{
    _repository.VerifyNoOtherCalls();
    _notifications.VerifyNoOtherCalls();
}
```

## Template: Successful Async Dependency Call

```csharp
// Purpose: returns the order loaded from the repository
[Fact]
public async Task GetAsync_OrderExists_ReturnsOrder()
{
    // arrange
    const int orderId = 42;
    var expected = new Order(orderId);
    _repository
        .Setup(repository => repository.GetAsync(
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(expected);

    // act
    var actual = await _target.GetAsync(orderId, CancellationToken.None);

    // assert
    Assert.Same(expected, actual);
    _repository.Verify(
        repository => repository.GetAsync(
            orderId,
            It.IsAny<CancellationToken>()),
        Times.Once);
    VerifyNoOtherCalls();
}
```

## Template: Failure or Exception Result

Use the result template when the dependency returns a failure value:

```csharp
// Purpose: returns failure when the repository cannot create the order
[Fact]
public async Task CreateAsync_WhenRepositoryFails_ReturnsFailure()
{
    // arrange
    var request = new CreateOrderRequest();
    _repository
        .Setup(repository => repository.CreateAsync(
            It.IsAny<Order>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(OperationResult.Failure("repository_error"));

    // act
    var actual = await _target.CreateAsync(request, CancellationToken.None);

    // assert
    Assert.False(actual.IsSuccess);
    _repository.Verify(
        repository => repository.CreateAsync(
            It.Is<Order>(order => order is not null),
            It.IsAny<CancellationToken>()),
        Times.Once);
    VerifyNoOtherCalls();
}
```

Use `Assert.ThrowsAsync<TException>` when the expected behavior is to propagate
or throw an exception:

```csharp
// Purpose: propagates the repository exception
[Fact]
public async Task GetAsync_WhenRepositoryThrows_PropagatesException()
{
    // arrange
    var expected = new InvalidOperationException("failure");
    _repository
        .Setup(repository => repository.GetAsync(
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
        .ThrowsAsync(expected);

    // act
    var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        _target.GetAsync(42, CancellationToken.None));

    // assert
    Assert.Same(expected, actual);
    _repository.Verify(
        repository => repository.GetAsync(
            42,
            It.IsAny<CancellationToken>()),
        Times.Once);
    VerifyNoOtherCalls();
}
```

## Template: Dependency Must Not Be Called

```csharp
// Purpose: rejects an invalid request without calling dependencies
[Fact]
public async Task CreateAsync_InvalidRequest_DoesNotCallDependencies()
{
    // arrange
    var request = new CreateOrderRequest();

    // act
    var actual = await _target.CreateAsync(request, CancellationToken.None);

    // assert
    Assert.False(actual.IsSuccess);
    _repository.Verify(
        repository => repository.CreateAsync(
            It.IsAny<Order>(),
            It.IsAny<CancellationToken>()),
        Times.Never);
    _notifications.Verify(
        notifications => notifications.SendAsync(
            It.IsAny<Order>(),
            It.IsAny<CancellationToken>()),
        Times.Never);
    VerifyNoOtherCalls();
}
```

## Formula.SimpleRepo Mocking Workflow

Do not copy or invent `Formula.SimpleRepo` interfaces or method signatures.
Resolve the exact API from the indexed NuGet package before generating a mock
setup or verification:

1. Call `resolve_library` with `Formula.SimpleRepo`.
2. Call `list_versions` and select the version used by the target project. If
   the project version is unknown, use the recommended stable version.
3. Call `get_symbol` for the exact repository type or member being mocked,
   such as `IReadOnlyRepository<TModel>`, `IRepository<TModel>`, `GetAsync`, or
   `InsertAsync`.
4. Call `query_docs` when usage guidance or a complete example is required.
5. Generate Moq setup and verification expressions only from the returned
   signature and cite the DevContext evidence.

If DevContext returns `not_found` or `insufficient_evidence`, state that the
signature could not be verified. Do not generate a guessed repository API.

## Formula.SimpleRepo Example Shape

Adapt this shape only after replacing the placeholder member and arguments
with a signature verified for the selected package version:

```csharp
_repository
    .Setup(repository => repository.VerifiedMemberAsync(
        It.IsAny<VerifiedArgumentType>()))
    .ReturnsAsync(expected);

_repository.Verify(
    repository => repository.VerifiedMemberAsync(
        It.Is<VerifiedArgumentType>(value => value == expectedArgument)),
    Times.Once);

_repository.VerifyNoOtherCalls();
```

# Stage 4 Implementation Plan: Verification

## Objective

Verify the complete application contract through isolated API tests and leave
the demo ready to build and run as one solution.

## Test Infrastructure

- Host the real Minimal API in process.
- Replace Open-Meteo HTTP transport with a deterministic fake handler.
- Give each test or test collection an isolated temporary SQLite database.
- Record external request counts and requested city names.
- Keep tests independent of execution order and the live network.

## Verification Matrix

- Exact ordered payload for `GET /city`.
- Exact ordered payload for `GET /city/usa`.
- Successful location payload.
- Successful population payload.
- URL-encoded names containing spaces.
- Case-insensitive and trimmed matching.
- Unsupported and misspelled city rejection before external access.
- Empty Open-Meteo result.
- Non-exact Open-Meteo result.
- First exact result selected when duplicates exist.
- Cache reuse across both detail endpoints.
- Cached nullable-population behavior.
- Upstream failure with an empty cache.
- Cached success while the upstream service is unavailable.
- Problem Details content type and status for every error category.

## Final Checks

1. Run formatting if the repository provides a formatter.
2. Build the complete `demo/app` solution.
3. Run all `Demo.CityApi.Tests`.
4. Confirm tests leave no database artifacts in the source tree.
5. Confirm no test or startup path contacts the live Open-Meteo API.
6. Confirm package versions and QA package selection are explicit.
7. Document the local build, test, and run commands.

## Completion Criteria

- All four endpoints meet `design/spec.md`.
- All stage tests pass together.
- The solution builds with no warnings.
- External-call count assertions prove cache-aside behavior.
- The application can be started locally with a configurable SQLite path.

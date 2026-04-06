# Veritas Atlas Smoke Pack

## Files

- run-smoke-tests.ps1
  Reusable local smoke test runner.

- smoke-env.json
  Default local environment values.

- smoke-requests.json
  Human-readable request inventory for the smoke flow.

## Usage

Start the API first, then run:

    .\tests\smoke\run-smoke-tests.ps1

Or with a custom URL:

    .\tests\smoke\run-smoke-tests.ps1 -BaseUrl https://localhost:7091

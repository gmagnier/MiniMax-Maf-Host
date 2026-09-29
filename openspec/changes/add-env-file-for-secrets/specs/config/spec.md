## Purpose

Lets the application read its runtime configuration from a `.env` file at development time, with platform-injected environment variables always winning, so contributors can configure secrets without touching the repository or learning the .NET user-secrets tool.

## ADDED Requirements

### Requirement: .env file loading at process start

The system MUST load variables from a `.env` file located in the current working directory at process start, before the ASP.NET Core configuration is built.

Variables defined in `.env` MUST be exposed as process environment variables. Variables already present in the process environment MUST take precedence over values in `.env` (no overwrite).

If no `.env` file is present, the system MUST continue to start normally; `.env` loading MUST NOT throw.

#### Scenario: developer has a populated .env
- **WHEN** a `.env` file containing `MiniMax__ApiKey=test-key` exists in the working directory
- **AND** no real process environment variable named `MiniMax__ApiKey` is set
- **THEN** the system starts successfully
- **AND** `MiniMax:ApiKey` resolves to `test-key`

#### Scenario: no .env file exists
- **WHEN** no `.env` file is present in the working directory
- **THEN** startup proceeds without error from the .env loader
- **AND** the existing `MiniMax:ApiKey` resolution behavior is unchanged

#### Scenario: platform-injected variable wins
- **WHEN** a process environment variable `MiniMax__ApiKey=prod-key` is set
- **AND** a `.env` file containing `MiniMax__ApiKey=dev-key` also exists
- **THEN** `MiniMax:ApiKey` resolves to `prod-key`

### Requirement: .env.example template committed to the repository

The repository MUST contain a committed `.env.example` file that lists every environment variable the application reads, with safe placeholder values (empty for secrets, real defaults for non-secrets).

`.env.example` MUST NOT be excluded by `.gitignore`.

#### Scenario: new contributor onboarding
- **WHEN** a contributor copies `.env.example` to `.env` and fills in the secret values
- **THEN** the host starts with all required configuration populated
- **AND** no secret value is required to be hard-coded

### Requirement: secrets externalized from committed configuration

The `MiniMax:ApiKey` configuration value MUST be sourced exclusively from process environment variables (populated either by `.env` at dev time or by the platform in production). The value MUST NOT be read from any JSON configuration file committed to the repository.

#### Scenario: production deployment
- **WHEN** the host is started with the platform's secret store injecting `MiniMax__ApiKey`
- **AND** no `.env` file is present
- **THEN** `MiniMax:ApiKey` resolves to the platform-injected value
- **AND** the agent can issue chat requests successfully

#### Scenario: missing secret still fails fast
- **WHEN** no `.env` file is present
- **AND** no process environment variable `MiniMax__ApiKey` is set
- **THEN** the host fails to start with a clear error naming the missing key

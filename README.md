# Stryker-NET (HOMT Fork)

> ⚠️ **This is a fork** of [stryker-mutator/stryker-net](https://github.com/stryker-mutator/stryker-net), extended with Higher-Order Mutation Testing (HOMT) for academic research.


## About this Fork

- **Goal:** Integrate higher-order mutantion testing for more thorough and/or faster mutation testing.
- **Thesis:** Implement and evaluate strategies for finding higher-order mutants in .NET Core & Framework.
- **Status:** 🚧 Work in progress.

---



[![Nuget](https://img.shields.io/nuget/v/dotnet-stryker.svg?color=blue&label=dotnet-stryker&style=flat-square)](https://www.nuget.org/packages/dotnet-stryker/)
[![Nuget](https://img.shields.io/nuget/dt/dotnet-stryker.svg?style=flat-square)](https://www.nuget.org/packages/dotnet-stryker/)
[![Azure DevOps build](https://img.shields.io/azure-devops/build/stryker-mutator/Stryker/4/master.svg?label=Azure%20Pipelines&style=flat-square)](https://dev.azure.com/stryker-mutator/Stryker/_build/latest?definitionId=4)
[![Azure DevOps tests](https://img.shields.io/azure-devops/tests/stryker-mutator/506a1f46-900e-434e-805f-ff8d36fc81af/4/master.svg?compact_message&style=flat-square)](https://dev.azure.com/stryker-mutator/Stryker/_build/latest?definitionId=4)
[![Mutation testing badge](https://img.shields.io/endpoint?style=flat&url=https%3A%2F%2Fbadge-api.stryker-mutator.io%2Fgithub.com%2Fstryker-mutator%2Fstryker-net%2Fmaster)](https://dashboard.stryker-mutator.io/reports/github.com/stryker-mutator/stryker-net/master)
[![Slack](https://img.shields.io/badge/chat-on%20slack-blueviolet?style=flat-square)](https://join.slack.com/t/stryker-mutator/shared_invite/enQtOTUyMTYyNTg1NDQ0LTU4ODNmZDlmN2I3MmEyMTVhYjZlYmJkOThlNTY3NTM1M2QxYmM5YTM3ODQxYmJjY2YyYzllM2RkMmM1NjNjZjM)
# ![S](https://raw.githubusercontent.com/stryker-mutator/stryker-mutator.github.io/6026230eaa82a130950a859e523a703d7f30f291/static/images/stryker-80x80.png)tryker.NET

*Professor X: For someone who hates mutants... you certainly keep some strange company.*
*William Stryker: Oh, they serve their purpose... as long as they can be controlled.*

## Introduction

Stryker offers mutation testing for your .NET Core and .NET Framework projects. It allows you to test your tests by temporarily inserting bugs in your source code.

For an introduction to mutation testing and Stryker's features, see [stryker-mutator.io](https://stryker-mutator.io/). Looking for mutation testing in [JavaScript & Typescript](https://stryker-mutator.github.io/stryker) or [Scala](https://stryker-mutator.github.io/stryker4s)?

## Compatibility

Minimum target version:

- dotnet core 1.1
- dotnet framework 4.5
- dotnet standard 1.3

 Tested against:

- dotnet core 3.1
- dotnet framework 4.8

## Getting started

```bash
dotnet tool install -g dotnet-stryker
cd /my/unit/test/project/folder
dotnet stryker
```

For more information read our [getting started](https://stryker-mutator.io/docs/stryker-net/getting-started).

## Documentation

For the full documentation on how to use Stryker.NET, see our [configuration docs](https://stryker-mutator.io/docs/stryker-net/configuration).

## Migrating

Coming from a previous version of Stryker.NET? Take a look at our [migration guide](https://stryker-mutator.io/docs/stryker-net/migration-guide).

## Supported Mutations

For the full list of all available mutations, see the [mutations docs](https://stryker-mutator.io/docs/stryker-net/mutations).

## Supported Reporters

For the full list of all available reporters, see the [reporter docs](https://stryker-mutator.io/docs/stryker-net/reporters).

## Contributing

Want to help develop Stryker.NET? Check out our [contribution guide](/CONTRIBUTING.md).

Issues for the HTML report should be issued at [mutation-testing-elements](https://github.com/stryker-mutator/mutation-testing-elements).

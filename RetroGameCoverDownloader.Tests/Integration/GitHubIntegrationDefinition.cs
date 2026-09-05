using Xunit;

namespace RetroGameCoverDownloader.Tests.Integration;

/// <summary>
/// Collection definition that disables parallelization for GitHub integration tests.
/// This ensures we respect GitHub API rate limits by running tests sequentially.
/// </summary>
[CollectionDefinition("GitHub Integration", DisableParallelization = true)]
public class GitHubIntegrationDefinition;
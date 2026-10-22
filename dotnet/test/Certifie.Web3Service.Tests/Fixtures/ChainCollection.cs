using Xunit;

namespace Certifie.Web3Service.Tests;

/// <summary>Tests that write with the shared test account run one at a time, so their nonces never collide.</summary>
[CollectionDefinition(Name)]
public sealed class ChainCollection : ICollectionFixture<Chain>
{
    public const string Name = "Chain";
}

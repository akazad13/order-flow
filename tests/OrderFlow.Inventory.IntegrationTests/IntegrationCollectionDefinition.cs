using OrderFlow.IntegrationTesting;

namespace OrderFlow.Inventory.IntegrationTests;

[CollectionDefinition(IntegrationCollection.Name)]
public sealed class IntegrationCollectionDefinition : ICollectionFixture<InfrastructureFixture>;

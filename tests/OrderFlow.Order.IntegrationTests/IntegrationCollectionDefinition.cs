using OrderFlow.IntegrationTesting;

namespace OrderFlow.Order.IntegrationTests;

[CollectionDefinition(IntegrationCollection.Name)]
public sealed class IntegrationCollectionDefinition : ICollectionFixture<InfrastructureFixture>;

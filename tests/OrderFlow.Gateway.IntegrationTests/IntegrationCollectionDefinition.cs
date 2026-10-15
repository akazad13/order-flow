using OrderFlow.IntegrationTesting;

namespace OrderFlow.Gateway.IntegrationTests;

[CollectionDefinition(IntegrationCollection.Name)]
public sealed class IntegrationCollectionDefinition : ICollectionFixture<InfrastructureFixture>;

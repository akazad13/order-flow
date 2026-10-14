using OrderFlow.IntegrationTesting;

namespace OrderFlow.Payment.IntegrationTests;

[CollectionDefinition(IntegrationCollection.Name)]
public sealed class IntegrationCollectionDefinition : ICollectionFixture<InfrastructureFixture>;

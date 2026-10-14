using OrderFlow.IntegrationTesting;

namespace OrderFlow.Notification.IntegrationTests;

[CollectionDefinition(IntegrationCollection.Name)]
public sealed class IntegrationCollectionDefinition : ICollectionFixture<InfrastructureFixture>;

using OrderFlow.ServiceDefaults;

// Walking skeleton: connected to the broker, no consumers yet (arrive with spec 002/003).
var builder = WebApplication.CreateBuilder(args);

builder.AddOrderFlowServiceDefaults("inventory");

var app = builder.Build();

app.MapOrderFlowDefaults();

app.Run();

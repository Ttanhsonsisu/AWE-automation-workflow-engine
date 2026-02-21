var builder = DistributedApplication.CreateBuilder(args);

//var messaging = builder.AddConnectionString("messaging")

builder.AddProject<Projects.AWE_ApiGateway>("awe-apigateway");

builder.AddProject<Projects.AWE_Worker>("awe-worker");

builder.AddProject<Projects.AWE_Wokrer_Engine>("awe-wokrer-engine");

builder.Build().Run();

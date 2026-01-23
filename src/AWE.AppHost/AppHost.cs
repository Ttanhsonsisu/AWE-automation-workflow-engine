var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.AWE_ApiGateway>("awe-apigateway");

builder.AddProject<Projects.AWE_Worker>("awe-worker");

builder.Build().Run();

var builder = DistributedApplication.CreateBuilder(args);

//var messaging = builder.AddConnectionString("messaging")

builder.AddProject<Projects.AWE_ApiGateway>("awe-apigateway");

builder.AddProject<Projects.AWE_Worker>("awe-worker");

builder.AddProject<Projects.AWE_Worker_JobExecution>("awe-worker-jobexecution");

builder.AddProject<Projects.AWE_Worker_WorkflowEvent>("awe-worker-workflowevent");

builder.Build().Run();

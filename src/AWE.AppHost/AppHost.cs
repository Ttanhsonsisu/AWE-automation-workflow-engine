var builder = DistributedApplication.CreateBuilder(args);

//var messaging = builder.AddConnectionString("messaging")


//builder.AddProject<Projects.AWE_ApiGateway>("awe-apigateway");

//builder.AddProject<Projects.AWE_Worker>("awe-worker");

//builder.AddProject<Projects.AWE_Wokrer_Engine>("awe-wokrer-engine");

// Infrastructure services
var postgres = builder.AddConnectionString("postgres");
var redis = builder.AddConnectionString("Redis");
var rabbit = builder.AddConnectionString("messaging");

// Khai báo các project — Aspire tự inject OTLP endpoint vào mỗi cái
var apiGateway = builder.AddProject<Projects.AWE_ApiGateway>("awe-apigateway")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(rabbit)
    .WithExternalHttpEndpoints();   // expose cho browser

var engineWorker = builder.AddProject<Projects.AWE_Wokrer_Engine>("awe-engine-worker")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(rabbit);

var pluginWorker = builder.AddProject<Projects.AWE_Worker>("awe-plugin-worker")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(rabbit);

builder.Build().Run();


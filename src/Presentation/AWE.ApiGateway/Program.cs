using AWE.Infrastructure;
using AWE.ServiceDefaults.Extensions;
using AWE.WorkflowEngine;

// ================================================================
// Application bootstrap
// Responsibility:
// - Configure infrastructure services,...
// ================================================================
var builder = WebApplication.CreateBuilder(args);

// Register common service defaults (logging, health checks, etc.)
builder.AddServiceDefaults();


// ------------------------------------------------------------
// Infrastructure configuration
// ------------------------------------------------------------

// Register persistence layer (EF Core, database context)
builder.Services.AddAwePersistence(builder.Configuration);

// Register messaging infrastructure 
builder.Services.AddAweMessaging(builder.Configuration);

// add service engine
builder.Services.AddWorkflowEngineService();

// Register object storage
//builder.Services.AddAweObjectStorage(builder.Configuration);


// ------------------------------------------------------------
// Web API configuration
// ------------------------------------------------------------

builder.Services.AddControllers();

// Register OpenAPI for development and testing
builder.Services.AddOpenApi();

var app = builder.Build();

// Map default infrastructure endpoints (health, metrics, etc.)
app.MapDefaultEndpoints();


// ------------------------------------------------------------
// HTTP request pipeline
// ------------------------------------------------------------

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();


// =============== API TEST =============
//app.MapPost("/api/workflows", async (
//    [FromBody] SubmitWorkflowCommand request,
//    [FromServices] IPublishEndpoint publishEndpoint,
//    [FromServices] ApplicationDbContext dbContext) =>
//{
//    await publishEndpoint.Publish(request);

//    await dbContext.SaveChangesAsync();

//    return Results.Accepted(value: new
//    {
//        Message = "sended command !",
//        JobId = request.DefinitionId
//    });
//});
// ===========================================

app.Run();

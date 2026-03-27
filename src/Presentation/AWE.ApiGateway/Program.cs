using AWE.Infrastructure;
using AWE.Application;
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

// Register application layer services
builder.Services.AddAweApplication();

// Register object storage
//builder.Services.AddAweObjectStorage(builder.Configuration);


// config cors to allow frontend to call API (adjust as needed for production)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin()   // Cho phép tất cả các nguồn
              .AllowAnyHeader()   // Cho phép bất kỳ header nào
              .AllowAnyMethod());  // Cho phép tất cả các phương thức HTTP
});


// ------------------------------------------------------------
// Web API configuration
// ------------------------------------------------------------

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Encoder =
            System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

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

app.UseCors("AllowAll");
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

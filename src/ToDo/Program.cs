// <snippet_all>
using NSwag.AspNetCore;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.PerfCounterCollector;
using Microsoft.ApplicationInsights.DependencyCollector;
using Microsoft.ApplicationInsights.Kubernetes;
using Microsoft.EntityFrameworkCore;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Messaging.ServiceBus;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

string connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<TodoDb>(opt => opt.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddEndpointsApiExplorer();

var keyVaultUri = new Uri($"https://{builder.Configuration["KeyVaultName"]}.vault.azure.net/");
var secretClient = new SecretClient(keyVaultUri, new DefaultAzureCredential());
var serviceBusConnection = secretClient.GetSecret("ServiceBusConnection").Value.Value;

builder.Services.AddSingleton(new ServiceBusClient(serviceBusConnection));
builder.Services.AddSingleton<ServiceBusService>();


builder.Services.AddOpenApiDocument(config =>
{
    config.DocumentName = "TodoAPI";
    config.Title = "TodoAPI v1";
    config.Version = "v1";
});

builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.EnableAdaptiveSampling = false; // Wyłączenie adaptacyjnego samplingu
    options.EnableDependencyTrackingTelemetryModule = true; // Distributed tracing
    options.EnablePerformanceCounterCollectionModule = true; // Metryki wydajności
    options.EnableAppServicesHeartbeatTelemetryModule = true; // Heartbeat
    options.EnableDebugLogger = true; // Debugowanie w konsoli (dla deweloperów)
});

builder.Services.AddApplicationInsightsKubernetesEnricher();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseOpenApi();
    app.UseSwaggerUi(config =>
    {
        config.DocumentTitle = "TodoAPI";
        config.Path = "/swagger";
        config.DocumentPath = "/swagger/{documentName}/swagger.json";
        config.DocExpansion = "list";
    });
}

// <snippet_group>
RouteGroupBuilder todoItems = app.MapGroup("/todoitems");

todoItems.MapGet("/", GetAllTodos);
todoItems.MapGet("/complete", GetCompleteTodos);
todoItems.MapGet("/{id}", GetTodo);
todoItems.MapPost("/", CreateTodo);
todoItems.MapPut("/{id}", UpdateTodo);
todoItems.MapDelete("/{id}", DeleteTodo);
// </snippet_group>

app.Run();

// <snippet_handlers>
// <snippet_getalltodos>
static async Task<IResult> GetAllTodos(TodoDb db)
{
    return TypedResults.Ok(await db.Todos.Select(x => new TodoItemDTO(x)).ToArrayAsync());
}
// </snippet_getalltodos>

static async Task<IResult> GetCompleteTodos(TodoDb db) {
    return TypedResults.Ok(await db.Todos.Where(t => t.IsComplete).Select(x => new TodoItemDTO(x)).ToListAsync());
}

static async Task<IResult> GetTodo(int id, TodoDb db, ServiceBusService serviceBus)
{
    var result =  await db.Todos.FindAsync(id);

    if (result != null)
    {
        var dto = new TodoItemDTO(result);
        await serviceBus.SendMessageAsync(new TodoEvent("TodoGeted", dto));
    }

    return result is Todo todo
            ? TypedResults.Ok(new TodoItemDTO(todo))
            : TypedResults.NotFound();;
}

static async Task<IResult> CreateTodo(TodoItemDTO todoItemDTO, TodoDb db, ServiceBusService serviceBus)
{
    var todoItem = new Todo
    {
        IsComplete = todoItemDTO.IsComplete,
        Name = todoItemDTO.Name
    };

    db.Todos.Add(todoItem);
    await db.SaveChangesAsync();
    
    var dto = new TodoItemDTO(todoItem);
    await serviceBus.SendMessageAsync(new TodoEvent("TodoCreated", dto));
    
    return TypedResults.Created($"/todoitems/{todoItem.Id}", dto);
}

static async Task<IResult> UpdateTodo(int id, TodoItemDTO todoItemDTO, TodoDb db)
{
    var todo = await db.Todos.FindAsync(id);

    if (todo is null) return TypedResults.NotFound();

    todo.Name = todoItemDTO.Name;
    todo.IsComplete = todoItemDTO.IsComplete;

    await db.SaveChangesAsync();

    return TypedResults.NoContent();
}

static async Task<IResult> DeleteTodo(int id, TodoDb db, ServiceBusService serviceBus)
{
    if (await db.Todos.FindAsync(id) is Todo todo)
    {
        db.Todos.Remove(todo);
        await db.SaveChangesAsync();
        var dto = new TodoItemDTO(todo);
        await serviceBus.SendMessageAsync(new TodoEvent("TodoDeleted", dto));
        return TypedResults.NoContent();
    }

    return TypedResults.NotFound();
}
// <snippet_handlers>
// </snippet_all>

public class TodoEvent
{
    public string EventType { get; set; }
    public TodoItemDTO Todo { get; set; }
    public DateTime Timestamp { get; set; }

    public TodoEvent(string eventType, TodoItemDTO todo)
    {
        EventType = eventType;
        Todo = todo;
        Timestamp = DateTime.UtcNow;
    }
}

public class ServiceBusService
{
    private readonly ServiceBusClient _client;
    private const string QueueName = "todoevents";

    public ServiceBusService(ServiceBusClient client)
    {
        _client = client;
    }

    public async Task SendMessageAsync(TodoEvent todoEvent)
    {
        try
        {
            var sender = _client.CreateSender(QueueName);
            var messageBody = JsonSerializer.Serialize(todoEvent);
            var message = new ServiceBusMessage(messageBody)
            {
                ContentType = "application/json",
                Subject = todoEvent.EventType
            };

            await sender.SendMessageAsync(message);
            Console.WriteLine($"Sent message: {todoEvent.EventType} for Todo {todoEvent.Todo.Id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending message to Service Bus: {ex.Message}");
            throw;
        }
    }
}

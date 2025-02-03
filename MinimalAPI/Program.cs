using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.EntityFrameworkCore;
using MinimalAPI;
using MinimalAPI.Entites;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<ApplicationDBContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

#region Cycle Ref.. in case of Api
//builder.Services.AddControllers().AddJsonOptions(options =>
//{
//    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
//}); 
#endregion

builder.Services.Configure<JsonOptions>(options => options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

#region Configure Kestrel time out when handling the request Header 
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
});
#endregion

builder.Services.AddRequestTimeouts(options =>
{
    options.DefaultPolicy = new RequestTimeoutPolicy()
    {
        Timeout = TimeSpan.FromMilliseconds(14000),
        TimeoutStatusCode = 200,
        WriteTimeoutResponse = async (httpContext) => await httpContext.Response.WriteAsync("You are crazy !")
    };
    options.AddPolicy("TestPolicy", TimeSpan.FromSeconds(2));
});


var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};


#region Weather EndPoint
app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 15).Select(index =>
    new WeatherForecast
    (
        DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
        Random.Shared.Next(-20, 55),
        summaries[Random.Shared.Next(summaries.Length)]
    ))
    .ToArray();
    return forecast;
});
#endregion

#region Check Production / Developement Env and Test Launch Setting 
var message = builder.Configuration.GetValue<string>("message");
app.MapGet("/message", () => { return message; });
#endregion

#region Department CRUD
app.MapPost("/department", async (Department department, ApplicationDBContext dbContext) =>
{
    dbContext.Add(department);
    await dbContext.SaveChangesAsync();
    return TypedResults.Ok();
});

#endregion

#region Employee CRUD

// create
app.MapPost("/employee", async (Employee employee, ApplicationDBContext dbContext) =>
{
    var isValidDepartmentId = await dbContext.Department.AnyAsync(d => d.Id == employee.DepartmentId);
    if (!isValidDepartmentId)
    {
        return TypedResults.Ok();
    }
    dbContext.Add(employee);
    await dbContext.SaveChangesAsync();
    return TypedResults.Ok();
});

// Get Employee By Id
app.MapGet("/employee/{id:int}", async Task<Results<Ok<Employee>, NotFound, BadRequest>> (int id, ApplicationDBContext dbContext) =>
{
    var employee = await dbContext.Employee.FirstOrDefaultAsync(e => e.Id == id);
    if (employee is null) return TypedResults.NotFound();
    return TypedResults.Ok(employee);
});

// update by id 
app.MapPut("/employee/{id:int}", async Task<Results<Ok<Employee>,NoContent, NotFound, BadRequest>> (int id, Employee employee, ApplicationDBContext dbContext) =>
{
    if (id != employee.Id) return TypedResults.BadRequest();
    var validateEmployee = await dbContext.Employee.AnyAsync(e => e.Id == id);
    if (!validateEmployee) return TypedResults.NotFound();
    dbContext.Employee.Update(employee);
    await dbContext.SaveChangesAsync();
    return TypedResults.NoContent();
});

app.MapGet("/Employees", async (ApplicationDBContext dbContext) =>
{
    var employeesInfo = await dbContext.Employee.Include(e => e.Department).ToListAsync();
    return TypedResults.Ok(employeesInfo);
});
app.UseRequestTimeouts();
app.Run();

#endregion
internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

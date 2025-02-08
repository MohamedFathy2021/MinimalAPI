using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.OutputCaching;
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

builder.Services.AddOutputCache();

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();
app.UseOutputCache();
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

// create department 
app.MapPost("/department", async (Department department, ApplicationDBContext dbContext, IOutputCacheStore outputCacheStore) =>
{
    dbContext.Add(department);
    await dbContext.SaveChangesAsync();
    await outputCacheStore.EvictByTagAsync("all-Department", default);
    return TypedResults.Ok();
});

// get all departments with employee
app.MapGet("/department", async (ApplicationDBContext dbContext) =>
{
    var departments = await dbContext.Department.Include(d => d.Employees).ToListAsync();
    return TypedResults.Ok(departments);
}).CacheOutput(x => x.Expire(TimeSpan.FromSeconds(60)).Tag("all-Department"));

// get department by id 
app.MapGet("/department/{id:int}", async Task<Results<Ok<Department>, NotFound>> (int id, ApplicationDBContext dbContext) =>
{
    var department = await dbContext.Department.FirstOrDefaultAsync(d => d.Id == id);
    if (department == null) return TypedResults.NotFound();
    return TypedResults.Ok(department);
});

// update department 
app.MapPut("/department/{id:int}", async Task<Results<NoContent, NotFound, BadRequest>> (int id, Department department, ApplicationDBContext dbContext, IOutputCacheStore outputCacheStore) =>
{
    if (id != department.Id) return TypedResults.BadRequest();
    var validateDepartment = await dbContext.Department.AnyAsync(d => d.Id == id);
    if (!validateDepartment) return TypedResults.NotFound();
    dbContext.Department.Update(department);
    await dbContext.SaveChangesAsync();
    await outputCacheStore.EvictByTagAsync("all-Department", default);
    return TypedResults.NoContent();
});

// delete department
app.MapDelete("/department/{id:int}", async Task<Results<NoContent, NotFound, BadRequest>> (int id, ApplicationDBContext dBContext, IOutputCacheStore outputCacheStore) =>
{
    var department = await dBContext.Department.FindAsync(id);
    if (department is null) return TypedResults.NotFound();
    dBContext.Department.Remove(department);
    await dBContext.SaveChangesAsync();
    await outputCacheStore.EvictByTagAsync("all-Department", default);
    return TypedResults.NoContent();
});

#endregion

#region Employee CRUD

// create employee
app.MapPost("/employee", async (Employee employee, ApplicationDBContext dbContext, IOutputCacheStore outputCacheStore) =>
{
    var isValidDepartmentId = await dbContext.Department.AnyAsync(d => d.Id == employee.DepartmentId);
    if (!isValidDepartmentId)
    {
        return TypedResults.Ok();
    }
    dbContext.Add(employee);
    await dbContext.SaveChangesAsync();

    // update caching in case of adding new employee
    await outputCacheStore.EvictByTagAsync("all-employee", default);
    return TypedResults.Ok();
});

// Get Employee By Id
app.MapGet("/employee/{id:int}", async Task<Results<Ok<Employee>, NotFound>> (int id, ApplicationDBContext dbContext) =>
{
    var employee = await dbContext.Employee.FirstOrDefaultAsync(e => e.Id == id);
    if (employee is null) return TypedResults.NotFound();
    return TypedResults.Ok(employee);
});

// update by id 
app.MapPut("/employee/{id:int}", async Task<Results<NoContent, NotFound, BadRequest>> (int id, Employee employee, ApplicationDBContext dbContext, IOutputCacheStore outputCacheStore) =>
{
    if (id != employee.Id) return TypedResults.BadRequest();
    var validateEmployee = await dbContext.Employee.AnyAsync(e => e.Id == id);
    if (!validateEmployee) return TypedResults.NotFound();
    dbContext.Employee.Update(employee);
    await dbContext.SaveChangesAsync();
    await outputCacheStore.EvictByTagAsync("all-employee", default);
    return TypedResults.NoContent();
});

//get all employees with department
app.MapGet("/Employee", async (ApplicationDBContext dbContext) =>
{
    var employeesInfo = await dbContext.Employee.Include(e => e.Department).Select(employee => new
    {
        employee.Id,
        employee.Name,
        Department = employee.Department.Name,
    }).ToListAsync();
    return TypedResults.Ok(employeesInfo);
})
 .CacheOutput(c => c.Expire(TimeSpan.FromSeconds(30)).Tag("all-employee"));

// delete employee
app.MapDelete("/employee/{id:int}", async Task<Results<NoContent, NotFound, BadRequest>> (int id, ApplicationDBContext dbContext, IOutputCacheStore outputCacheStore) =>
{
    var employee = await dbContext.Employee.FindAsync(id);
    if (employee is null) return TypedResults.NotFound();
    dbContext.Employee.Remove(employee);
    await dbContext.SaveChangesAsync();
    await outputCacheStore.EvictByTagAsync("all-employee", default);
    return TypedResults.NoContent();
});

app.UseRequestTimeouts();
app.Run();

#endregion
internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

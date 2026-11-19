using System.Diagnostics;
using Dapper;
using Microsoft.Data.SqlClient;

const string connStr =
    @"Server=DANAHI;Database=UsersDb;Integrated Security=True;TrustServerCertificate=True;";

var repo = new UserRepository(connStr);

Console.WriteLine("=== Synchronous operations ===");
int id = repo.Create("Anna", 20);
Console.WriteLine($"Created user Id={id}");
Console.WriteLine($"GetById: {repo.GetById(id)}");
repo.Update(new User { Id = id, Name = "Anna Updated", Age = 21 });
Console.WriteLine($"After Update: {repo.GetById(id)}");
Console.WriteLine($"Total users: {repo.GetAll().Count}");
repo.Delete(id);
Console.WriteLine($"After Delete: {(repo.GetById(id) == null ? "not found (OK)" : "ERROR")}");

Console.WriteLine("\n=== Asynchronous operations ===");
int idAsync = await repo.CreateAsync("Bob", 30);
Console.WriteLine($"Created user Id={idAsync}");
Console.WriteLine($"GetByIdAsync: {await repo.GetByIdAsync(idAsync)}");
await repo.UpdateAsync(new User { Id = idAsync, Name = "Bob Updated", Age = 31 });
Console.WriteLine($"After UpdateAsync: {await repo.GetByIdAsync(idAsync)}");
Console.WriteLine($"Total users: {(await repo.GetAllAsync()).Count}");
await repo.DeleteAsync(idAsync);
Console.WriteLine($"After DeleteAsync: {(await repo.GetByIdAsync(idAsync) == null ? "not found (OK)" : "ERROR")}");

Console.WriteLine("\n=== Performance (sequential vs parallel) ===");
const int N = 1000;
var options = new ParallelOptions { MaxDegreeOfParallelism = 10 };

long seqInsert = await Measure(async () =>
{
    for (int i = 0; i < N; i++)
        await repo.CreateAsync($"Seq{i}", 20);
});
long parInsert = await Measure(() =>
    Parallel.ForEachAsync(Enumerable.Range(0, N), options, async (i, ct) =>
        await repo.CreateAsync($"Par{i}", 20)));

var all = await repo.GetAllAsync();
var seqUsers = all.Where(u => u.Name.StartsWith("Seq")).ToList();
var parUsers = all.Where(u => u.Name.StartsWith("Par")).ToList();

long seqUpdate = await Measure(async () =>
{
    foreach (var u in seqUsers)
    {
        u.Age++;
        await repo.UpdateAsync(u);
    }
});
long parUpdate = await Measure(() =>
    Parallel.ForEachAsync(parUsers, options, async (u, ct) =>
    {
        u.Age++;
        await repo.UpdateAsync(u);
    }));

long seqDelete = await Measure(async () =>
{
    foreach (var u in seqUsers)
        await repo.DeleteAsync(u.Id);
});
long parDelete = await Measure(() =>
    Parallel.ForEachAsync(parUsers, options, async (u, ct) =>
        await repo.DeleteAsync(u.Id)));

Console.WriteLine($"{N} operations of each type, max degree of parallelism = {options.MaxDegreeOfParallelism}");
Console.WriteLine($"{"",-10}{"Sequential, ms",15}{"Parallel, ms",15}");
Console.WriteLine($"{"INSERT",-10}{seqInsert,15}{parInsert,15}");
Console.WriteLine($"{"UPDATE",-10}{seqUpdate,15}{parUpdate,15}");
Console.WriteLine($"{"DELETE",-10}{seqDelete,15}{parDelete,15}");

static async Task<long> Measure(Func<Task> action)
{
    var sw = Stopwatch.StartNew();
    await action();
    return sw.ElapsedMilliseconds;
}

class User
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }

    public override string ToString() => $"[{Id}] {Name}, {Age}";
}

class UserRepository
{
    private readonly string _connStr;

    public UserRepository(string connStr) => _connStr = connStr;

    public int Create(string name, int age)
    {
        using var db = new SqlConnection(_connStr);
        return db.QuerySingle<int>(
            "INSERT INTO Users (Name, Age) OUTPUT INSERTED.Id VALUES (@name, @age)",
            new { name, age });
    }

    public List<User> GetAll()
    {
        using var db = new SqlConnection(_connStr);
        return db.Query<User>("SELECT Id, Name, Age FROM Users").ToList();
    }

    public User? GetById(int id)
    {
        using var db = new SqlConnection(_connStr);
        return db.QuerySingleOrDefault<User>("SELECT Id, Name, Age FROM Users WHERE Id = @id", new { id });
    }

    public bool Update(User user)
    {
        using var db = new SqlConnection(_connStr);
        return db.Execute("UPDATE Users SET Name = @Name, Age = @Age WHERE Id = @Id", user) > 0;
    }

    public bool Delete(int id)
    {
        using var db = new SqlConnection(_connStr);
        return db.Execute("DELETE FROM Users WHERE Id = @id", new { id }) > 0;
    }

    public async Task<int> CreateAsync(string name, int age)
    {
        await using var db = new SqlConnection(_connStr);
        return await db.QuerySingleAsync<int>(
            "INSERT INTO Users (Name, Age) OUTPUT INSERTED.Id VALUES (@name, @age)",
            new { name, age });
    }

    public async Task<List<User>> GetAllAsync()
    {
        await using var db = new SqlConnection(_connStr);
        return (await db.QueryAsync<User>("SELECT Id, Name, Age FROM Users")).ToList();
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        await using var db = new SqlConnection(_connStr);
        return await db.QuerySingleOrDefaultAsync<User>("SELECT Id, Name, Age FROM Users WHERE Id = @id", new { id });
    }

    public async Task<bool> UpdateAsync(User user)
    {
        await using var db = new SqlConnection(_connStr);
        return await db.ExecuteAsync("UPDATE Users SET Name = @Name, Age = @Age WHERE Id = @Id", user) > 0;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = new SqlConnection(_connStr);
        return await db.ExecuteAsync("DELETE FROM Users WHERE Id = @id", new { id }) > 0;
    }
}
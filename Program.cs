using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Prisma.Data;
using Prisma.Models;
using Prisma.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Database Context with SQLite
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "prisma.db");
builder.Services.AddDbContext<PrismaDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Add HttpClient, Summarizer & AuthService
builder.Services.AddHttpClient();
builder.Services.AddScoped<ISummarizerService, SummarizerService>();
builder.Services.AddSingleton<IAuthService, AuthService>();

// CORS if accessing from different ports
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

// Ensure SQLite Database and initial Seed Data (including 3 test users) are created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PrismaDbContext>();
    var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
    db.Database.EnsureCreated();

    // Seed 3 test users if none exist
    if (!db.Users.Any())
    {
        var defaultPasswordHash = auth.HashPassword("Prisma123!");
        db.Users.AddRange(
            new User { Id = 1, Username = "valeria_g", PasswordHash = defaultPasswordHash, CreatedAt = DateTime.UtcNow },
            new User { Id = 2, Username = "carlos_m", PasswordHash = defaultPasswordHash, CreatedAt = DateTime.UtcNow },
            new User { Id = 3, Username = "sofia_arte", PasswordHash = defaultPasswordHash, CreatedAt = DateTime.UtcNow }
        );
        db.SaveChanges();
    }
}

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// ----------------------------------------------------
// API ENDPOINTS
// ----------------------------------------------------

// GET /api/places - List all places with calculated metrics
app.MapGet("/api/places", async (PrismaDbContext db, [FromQuery] string? category, [FromQuery] string? search) =>
{
    var query = db.Places.Include(p => p.Reviews).AsQueryable();

    if (!string.IsNullOrWhiteSpace(category) && category != "Todos")
    {
        query = query.Where(p => p.Category.ToLower() == category.ToLower());
    }

    if (!string.IsNullOrWhiteSpace(search))
    {
        var term = search.ToLower();
        query = query.Where(p => p.Name.ToLower().Contains(term) ||
                                 p.Description.ToLower().Contains(term) ||
                                 p.Address.ToLower().Contains(term));
    }

    var places = await query.ToListAsync();

    var result = places.Select(p => new
    {
        p.Id,
        p.Name,
        p.Category,
        p.Address,
        p.ImageUrl,
        p.Description,
        p.CreatedAt,
        p.AiSummary,
        p.AiSummaryUpdatedAt,
        TotalReviews = p.Reviews.Count,
        AverageRating = p.Reviews.Count > 0 ? Math.Round(p.Reviews.Average(r => r.Rating), 1) : 0.0
    }).OrderByDescending(p => p.AverageRating).ThenByDescending(p => p.TotalReviews);

    return Results.Ok(result);
});

// GET /api/places/{id} - Get single place with all reviews
app.MapGet("/api/places/{id:int}", async (int id, PrismaDbContext db) =>
{
    var place = await db.Places
        .Include(p => p.Reviews)
        .FirstOrDefaultAsync(p => p.Id == id);

    if (place == null)
        return Results.NotFound(new { error = "Lugar no encontrado." });

    var totalReviews = place.Reviews.Count;
    var avgRating = totalReviews > 0 ? Math.Round(place.Reviews.Average(r => r.Rating), 1) : 0.0;

    var response = new
    {
        place.Id,
        place.Name,
        place.Category,
        place.Address,
        place.ImageUrl,
        place.Description,
        place.CreatedAt,
        place.AiSummary,
        place.AiSummaryUpdatedAt,
        TotalReviews = totalReviews,
        AverageRating = avgRating,
        Reviews = place.Reviews.OrderByDescending(r => r.CreatedAt).Select(r => new
        {
            r.Id,
            r.AuthorName,
            r.Rating,
            r.Title,
            r.Comment,
            r.CreatedAt
        })
    };

    return Results.Ok(response);
});

// POST /api/places - Create a new place
app.MapPost("/api/places", async (CreatePlaceDto dto, PrismaDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(dto.Name))
        return Results.BadRequest(new { error = "El nombre del lugar es requerido." });

    var place = new Place
    {
        Name = dto.Name.Trim(),
        Category = string.IsNullOrWhiteSpace(dto.Category) ? "General" : dto.Category.Trim(),
        Address = dto.Address?.Trim() ?? string.Empty,
        ImageUrl = string.IsNullOrWhiteSpace(dto.ImageUrl)
            ? "https://images.unsplash.com/photo-1517248135467-4c7edcad34c4?auto=format&fit=crop&w=900&q=80"
            : dto.ImageUrl.Trim(),
        Description = dto.Description?.Trim() ?? string.Empty,
        CreatedAt = DateTime.UtcNow,
        AiSummary = "Lugar recientemente registrado. ¡Sé la primera persona en escribir una reseña!",
        AiSummaryUpdatedAt = DateTime.UtcNow
    };

    db.Places.Add(place);
    await db.SaveChangesAsync();

    return Results.Created($"/api/places/{place.Id}", new
    {
        place.Id,
        place.Name,
        place.Category,
        place.Address,
        place.ImageUrl,
        place.Description,
        place.CreatedAt,
        place.AiSummary,
        TotalReviews = 0,
        AverageRating = 0.0
    });
});

// ----------------------------------------------------
// AUTH ENDPOINTS
// ----------------------------------------------------

// POST /api/auth/register - Create a new user
app.MapPost("/api/auth/register", async (RegisterDto dto, PrismaDbContext db, IAuthService auth) =>
{
    if (string.IsNullOrWhiteSpace(dto.Username) || dto.Username.Trim().Length < 3)
        return Results.BadRequest(new { error = "El nombre de usuario debe tener al menos 3 caracteres." });

    if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 4)
        return Results.BadRequest(new { error = "La contraseña debe tener al menos 4 caracteres." });

    var usernameClean = dto.Username.Trim();
    var exists = await db.Users.AnyAsync(u => u.Username.ToLower() == usernameClean.ToLower());
    if (exists)
        return Results.BadRequest(new { error = "El nombre de usuario ya está registrado. Por favor elige otro." });

    var user = new User
    {
        Username = usernameClean,
        PasswordHash = auth.HashPassword(dto.Password),
        CreatedAt = DateTime.UtcNow
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    var token = auth.GenerateToken(user.Id, user.Username);

    return Results.Created("/api/auth/me", new
    {
        token,
        user = new { id = user.Id, username = user.Username }
    });
});

// POST /api/auth/login - Authenticate user
app.MapPost("/api/auth/login", async (LoginDto dto, PrismaDbContext db, IAuthService auth) =>
{
    if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
        return Results.BadRequest(new { error = "Por favor ingresa usuario y contraseña." });

    var usernameClean = dto.Username.Trim();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == usernameClean.ToLower());
    if (user == null || !auth.VerifyPassword(dto.Password, user.PasswordHash))
        return Results.BadRequest(new { error = "Usuario o contraseña incorrectos." });

    var token = auth.GenerateToken(user.Id, user.Username);

    return Results.Ok(new
    {
        token,
        user = new { id = user.Id, username = user.Username }
    });
});

// GET /api/auth/me - Get current user profile
app.MapGet("/api/auth/me", async ([FromHeader(Name = "Authorization")] string? authHeader, PrismaDbContext db, IAuthService auth) =>
{
    if (string.IsNullOrWhiteSpace(authHeader))
        return Results.Unauthorized();

    var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? authHeader.Substring(7).Trim()
        : authHeader.Trim();

    var principal = auth.ValidateToken(token);
    if (principal == null)
        return Results.Unauthorized();

    var user = await db.Users.FindAsync(principal.Value.UserId);
    if (user == null)
        return Results.Unauthorized();

    return Results.Ok(new { id = user.Id, username = user.Username });
});

// POST /api/places/{id}/reviews - Submit a review to database under authenticated user
app.MapPost("/api/places/{id:int}/reviews", async (
    int id,
    CreateReviewDto dto,
    PrismaDbContext db,
    ISummarizerService summarizer,
    IAuthService auth,
    [FromHeader(Name = "Authorization")] string? authHeader,
    [FromHeader(Name = "X-Gemini-Key")] string? geminiKey) =>
{
    // Require authenticated user
    if (string.IsNullOrWhiteSpace(authHeader))
    {
        return Results.Json(new { error = "Debes iniciar sesión con tu usuario y contraseña para publicar una reseña." }, statusCode: 401);
    }

    var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? authHeader.Substring(7).Trim()
        : authHeader.Trim();

    var principal = auth.ValidateToken(token);
    if (principal == null)
    {
        return Results.Json(new { error = "Sesión no válida o expirada. Por favor vuelve a iniciar sesión." }, statusCode: 401);
    }

    var user = await db.Users.FindAsync(principal.Value.UserId);
    if (user == null)
    {
        return Results.Json(new { error = "Usuario no encontrado." }, statusCode: 401);
    }

    var place = await db.Places.Include(p => p.Reviews).FirstOrDefaultAsync(p => p.Id == id);
    if (place == null)
        return Results.NotFound(new { error = "Lugar no encontrado." });

    if (string.IsNullOrWhiteSpace(dto.Comment))
        return Results.BadRequest(new { error = "El comentario de la reseña es obligatorio." });

    if (dto.Rating < 1 || dto.Rating > 5)
        return Results.BadRequest(new { error = "La calificación debe ser entre 1 y 5 estrellas." });

    // Associate review automatically to the authenticated user's username and ID
    var review = new Review
    {
        PlaceId = id,
        UserId = user.Id,
        AuthorName = user.Username,
        Rating = dto.Rating,
        Title = string.IsNullOrWhiteSpace(dto.Title) ? "Opinión" : dto.Title.Trim(),
        Comment = dto.Comment.Trim(),
        CreatedAt = DateTime.UtcNow
    };

    db.Reviews.Add(review);
    await db.SaveChangesAsync();

    // Automatically recalculate AI summary with all reviews in DB
    string? newSummary = null;
    try
    {
        newSummary = await summarizer.GeneratePlaceSummaryAsync(id, geminiKey);
    }
    catch
    {
        // Keep existing if error
    }

    return Results.Created($"/api/places/{id}/reviews/{review.Id}", new
    {
        review.Id,
        review.PlaceId,
        review.AuthorName,
        review.Rating,
        review.Title,
        review.Comment,
        review.CreatedAt,
        UpdatedSummary = newSummary ?? place.AiSummary
    });
});

// POST /api/places/{id}/summarize - Collect all reviews and summarize into a paragraph
app.MapPost("/api/places/{id:int}/summarize", async (
    int id,
    ISummarizerService summarizer,
    [FromHeader(Name = "X-Gemini-Key")] string? headerKey,
    [FromBody] SummarizeRequestDto? body) =>
{
    try
    {
        var key = !string.IsNullOrWhiteSpace(headerKey) ? headerKey : body?.ApiKey;
        var summary = await summarizer.GeneratePlaceSummaryAsync(id, key);
        return Results.Ok(new
        {
            placeId = id,
            summary = summary,
            updatedAt = DateTime.UtcNow
        });
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = "Lugar no encontrado." });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, title: "Error al generar resumen");
    }
});

// GET /api/stats - Global statistics
app.MapGet("/api/stats", async (PrismaDbContext db) =>
{
    var totalPlaces = await db.Places.CountAsync();
    var totalReviews = await db.Reviews.CountAsync();
    var averageGlobalRating = totalReviews > 0 ? Math.Round(await db.Reviews.AverageAsync(r => r.Rating), 1) : 0.0;
    var totalSummaries = await db.Places.CountAsync(p => !string.IsNullOrEmpty(p.AiSummary));

    return Results.Ok(new
    {
        totalPlaces,
        totalReviews,
        averageGlobalRating,
        totalSummaries
    });
});

app.Run();

// DTO records
public record CreatePlaceDto(string Name, string? Category, string? Address, string? ImageUrl, string? Description);
public record CreateReviewDto(int Rating, string? Title, string Comment);
public record RegisterDto(string Username, string Password);
public record LoginDto(string Username, string Password);
public record SummarizeRequestDto(string? ApiKey);



using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TrustVault.Api;

var builder = WebApplication.CreateBuilder(args);

// DATABASE CONNECTION
var connection = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=trustvault;Username=trustvault;Password=change-this-password";

// JWT SECRET FROM USER SECRETS
var jwtKey = builder.Configuration["Jwt:Key"];

if (string.IsNullOrWhiteSpace(jwtKey) ||
    Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "JWT key is missing or too short. Configure Jwt:Key using User Secrets.");
}

// DATABASE
builder.Services.AddDbContext<AppDb>(options =>
    options.UseNpgsql(connection));

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "https://trustvault-web.onrender.com"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});



// JWT AUTHENTICATION
builder.Services.AddAuthentication(
    JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtKey)),

                ClockSkew = TimeSpan.Zero
            };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// MIDDLEWARE
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

// FILE STORAGE
var uploadFolder = Path.Combine(
    app.Environment.ContentRootPath,
    "private_uploads");

Directory.CreateDirectory(uploadFolder);

// DATABASE INITIALIZATION
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await db.Database.EnsureCreatedAsync();
}

// HEALTH CHECK
app.MapGet("/api/health", () =>
    Results.Ok(new { status = "ok" }));

// REGISTER
app.MapPost("/api/auth/register", async (
    RegisterRequest request,
    AppDb db) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) ||
        string.IsNullOrWhiteSpace(request.Email) ||
        string.IsNullOrEmpty(request.Password) ||
        request.Password.Length < 10)
        return Results.BadRequest("Invalid registration details.");

    var email = request.Email.Trim().ToLowerInvariant();

    if (await db.Users.AnyAsync(x => x.Email == email))
        return Results.BadRequest("Email already registered.");

    var user = new User
    {
        Name = request.Name.Trim(),
        Email = email,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
        Role = "Viewer"
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Ok(new { message = "Registration successful." });
});

// LOGIN
app.MapPost("/api/auth/login", async (
    LoginRequest request,
    AppDb db) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) ||
        string.IsNullOrEmpty(request.Password))
        return Results.BadRequest("Email and password are required.");

    var email = request.Email.Trim().ToLowerInvariant();

    var user = await db.Users.FirstOrDefaultAsync(
        x => x.Email == email);

    if (user == null ||
        !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        return Results.Unauthorized();

    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Name),
        new Claim(ClaimTypes.Role, user.Role),
        new Claim(ClaimTypes.Email, user.Email)
    };

    var key = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes(jwtKey));

    var credentials = new SigningCredentials(
        key,
        SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        claims: claims,
        expires: DateTime.UtcNow.AddHours(2),
        signingCredentials: credentials);

    return Results.Ok(new
    {
        token = new JwtSecurityTokenHandler().WriteToken(token)
    });
});

// AUTHORIZED API GROUP
var api = app.MapGroup("/api").RequireAuthorization();

// CURRENT USER
api.MapGet("/me", (ClaimsPrincipal user) =>
{
    return Results.Ok(new
    {
        id = user.UserId(),
        name = user.Identity?.Name ??
               user.FindFirstValue(ClaimTypes.Name),
        email = user.FindFirstValue(ClaimTypes.Email),
        role = user.Role()
    });
});

// SHAREABLE USERS
api.MapGet("/users/shareable", async (
    ClaimsPrincipal principal,
    AppDb db) =>
{
    var currentId = principal.UserId();

    var users = await db.Users
        .Where(x => x.Id != currentId &&
                    (x.Role == "Editor" || x.Role == "Viewer"))
        .Select(x => new
        {
            id = x.Id,
            name = x.Name,
            email = x.Email,
            role = x.Role
        })
        .ToListAsync();

    return Results.Ok(users);
});

// ADMIN: ALL USERS
api.MapGet("/users", async (
    ClaimsPrincipal principal,
    AppDb db) =>
{
    if (principal.Role() != "Admin")
        return Results.Forbid();

    var users = await db.Users
        .Select(x => new
        {
            id = x.Id,
            name = x.Name,
            email = x.Email,
            role = x.Role
        })
        .ToListAsync();

    return Results.Ok(users);
});

// ADMIN: CREATE USER
api.MapPost("/users", async (
    CreateUserRequest request,
    ClaimsPrincipal principal,
    AppDb db) =>
{
    if (principal.Role() != "Admin")
        return Results.Forbid();

    var allowedRoles = new[]
    {
        "Admin", "Owner", "Editor", "Viewer"
    };

    if (!allowedRoles.Contains(request.Role) ||
        string.IsNullOrEmpty(request.Password) ||
        request.Password.Length < 10 ||
        string.IsNullOrWhiteSpace(request.Name) ||
        string.IsNullOrWhiteSpace(request.Email))
        return Results.BadRequest("Invalid user details.");

    var email = request.Email.Trim().ToLowerInvariant();

    if (await db.Users.AnyAsync(x => x.Email == email))
        return Results.BadRequest("Email already exists.");

    var user = new User
    {
        Name = request.Name.Trim(),
        Email = email,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
        Role = request.Role
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Ok(new { message = "User created." });
});

// LIST ACCESSIBLE DOCUMENTS
api.MapGet("/documents", async (
    ClaimsPrincipal principal,
    AppDb db) =>
{
    var userId = principal.UserId();
    var role = principal.Role();

    var documents = await db.Documents
        .Include(x => x.Owner)
        .Include(x => x.Permissions)
        .Where(x =>
            role == "Admin" ||
            x.OwnerId == userId ||
            x.Permissions.Any(p =>
                p.UserId == userId && p.CanRead))
        .Select(x => new
        {
            id = x.Id,
            name = x.Name,
            category = x.Category,
            createdAt = x.CreatedAt,
            ownerId = x.OwnerId,
            ownerName = x.Owner.Name
        })
        .ToListAsync();

    return Results.Ok(documents);
});

// UPLOAD DOCUMENT
api.MapPost("/documents", async (
    HttpRequest request,
    ClaimsPrincipal principal,
    AppDb db) =>
{
    if (principal.Role() != "Owner" &&
        principal.Role() != "Admin")
        return Results.Forbid();

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");

    if (file == null || file.Length == 0)
        return Results.BadRequest("Select a file.");

    if (file.Length > 10 * 1024 * 1024)
        return Results.BadRequest("Maximum file size is 10 MB.");

    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

    var allowed = new[]
    {
        ".pdf", ".txt", ".png", ".jpg",
        ".jpeg", ".docx", ".xlsx"
    };

    if (!allowed.Contains(extension))
        return Results.BadRequest("Unsupported file format.");

    var storageName = Guid.NewGuid() + extension;
    var path = Path.Combine(uploadFolder, storageName);

    await using (var stream = File.Create(path))
    {
        await file.CopyToAsync(stream);
    }

    var document = new Document
    {
        Name = Path.GetFileName(file.FileName),
        Category = form["category"].FirstOrDefault() ?? "General",
        StorageName = storageName,
        ContentType = file.ContentType,
        OwnerId = principal.UserId()
    };

    db.Documents.Add(document);

    db.AuditLogs.Add(new AuditLog
    {
        UserId = principal.UserId(),
        DocumentId = document.Id,
        Action = "UPLOAD",
        Detail = $"{principal.Identity?.Name} uploaded {document.Name}"
    });

    await db.SaveChangesAsync();

    return Results.Ok(new { message = "Upload successful." });
});

// DOWNLOAD DOCUMENT
api.MapGet("/documents/{id:guid}/download", async (
    Guid id,
    ClaimsPrincipal principal,
    AppDb db) =>
{
    var document = await db.Documents
        .FirstOrDefaultAsync(x => x.Id == id);

    if (document == null)
        return Results.NotFound();

    if (!await CanRead(principal, document, db))
        return Results.Forbid();

    var path = Path.Combine(uploadFolder, document.StorageName);

    if (!File.Exists(path))
        return Results.NotFound("Stored file not found.");

    db.AuditLogs.Add(new AuditLog
    {
        UserId = principal.UserId(),
        DocumentId = id,
        Action = "DOWNLOAD",
        Detail = $"{principal.Identity?.Name} downloaded {document.Name}"
    });

    await db.SaveChangesAsync();

    return Results.File(path, document.ContentType, document.Name);
});

// GET TXT CONTENT
api.MapGet("/documents/{id:guid}/content", async (
    Guid id,
    ClaimsPrincipal principal,
    AppDb db) =>
{
    var document = await db.Documents
        .FirstOrDefaultAsync(x => x.Id == id);

    if (document == null)
        return Results.NotFound();

    if (!await CanEdit(principal, document, db))
        return Results.Forbid();

    if (!document.StorageName.EndsWith(
        ".txt", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(
            "In-app editing is supported for TXT files only.");

    var path = Path.Combine(uploadFolder, document.StorageName);

    if (!File.Exists(path))
        return Results.NotFound("File not found.");

    var content = await File.ReadAllTextAsync(path);

    return Results.Ok(new { content });
});

// SAVE TXT CONTENT
api.MapPut("/documents/{id:guid}/content", async (
    Guid id,
    EditContentRequest request,
    ClaimsPrincipal principal,
    AppDb db) =>
{
    var document = await db.Documents
        .FirstOrDefaultAsync(x => x.Id == id);

    if (document == null)
        return Results.NotFound();

    if (!await CanEdit(principal, document, db))
        return Results.Forbid();

    if (!document.StorageName.EndsWith(
        ".txt", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("Only TXT files can be edited.");

    if (request.Content == null ||
        Encoding.UTF8.GetByteCount(request.Content) > 10 * 1024 * 1024)
        return Results.BadRequest("Content is too large.");

    var path = Path.Combine(uploadFolder, document.StorageName);
    var tempPath = path + "." + Guid.NewGuid() + ".tmp";

    try
    {
        await File.WriteAllTextAsync(tempPath, request.Content);
        File.Move(tempPath, path, true);

        db.AuditLogs.Add(new AuditLog
        {
            UserId = principal.UserId(),
            DocumentId = document.Id,
            Action = "EDIT_CONTENT",
            Detail = $"{principal.Identity?.Name} edited document {document.Name}"
        });

        await db.SaveChangesAsync();

        return Results.Ok(new
        {
            message = "Document content saved successfully."
        });
    }
    finally
    {
        if (File.Exists(tempPath))
            File.Delete(tempPath);
    }
});

// UPDATE DOCUMENT DETAILS
api.MapPut("/documents/{id:guid}", async (
    Guid id,
    UpdateDocumentRequest request,
    ClaimsPrincipal principal,
    AppDb db) =>
{
    var document = await db.Documents.FindAsync(id);

    if (document == null)
        return Results.NotFound();

    if (!await CanEdit(principal, document, db))
        return Results.Forbid();

    document.Name = request.Name;
    document.Category = request.Category;

    db.AuditLogs.Add(new AuditLog
    {
        UserId = principal.UserId(),
        DocumentId = id,
        Action = "EDIT",
        Detail = $"{principal.Identity?.Name} updated document details: {document.Name}"
    });

    await db.SaveChangesAsync();

    return Results.Ok(new { message = "Document updated." });
});

// DELETE DOCUMENT
api.MapDelete("/documents/{id:guid}", async (
    Guid id,
    ClaimsPrincipal principal,
    AppDb db) =>
{
    var document = await db.Documents.FindAsync(id);

    if (document == null)
        return Results.NotFound();

    if (principal.Role() != "Admin" &&
        document.OwnerId != principal.UserId())
        return Results.Forbid();

    var path = Path.Combine(uploadFolder, document.StorageName);

    if (File.Exists(path))
        File.Delete(path);

    db.AuditLogs.Add(new AuditLog
    {
        UserId = principal.UserId(),
        DocumentId = id,
        Action = "DELETE",
        Detail = $"{principal.Identity?.Name} deleted {document.Name}"
    });

    db.Documents.Remove(document);
    await db.SaveChangesAsync();

    return Results.NoContent();
});

// GRANT PERMISSION
api.MapPost("/documents/{id:guid}/permissions", async (
    Guid id,
    PermissionRequest request,
    ClaimsPrincipal principal,
    AppDb db) =>
{
    var document = await db.Documents.FindAsync(id);

    if (document == null)
        return Results.NotFound();

    if (principal.Role() != "Admin" &&
        document.OwnerId != principal.UserId())
        return Results.Forbid();

    if (document.OwnerId == request.UserId)
        return Results.BadRequest(
            "Cannot share with the document owner.");

    var targetUser = await db.Users.FindAsync(request.UserId);

    if (targetUser == null)
        return Results.NotFound("User not found.");

    var permission = await db.Permissions.FirstOrDefaultAsync(
        x => x.DocumentId == id && x.UserId == request.UserId);

    if (permission == null)
    {
        permission = new Permission
        {
            DocumentId = id,
            UserId = request.UserId
        };

        db.Permissions.Add(permission);
    }

    permission.CanRead = request.CanRead;
    permission.CanEdit = request.CanRead && request.CanEdit;

    db.AuditLogs.Add(new AuditLog
    {
        UserId = principal.UserId(),
        DocumentId = id,
        Action = "GRANT_OR_UPDATE_PERMISSION",
        Detail = $"{principal.Identity?.Name} updated access for {targetUser.Name} on {document.Name}"
    });

    await db.SaveChangesAsync();

    return Results.Ok(new { message = "Permission updated." });
});

// REVOKE PERMISSION
api.MapDelete("/documents/{id:guid}/permissions/{userId:guid}",
    async (
        Guid id,
        Guid userId,
        ClaimsPrincipal principal,
        AppDb db) =>
{
    var document = await db.Documents.FindAsync(id);

    if (document == null)
        return Results.NotFound();

    if (principal.Role() != "Admin" &&
        document.OwnerId != principal.UserId())
        return Results.Forbid();

    var permission = await db.Permissions.FirstOrDefaultAsync(
        x => x.DocumentId == id && x.UserId == userId);

    if (permission == null)
        return Results.NotFound();

    db.Permissions.Remove(permission);

    db.AuditLogs.Add(new AuditLog
    {
        UserId = principal.UserId(),
        DocumentId = id,
        Action = "REVOKE_PERMISSION",
        Detail = $"{principal.Identity?.Name} revoked access to {document.Name}"
    });

    await db.SaveChangesAsync();

    return Results.NoContent();
});

// ADMIN AUDIT LOG
api.MapGet("/audit", async (
    ClaimsPrincipal principal,
    AppDb db) =>
{
    if (principal.Role() != "Admin")
        return Results.Forbid();

    var logs = await db.AuditLogs
        .OrderByDescending(x => x.CreatedAt)
        .Select(x => new
        {
            id = x.Id,
            userId = x.UserId,
            documentId = x.DocumentId,
            action = x.Action,
            detail = x.Detail,
            createdAt = x.CreatedAt
        })
        .ToListAsync();

    return Results.Ok(logs);
});

app.Run();

// CHECK DOCUMENT READ PERMISSION
static async Task<bool> CanRead(
    ClaimsPrincipal principal,
    Document document,
    AppDb db)
{
    if (principal.Role() == "Admin" ||
        document.OwnerId == principal.UserId())
        return true;

    return await db.Permissions.AnyAsync(x =>
        x.DocumentId == document.Id &&
        x.UserId == principal.UserId() &&
        x.CanRead);
}

// CHECK DOCUMENT EDIT PERMISSION
static async Task<bool> CanEdit(
    ClaimsPrincipal principal,
    Document document,
    AppDb db)
{
    if (principal.Role() == "Admin" ||
        document.OwnerId == principal.UserId())
        return true;

    return await db.Permissions.AnyAsync(x =>
        x.DocumentId == document.Id &&
        x.UserId == principal.UserId() &&
        x.CanRead &&
        x.CanEdit);
}


using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace TrustVault.Api;

// CLAIMS EXTENSIONS
public static class ClaimsExt
{
    public static Guid UserId(this ClaimsPrincipal principal)
    {
        return Guid.Parse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier)!
        );
    }

    public static string Role(this ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.Role) ?? "";
    }
}

// REQUEST MODELS
public record RegisterRequest(
    string Name,
    string Email,
    string Password
);

public record LoginRequest(
    string Email,
    string Password
);

public record CreateUserRequest(
    string Name,
    string Email,
    string Password,
    string Role
);

public record UpdateDocumentRequest(
    string Name,
    string Category
);

public record PermissionRequest(
    Guid UserId,
    bool CanRead,
    bool CanEdit
);

// NEW: EDIT DOCUMENT CONTENT REQUEST
public record EditContentRequest(
    string Content
);

// USER MODEL
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public string Email { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public string Role { get; set; } = "Viewer";
}

// DOCUMENT MODEL
public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public string Category { get; set; } = "General";

    public string StorageName { get; set; } = "";

    public string ContentType { get; set; } =
        "application/octet-stream";

    public Guid OwnerId { get; set; }

    public User Owner { get; set; } = null!;

    public DateTime CreatedAt { get; set; } =
        DateTime.UtcNow;

    public List<Permission> Permissions { get; set; } = [];
}

// PERMISSION MODEL
public class Permission
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DocumentId { get; set; }

    public Guid UserId { get; set; }

    public bool CanRead { get; set; } = true;

    public bool CanEdit { get; set; }
}

// AUDIT LOG MODEL
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid? DocumentId { get; set; }

    public string Action { get; set; } = "";

    public string Detail { get; set; } = "";

    public DateTime CreatedAt { get; set; } =
        DateTime.UtcNow;
}

// DATABASE CONTEXT
public class AppDb(
    DbContextOptions<AppDb> options
) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<User>()
            .HasIndex(x => x.Email)
            .IsUnique();

        builder.Entity<Document>()
            .HasOne(x => x.Owner)
            .WithMany()
            .HasForeignKey(x => x.OwnerId);

        builder.Entity<Permission>()
            .HasIndex(x => new
            {
                x.DocumentId,
                x.UserId
            })
            .IsUnique();

        builder.Entity<Document>()
            .HasMany(x => x.Permissions)
            .WithOne()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using LankaMart.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Api.Data;

// ============================================================================
//  THE MOST IMPORTANT FILE IN THIS PROJECT.
//
//  SLIDE 25 - "ORM = Object-Relational Mapper: classes <-> tables,
//              properties <-> columns, objects <-> rows."
//
//  This file IS that mapping, written out explicitly. Read it beside
//  db/schema_reference.sql (the hand-written SQL from SLIDE 20) and you can
//  see the same schema expressed twice - once as C#, once as DDL. EF Core
//  generates the DDL from what is below when you run:
//
//      dotnet ef migrations add InitialCreate
//      dotnet ef database update
//
//  WHY SO MUCH CONFIGURATION, when EF Core has conventions?
//  Because the conventions would give us PascalCase tables ("Users") and
//  columns ("PasswordHash"), and SLIDE 19 asks for snake_case, plural table
//  names - the PostgreSQL house style. Writing it out also makes every
//  best-practice decision from Part 2 visible in one place, which is the
//  point for teaching. A production project would shorten this with the
//  EFCore.NamingConventions package (.UseSnakeCaseNamingConvention()).
// ============================================================================
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User>           Users           => Set<User>();
    public DbSet<Role>           Roles           => Set<Role>();
    public DbSet<Permission>     Permissions     => Set<Permission>();
    public DbSet<UserRole>       UserRoles       => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Category>       Categories      => Set<Category>();
    public DbSet<Product>        Products        => Set<Product>();
    public DbSet<Order>          Orders          => Set<Order>();
    public DbSet<OrderItem>      OrderItems      => Set<OrderItem>();
    public DbSet<RefreshToken>   RefreshTokens   => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ====================================================================
        //  users                                          (SLIDE 15, 20, 37)
        // ====================================================================
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);

            // SLIDE 14 - surrogate key. GENERATED ALWAYS AS IDENTITY is the
            // modern PostgreSQL form, preferred over SERIAL.
            // (Use UseIdentityByDefaultColumn() instead if you ever need to
            //  insert explicit ids, e.g. importing legacy data.)
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();

            e.Property(x => x.Name)
                .HasColumnName("name").HasColumnType("text").IsRequired();

            e.Property(x => x.Email)
                .HasColumnName("email").HasColumnType("text").IsRequired();

            e.Property(x => x.PasswordHash)
                .HasColumnName("password_hash").HasColumnType("text").IsRequired();

            e.Property(x => x.IsActive)
                .HasColumnName("is_active").HasDefaultValue(true);

            // SLIDE 19 - TIMESTAMPTZ, always, storing UTC. Sri Lanka is
            // UTC+05:30; naive timestamps are a classic production incident.
            e.Property(x => x.CreatedAt)
                .HasColumnName("created_at").HasColumnType("timestamptz")
                .HasDefaultValueSql("now()");

            // SLIDE 20 - UNIQUE on the natural key. This also creates the index
            // that makes the login query (WHERE email = $1) fast.
            e.HasIndex(x => x.Email).IsUnique().HasDatabaseName("ux_users_email");
        });

        // ====================================================================
        //  roles / permissions                                    (SLIDE 37)
        // ====================================================================
        b.Entity<Role>(e =>
        {
            e.ToTable("roles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.Name)
                .HasColumnName("name").HasColumnType("text").IsRequired();
            e.Property(x => x.Description)
                .HasColumnName("description").HasColumnType("text");

            e.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_roles_name");
        });

        b.Entity<Permission>(e =>
        {
            e.ToTable("permissions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.Code)
                .HasColumnName("code").HasColumnType("text").IsRequired();
            e.Property(x => x.Description)
                .HasColumnName("description").HasColumnType("text");

            e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_permissions_code");
        });

        // ====================================================================
        //  user_roles - the M:N junction                     (SLIDE 13, 37)
        //  "Same pattern twice!" - identical shape to order_items below.
        // ====================================================================
        b.Entity<UserRole>(e =>
        {
            e.ToTable("user_roles");

            // COMPOSITE primary key: also prevents assigning a role twice.
            e.HasKey(x => new { x.UserId, x.RoleId });

            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.RoleId).HasColumnName("role_id");
            e.Property(x => x.AssignedAt)
                .HasColumnName("assigned_at").HasColumnType("timestamptz")
                .HasDefaultValueSql("now()");

            // CASCADE: a role assignment has no meaning without its user.
            e.HasOne(x => x.User).WithMany(u => u.UserRoles)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // RESTRICT: refuse to delete a role that is still assigned to
            // someone. Silently un-permissioning users is a security event.
            e.HasOne(x => x.Role).WithMany(r => r.UserRoles)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

            // SLIDE 19/20 - PostgreSQL does NOT index foreign keys
            // automatically. The composite PK already indexes (user_id, role_id)
            // left-to-right, so lookups by user_id are covered but lookups by
            // role_id alone are not. Hence this index.
            e.HasIndex(x => x.RoleId).HasDatabaseName("idx_user_roles_role");
        });

        b.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions");
            e.HasKey(x => new { x.RoleId, x.PermissionId });

            e.Property(x => x.RoleId).HasColumnName("role_id");
            e.Property(x => x.PermissionId).HasColumnName("permission_id");

            e.HasOne(x => x.Role).WithMany(r => r.RolePermissions)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Permission).WithMany(p => p.RolePermissions)
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.PermissionId).HasDatabaseName("idx_role_permissions_permission");
        });

        // ====================================================================
        //  categories                                             (SLIDE 18)
        // ====================================================================
        b.Entity<Category>(e =>
        {
            e.ToTable("categories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.Name)
                .HasColumnName("name").HasColumnType("text").IsRequired();

            e.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_categories_name");
        });

        // ====================================================================
        //  products                                           (SLIDE 19, 20)
        // ====================================================================
        b.Entity<Product>(e =>
        {
            // Two CHECK constraints. SLIDE 19: "constraints = free
            // correctness" - tests that run on every INSERT and UPDATE,
            // forever, even when the application code has a bug.
            e.ToTable("products", t =>
            {
                t.HasCheckConstraint("ck_products_price_non_negative", "price >= 0");
                t.HasCheckConstraint("ck_products_stock_non_negative",  "stock_qty >= 0");
            });

            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();

            e.Property(x => x.Name)
                .HasColumnName("name").HasColumnType("text").IsRequired();

            // SLIDE 19 - NUMERIC for money, never FLOAT.
            e.Property(x => x.Price)
                .HasColumnName("price").HasColumnType("numeric(10,2)");

            e.Property(x => x.StockQty)
                .HasColumnName("stock_qty").HasDefaultValue(0);

            e.Property(x => x.CategoryId).HasColumnName("category_id");

            e.Property(x => x.SupplierCostNote)
                .HasColumnName("supplier_cost_note").HasColumnType("text")
                .HasDefaultValue("");

            e.Property(x => x.CreatedAt)
                .HasColumnName("created_at").HasColumnType("timestamptz")
                .HasDefaultValueSql("now()");

            e.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_products_name");

            // RESTRICT: deleting a category that still has products should
            // fail loudly rather than orphan or erase the catalogue.
            e.HasOne(x => x.Category).WithMany(c => c.Products)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.CategoryId).HasDatabaseName("idx_products_category");
        });

        // ====================================================================
        //  orders                                             (SLIDE 14, 15)
        // ====================================================================
        b.Entity<Order>(e =>
        {
            var statuses = string.Join(", ", OrderStatus.All.Select(s => $"'{s}'"));

            e.ToTable("orders", t =>
            {
                t.HasCheckConstraint("ck_orders_status", $"status IN ({statuses})");
                t.HasCheckConstraint("ck_orders_fees_non_negative",
                    "delivery_fee >= 0 AND surcharge >= 0");
            });

            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();

            e.Property(x => x.CustomerId).HasColumnName("customer_id");

            e.Property(x => x.Status)
                .HasColumnName("status").HasColumnType("text")
                .HasDefaultValue(OrderStatus.Pending).IsRequired();

            e.Property(x => x.PaymentMethod)
                .HasColumnName("payment_method").HasColumnType("text").IsRequired();

            e.Property(x => x.DeliveryFee)
                .HasColumnName("delivery_fee").HasColumnType("numeric(10,2)");

            e.Property(x => x.Surcharge)
                .HasColumnName("surcharge").HasColumnType("numeric(10,2)");

            e.Property(x => x.CreatedAt)
                .HasColumnName("created_at").HasColumnType("timestamptz")
                .HasDefaultValueSql("now()");

            // SLIDE 14 - RESTRICT. Deleting a customer who has orders must
            // fail; losing order history silently (CASCADE) is a business
            // disaster and, in most countries, illegal for tax records.
            e.HasOne(x => x.Customer).WithMany(u => u.Orders)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // SLIDE 20 - the explicit FK index. Every "my orders" page filters
            // on customer_id; without this index that becomes a full table
            // scan as soon as the table is real.
            e.HasIndex(x => x.CustomerId).HasDatabaseName("idx_orders_customer");

            // Computed in C#, not stored in PostgreSQL.
            e.Ignore(x => x.Subtotal);
            e.Ignore(x => x.Total);
        });

        // ====================================================================
        //  order_items - the M:N junction from Part 2          (SLIDE 15, 20)
        // ====================================================================
        b.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items", t =>
                t.HasCheckConstraint("ck_order_items_quantity_positive", "quantity > 0"));

            // The composite key that resolves orders <-> products.
            e.HasKey(x => new { x.OrderId, x.ProductId });

            e.Property(x => x.OrderId).HasColumnName("order_id");
            e.Property(x => x.ProductId).HasColumnName("product_id");
            e.Property(x => x.Quantity).HasColumnName("quantity");

            e.Property(x => x.UnitPrice)
                .HasColumnName("unit_price").HasColumnType("numeric(10,2)");

            // CASCADE is correct HERE and only here: an order line has no
            // meaning without its order. Contrast with orders -> users above.
            e.HasOne(x => x.Order).WithMany(o => o.Items)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // RESTRICT: a product that has been sold cannot be deleted. Its
            // rows are part of an invoice. Deactivate it instead.
            e.HasOne(x => x.Product).WithMany(p => p.OrderItems)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.ProductId).HasDatabaseName("idx_order_items_product");

            e.Ignore(x => x.LineTotal);
        });

        // ====================================================================
        //  refresh_tokens                                         (SLIDE 32)
        // ====================================================================
        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();

            e.Property(x => x.UserId).HasColumnName("user_id");

            e.Property(x => x.TokenHash)
                .HasColumnName("token_hash").HasColumnType("text").IsRequired();

            e.Property(x => x.FamilyId).HasColumnName("family_id");

            e.Property(x => x.CreatedAt)
                .HasColumnName("created_at").HasColumnType("timestamptz")
                .HasDefaultValueSql("now()");

            e.Property(x => x.ExpiresAt)
                .HasColumnName("expires_at").HasColumnType("timestamptz");

            e.Property(x => x.RevokedAt)
                .HasColumnName("revoked_at").HasColumnType("timestamptz");

            e.Property(x => x.ReplacedByTokenHash)
                .HasColumnName("replaced_by_token_hash").HasColumnType("text");

            e.Property(x => x.RevokedReason)
                .HasColumnName("revoked_reason").HasColumnType("text");

            // The lookup on every /auth/refresh call, so it must be indexed -
            // and unique, because a hash collision would be an auth bypass.
            e.HasIndex(x => x.TokenHash).IsUnique()
                .HasDatabaseName("ux_refresh_tokens_token_hash");

            // Theft detection revokes a whole family at once, so we query by it.
            e.HasIndex(x => x.FamilyId).HasDatabaseName("idx_refresh_tokens_family");

            // CASCADE: deleting a user must take their credentials with them.
            e.HasOne(x => x.User).WithMany(u => u.RefreshTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

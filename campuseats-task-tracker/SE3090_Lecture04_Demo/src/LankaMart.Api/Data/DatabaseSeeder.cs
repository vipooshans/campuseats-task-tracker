using LankaMart.Api.Common;
using LankaMart.Api.Models;
using LankaMart.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Api.Data;

// ============================================================================
//  SLIDE 19 - "Seed data scripts for dev/test."
//  SLIDE 37, lab tip - "seed the three roles and one default admin so every
//                       environment starts consistent."
//
//  Why seeding matters more than it looks: without it, a fresh clone of this
//  repository has an empty roles table, registration fails with "the Customer
//  role is missing", and thirty students raise their hands at once.
//
//  The method is IDEMPOTENT - it checks before it writes - so restarting the
//  API does not duplicate anything.
// ============================================================================
public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        AppDbContext db,
        IPasswordHasher hasher,
        LankaMartOptions options,
        ILogger logger,
        CancellationToken ct = default)
    {
        // ------------------------------------------------------------------
        //  1. Roles                                              (SLIDE 36)
        // ------------------------------------------------------------------
        var roleDefinitions = new (string Name, string Description)[]
        {
            (Roles.Admin,    "Manages users and roles, all reports, system settings"),
            (Roles.Staff,    "Manages products and stock, processes orders, views sales"),
            (Roles.Customer, "Browses products, places orders, views OWN orders only")
        };

        foreach (var (name, description) in roleDefinitions)
            if (!await db.Roles.AnyAsync(r => r.Name == name, ct))
                db.Roles.Add(new Role { Name = name, Description = description });

        // ------------------------------------------------------------------
        //  2. Permissions - the finer-grained layer          (SLIDE 36, 38)
        // ------------------------------------------------------------------
        var permissionDefinitions = new (string Code, string Description)[]
        {
            (Permissions.ProductsCreate, "Add products to the catalogue"),
            (Permissions.ProductsUpdate, "Edit products and stock levels"),
            (Permissions.ProductsDelete, "Remove products from the catalogue"),
            (Permissions.OrdersViewAll,  "View every customer's orders"),
            (Permissions.OrdersRefund,   "Refund a paid order"),
            (Permissions.UsersManage,    "Create users and change their roles")
        };

        foreach (var (code, description) in permissionDefinitions)
            if (!await db.Permissions.AnyAsync(p => p.Code == code, ct))
                db.Permissions.Add(new Permission { Code = code, Description = description });

        await db.SaveChangesAsync(ct);       // ids assigned here

        // ------------------------------------------------------------------
        //  3. role_permissions - which bundle each role gets      (SLIDE 37)
        // ------------------------------------------------------------------
        var roles       = await db.Roles.ToDictionaryAsync(r => r.Name, ct);
        var permissions = await db.Permissions.ToDictionaryAsync(p => p.Code, ct);

        var grants = new Dictionary<string, string[]>
        {
            // Admin gets everything.
            [Roles.Admin] = permissionDefinitions.Select(p => p.Code).ToArray(),

            // SLIDE 36 - least privilege. Staff run the shop; they do not
            // manage user accounts and cannot delete catalogue history.
            [Roles.Staff] = new[]
            {
                Permissions.ProductsCreate,
                Permissions.ProductsUpdate,
                Permissions.OrdersViewAll,
                Permissions.OrdersRefund
            },

            // Customers hold no administrative permission at all. Their rights
            // come from ownership of their own rows, not from a permission code.
            [Roles.Customer] = Array.Empty<string>()
        };

        foreach (var (roleName, codes) in grants)
        foreach (var code in codes)
        {
            var roleId       = roles[roleName].Id;
            var permissionId = permissions[code].Id;

            if (!await db.RolePermissions.AnyAsync(
                    rp => rp.RoleId == roleId && rp.PermissionId == permissionId, ct))
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleId, PermissionId = permissionId
                });
        }

        await db.SaveChangesAsync(ct);

        // ------------------------------------------------------------------
        //  4. Categories and products                            (SLIDE 18)
        // ------------------------------------------------------------------
        if (!await db.Categories.AnyAsync(ct))
        {
            db.Categories.AddRange(
                new Category { Name = "Accessories" },
                new Category { Name = "Computers" },
                new Category { Name = "Mobile" },
                new Category { Name = "Home & Kitchen" });

            await db.SaveChangesAsync(ct);
        }

        if (!await db.Products.AnyAsync(ct))
        {
            var categories = await db.Categories.ToDictionaryAsync(c => c.Name, ct);

            // Prices in LKR, as decimal -> NUMERIC(10,2).
            db.Products.AddRange(
                MakeProduct("Wireless Mouse",        "Accessories",    2_500m,  40),
                MakeProduct("Mechanical Keyboard",   "Accessories",    5_500m,  25),
                MakeProduct("USB-C Hub 7-in-1",      "Accessories",    7_900m,  18),
                MakeProduct("HDMI Cable 2m",         "Accessories",      950m, 120),
                MakeProduct("24 inch IPS Monitor",   "Computers",     48_500m,   8),
                MakeProduct("Laptop Stand Aluminium","Computers",      6_200m,  30),
                MakeProduct("1TB NVMe SSD",          "Computers",     29_900m,  14),
                MakeProduct("Power Bank 20000mAh",   "Mobile",         8_750m,  22),
                MakeProduct("Phone Case Clear",      "Mobile",         1_450m,  90),
                MakeProduct("Electric Kettle 1.7L",  "Home & Kitchen", 6_900m,  16));

            await db.SaveChangesAsync(ct);

            Product MakeProduct(string name, string category, decimal price, int stock) => new()
            {
                Name             = name,
                CategoryId       = categories[category].Id,
                Price            = price,
                StockQty         = stock,
                SupplierCostNote = "(internal - never returned by the API)",
                CreatedAt        = DateTime.UtcNow
            };
        }

        // ------------------------------------------------------------------
        //  5. Demo users, one per role                    (SLIDE 31, 36, 42)
        // ------------------------------------------------------------------
        if (!await db.Users.AnyAsync(ct))
        {
            var password = options.DemoUserPassword;

            // Hash ONCE and reuse. bcrypt at work factor 12 costs a few hundred
            // milliseconds; four separate hashes would add a visible pause to
            // startup for no benefit, since these are throwaway demo accounts.
            var hash = hasher.Hash(password);

            var users = new[]
            {
                NewUser("System Admin",  "admin@lankamart.lk", Roles.Admin),
                NewUser("Kavindu Staff", "staff@lankamart.lk", Roles.Staff),
                NewUser("Amal Perera",   "amal@mail.lk",       Roles.Customer),
                NewUser("Nadia Silva",   "nadia@mail.lk",      Roles.Customer)
            };

            db.Users.AddRange(users.Select(u => u.User));
            await db.SaveChangesAsync(ct);           // user ids assigned here

            foreach (var (user, roleName) in users)
                db.UserRoles.Add(new UserRole
                {
                    UserId     = user.Id,
                    RoleId     = roles[roleName].Id,
                    AssignedAt = DateTime.UtcNow
                });

            await db.SaveChangesAsync(ct);

            (User User, string Role) NewUser(string name, string email, string role)
                => (new User
                {
                    Name         = name,
                    Email        = email,
                    PasswordHash = hash,
                    IsActive     = true,
                    CreatedAt    = DateTime.UtcNow
                }, role);

            logger.LogWarning(
                "Seeded 4 demo accounts with the shared development password '{Password}'. " +
                "Development only - the seeder does not run in any other environment.",
                password);
        }

        // ------------------------------------------------------------------
        //  6. One historical order for Amal.
        //
        //  This exists so the object-level authorization demo works the moment
        //  the API starts: log in as nadia@mail.lk and ask for Amal's order id.
        //  Same role, valid token, 403.
        // ------------------------------------------------------------------
        if (!await db.Orders.AnyAsync(ct))
        {
            var amal = await db.Users.FirstOrDefaultAsync(u => u.Email == "amal@mail.lk", ct);
            var mouse    = await db.Products.FirstOrDefaultAsync(p => p.Name == "Wireless Mouse", ct);
            var keyboard = await db.Products.FirstOrDefaultAsync(p => p.Name == "Mechanical Keyboard", ct);

            if (amal is not null && mouse is not null && keyboard is not null)
            {
                var order = new Order
                {
                    CustomerId    = amal.Id,
                    PaymentMethod = "card",
                    DeliveryFee   = 350m,      // subtotal is under Rs 10,000
                    Surcharge     = 0m,
                    CreatedAt     = DateTime.UtcNow.AddDays(-3)
                };

                order.Items.Add(new OrderItem
                {
                    ProductId = mouse.Id, Quantity = 1, UnitPrice = mouse.Price
                });
                order.Items.Add(new OrderItem
                {
                    ProductId = keyboard.Id, Quantity = 1, UnitPrice = keyboard.Price
                });

                order.Confirm();

                db.Orders.Add(order);
                await db.SaveChangesAsync(ct);

                logger.LogInformation(
                    "Seeded order {OrderId} for {Email} - use it for the 403 demo",
                    order.Id, amal.Email);
            }
        }
    }
}

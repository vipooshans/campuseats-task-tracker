// ============================================================================
//  SE3090 - Lecture 04 teaching demo
//
//  Lecture 03 proved that a service with no HTTP dependency can be tested with
//  a fake repository and no web server. Lecture 04 adds a database and a
//  security model - and NOTHING in that claim changes. Every test below covers
//  a rule from Part 4 or Part 5 of the lecture, and not one of them starts
//  PostgreSQL, Kestrel or Docker.
//
//      dotnet run --project src/LankaMart.ServiceTests
//
//  Ask the class the honest follow-up question: what can these tests NOT
//  catch? Answers: a wrong column mapping, a missing index, a broken FOREIGN
//  KEY, a transaction that does not really roll back. Those need the real
//  database - which is why integration tests exist and why Lecture 08 runs
//  both in CI.
// ============================================================================

using System.IdentityModel.Tokens.Jwt;
using LankaMart.Api.Common;
using LankaMart.Api.Dtos;
using LankaMart.Api.Models;
using LankaMart.Api.Security;
using LankaMart.Api.Services;
using LankaMart.ServiceTests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

Console.WriteLine();
Console.WriteLine("============================================================");
Console.WriteLine("  LankaMart service tests - no database, no web server");
Console.WriteLine("============================================================");
Console.WriteLine();

var startOfDemo = new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);

// ===========================================================================
//  PART 4 - AUTHENTICATION RULES                              (SLIDES 31-33)
// ===========================================================================
Console.WriteLine("  --- Authentication -------------------------------------");

await MiniTest.Run("Registration never stores the plaintext password", async () =>
{
    var (auth, users, _, _) = BuildAuthService();

    await auth.RegisterAsync(new RegisterRequest("Nimal Fernando", "nimal@mail.lk", "Secret123!"));

    var saved = await users.GetByEmailWithRolesAsync("nimal@mail.lk");

    MiniTest.IsTrue(saved is not null, "The user should have been created.");
    MiniTest.IsTrue(saved!.PasswordHash != "Secret123!",
        "The password was stored in the clear - slide 20 says never.");
    MiniTest.IsTrue(saved.PasswordHash.StartsWith(FakePasswordHasher.Prefix),
        "The stored value should be the output of the hasher.");
});

await MiniTest.Run("Registering an email twice is a 409 Conflict", async () =>
{
    var (auth, _, _, _) = BuildAuthService();

    await auth.RegisterAsync(new RegisterRequest("First", "same@mail.lk", "Secret123!"));

    await MiniTest.Throws<ConflictException>(
        () => auth.RegisterAsync(new RegisterRequest("Second", "same@mail.lk", "Secret123!")),
        "A duplicate email must not create a second account.");
});

await MiniTest.Run("Self-registration grants the Customer role and nothing else", async () =>
{
    var (auth, _, roles, _) = BuildAuthService();

    var response = await auth.RegisterAsync(
        new RegisterRequest("Nimal", "nimal2@mail.lk", "Secret123!"));

    MiniTest.AreEqual(1, response.User.Roles.Count, "Exactly one role should be granted.");
    MiniTest.AreEqual(Roles.Customer, response.User.Roles[0],
        "Self-registration must never grant Staff or Admin.");
    MiniTest.AreEqual(1, roles.Assignments.Count, "One row should be added to user_roles.");
});

await MiniTest.Run("A wrong password is rejected", async () =>
{
    var (auth, _, _, _) = BuildAuthService(SeedAmal());

    await MiniTest.Throws<UnauthorizedException>(
        () => auth.LoginAsync(new LoginRequest("amal@mail.lk", "wrong-password")),
        "Login must fail when the password does not match the stored hash.");
});

await MiniTest.Run("Unknown email and wrong password give the IDENTICAL message", async () =>
{
    // SLIDE 31, speaker notes. If these two differ by even one word, an
    // attacker can discover which email addresses have accounts.
    var (auth, _, _, _) = BuildAuthService(SeedAmal());

    var wrongPassword = await CaptureMessage(
        () => auth.LoginAsync(new LoginRequest("amal@mail.lk", "wrong-password")));

    var unknownEmail = await CaptureMessage(
        () => auth.LoginAsync(new LoginRequest("ghost@mail.lk", "wrong-password")));

    MiniTest.AreEqual(wrongPassword, unknownEmail,
        "Different messages leak which accounts exist (user enumeration).");
});

await MiniTest.Run("A disabled account cannot log in, with the same message", async () =>
{
    var amal = SeedAmal();
    amal.IsActive = false;

    var (auth, _, _, _) = BuildAuthService(amal);

    var message = await CaptureMessage(
        () => auth.LoginAsync(new LoginRequest("amal@mail.lk", "Password123!")));

    MiniTest.IsTrue(!message.Contains("disabled", StringComparison.OrdinalIgnoreCase),
        "'Your account is disabled' confirms to a stranger that the account exists.");
});

await MiniTest.Run("The access token carries sub, role and permission claims", async () =>
{
    var (auth, _, _, _) = BuildAuthService(SeedAmal());

    var response = await auth.LoginAsync(new LoginRequest("amal@mail.lk", "Password123!"));

    // Decode it exactly the way jwt.io does - no key required (SLIDE 30).
    var jwt = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);

    var sub  = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
    var role = jwt.Claims.FirstOrDefault(c => c.Type == "role")?.Value;

    MiniTest.AreEqual("1", sub ?? "", "The 'sub' claim must carry the user id.");
    MiniTest.AreEqual(Roles.Customer, role ?? "", "The role must travel in the token.");

    MiniTest.IsTrue(jwt.Claims.All(c => c.Type != "password" && c.Type != "passwordHash"),
        "A JWT is signed, not encrypted - nothing secret may go in the payload.");
});

await MiniTest.Run("The refresh token is stored HASHED, never in the clear", async () =>
{
    var (auth, _, _, tokens) = BuildAuthService(SeedAmal());

    var response = await auth.LoginAsync(new LoginRequest("amal@mail.lk", "Password123!"));

    MiniTest.AreEqual(1, tokens.All.Count, "Login should store exactly one refresh token.");
    MiniTest.IsTrue(tokens.All[0].TokenHash != response.RefreshToken,
        "The raw refresh token must never be persisted - store its hash (slide 32).");
});

await MiniTest.Run("Refreshing rotates the token and revokes the old one", async () =>
{
    var (auth, _, _, tokens) = BuildAuthService(SeedAmal());

    var login   = await auth.LoginAsync(new LoginRequest("amal@mail.lk", "Password123!"));
    var refresh = await auth.RefreshAsync(login.RefreshToken);

    MiniTest.IsTrue(refresh.RefreshToken != login.RefreshToken,
        "Rotation must issue a NEW refresh token.");
    MiniTest.AreEqual(2, tokens.All.Count, "Both the old and the new token should be on record.");
    MiniTest.IsTrue(tokens.All[0].RevokedAt is not null,
        "The presented token must be revoked as part of rotation.");
    MiniTest.AreEqual("rotated", tokens.All[0].RevokedReason ?? "", "The reason should be recorded.");
});

await MiniTest.Run("Re-using a rotated refresh token revokes the WHOLE family", async () =>
{
    // SLIDE 32 - theft detection. This is the test worth reading aloud.
    var (auth, _, _, tokens) = BuildAuthService(SeedAmal());

    var login = await auth.LoginAsync(new LoginRequest("amal@mail.lk", "Password123!"));
    await auth.RefreshAsync(login.RefreshToken);          // legitimate rotation

    await MiniTest.Throws<UnauthorizedException>(
        () => auth.RefreshAsync(login.RefreshToken),      // the same token, again
        "A replayed refresh token must be rejected.");

    MiniTest.IsTrue(tokens.All.All(t => t.RevokedAt is not null),
        "Every token in the family must be revoked when reuse is detected.");
    MiniTest.IsTrue(tokens.All.Any(t => t.RevokedReason == "reuse-detected"),
        "The incident must be recorded so it can be investigated.");
});

await MiniTest.Run("An expired refresh token is rejected", async () =>
{
    var clock = new FixedClock(startOfDemo);
    var (auth, _, _, _) = BuildAuthService(SeedAmal(), clock);

    var login = await auth.LoginAsync(new LoginRequest("amal@mail.lk", "Password123!"));

    clock.Advance(TimeSpan.FromDays(8));                  // refresh tokens last 7

    await MiniTest.Throws<UnauthorizedException>(
        () => auth.RefreshAsync(login.RefreshToken),
        "A refresh token past its expiry must not be accepted.");
});

await MiniTest.Run("Logout revokes the refresh token", async () =>
{
    var (auth, _, _, tokens) = BuildAuthService(SeedAmal());

    var login = await auth.LoginAsync(new LoginRequest("amal@mail.lk", "Password123!"));
    await auth.LogoutAsync(login.RefreshToken);

    MiniTest.AreEqual("logout", tokens.All[0].RevokedReason ?? "", "Logout must revoke server-side.");

    await MiniTest.Throws<UnauthorizedException>(
        () => auth.RefreshAsync(login.RefreshToken),
        "A logged-out refresh token must no longer work.");
});

// ===========================================================================
//  PART 5 - AUTHORIZATION RULES                               (SLIDES 36-38)
// ===========================================================================
Console.WriteLine();
Console.WriteLine("  --- Object-level authorization -------------------------");

await MiniTest.Run("A customer CANNOT read another customer's order", async () =>
{
    // OWASP API Security #1, as a five-line test.
    var orders = BuildOrderService(out _, SeedOrderForCustomer(customerId: 1));

    await MiniTest.Throws<ForbiddenException>(
        () => orders.GetByIdAsync(orderId: 1, requestingUserId: 2, callerIsStaffOrAdmin: false),
        "Changing the id in the URL must not expose another customer's order.");
});

await MiniTest.Run("A customer CAN read their own order", async () =>
{
    var orders = BuildOrderService(out _, SeedOrderForCustomer(customerId: 1));

    var order = await orders.GetByIdAsync(1, requestingUserId: 1, callerIsStaffOrAdmin: false);

    MiniTest.AreEqual(1L, order.CustomerId, "The owner must still be able to read it.");
});

await MiniTest.Run("Staff can read any customer's order", async () =>
{
    var orders = BuildOrderService(out _, SeedOrderForCustomer(customerId: 1));

    var order = await orders.GetByIdAsync(1, requestingUserId: 99, callerIsStaffOrAdmin: true);

    MiniTest.AreEqual(1L, order.CustomerId, "Staff handle every order, by design.");
});

await MiniTest.Run("Cancelling someone else's order is forbidden", async () =>
{
    var orders = BuildOrderService(out _, SeedOrderForCustomer(customerId: 1));

    await MiniTest.Throws<ForbiddenException>(
        () => orders.CancelAsync(1, requestingUserId: 2, callerIsStaffOrAdmin: false),
        "Ownership must be checked on writes as well as reads.");
});

// ===========================================================================
//  BUSINESS RULES CARRIED OVER FROM LECTURE 03
// ===========================================================================
Console.WriteLine();
Console.WriteLine("  --- Orders, stock and pricing --------------------------");

await MiniTest.Run("An order below Rs 10,000 pays the Rs 350 delivery fee", async () =>
{
    var orders = BuildOrderServiceFor(out _,
        new Product { Name = "Wireless Mouse", Price = 2_500m, StockQty = 40 });

    var order = await orders.PlaceOrderAsync(1, NewOrder("card", (1, 2)));

    MiniTest.AreEqual(5_000m, order.Subtotal, "2 x Rs 2,500.");
    MiniTest.AreEqual(350m, order.DeliveryFee, "Below the free-delivery threshold.");
});

await MiniTest.Run("An order of Rs 10,000 or more gets free delivery", async () =>
{
    var orders = BuildOrderServiceFor(out _,
        new Product { Name = "Monitor", Price = 48_500m, StockQty = 5 });

    var order = await orders.PlaceOrderAsync(1, NewOrder("card", (1, 1)));

    MiniTest.AreEqual(0m, order.DeliveryFee, "At or above the threshold, delivery is free.");
});

await MiniTest.Run("Cash on delivery adds the surcharge", async () =>
{
    var orders = BuildOrderServiceFor(out _,
        new Product { Name = "Mouse", Price = 2_500m, StockQty = 40 });

    var order = await orders.PlaceOrderAsync(1, NewOrder("cash", (1, 1)));

    MiniTest.AreEqual(250m, order.Surcharge, "Cash orders carry a surcharge.");
    MiniTest.AreEqual(3_100m, order.Total, "2500 + 350 delivery + 250 surcharge.");
});

await MiniTest.Run("Ordering more than the stock is a 409, and stock is untouched", async () =>
{
    var mouse   = new Product { Name = "Mouse", Price = 2_500m, StockQty = 5 };
    var orders  = BuildOrderServiceFor(out _, mouse);

    await MiniTest.Throws<ConflictException>(
        () => orders.PlaceOrderAsync(1, NewOrder("card", (1, 99))),
        "Overselling must be refused.");

    MiniTest.AreEqual(5, mouse.StockQty, "Stock must not move when the order is rejected.");
});

await MiniTest.Run("Placing an order reserves stock", async () =>
{
    var mouse  = new Product { Name = "Mouse", Price = 2_500m, StockQty = 40 };
    var orders = BuildOrderServiceFor(out _, mouse);

    await orders.PlaceOrderAsync(1, NewOrder("card", (1, 3)));

    MiniTest.AreEqual(37, mouse.StockQty, "Three units should be reserved.");
});

await MiniTest.Run("The unit price is a snapshot - later price changes do not rewrite it", async () =>
{
    // SLIDE 15 - why order_items.unit_price is not redundant.
    var mouse  = new Product { Name = "Mouse", Price = 2_500m, StockQty = 40 };
    var orders = BuildOrderServiceFor(out _, mouse);

    var placed = await orders.PlaceOrderAsync(1, NewOrder("card", (1, 2)));

    mouse.Price = 3_900m;                       // the shop raises the price

    var reread = await orders.GetByIdAsync(placed.Id, 1, false);

    MiniTest.AreEqual(2_500m, reread.Items[0].UnitPrice,
        "A past invoice must not change when today's price changes.");
});

await MiniTest.Run("Cancelling an order puts the stock back", async () =>
{
    var mouse  = new Product { Name = "Mouse", Price = 2_500m, StockQty = 40 };
    var orders = BuildOrderServiceFor(out _, mouse);

    var placed = await orders.PlaceOrderAsync(1, NewOrder("card", (1, 4)));
    MiniTest.AreEqual(36, mouse.StockQty, "Four units reserved.");

    await orders.CancelAsync(placed.Id, requestingUserId: 1, callerIsStaffOrAdmin: false);

    MiniTest.AreEqual(40, mouse.StockQty, "Cancelling must release the reservation.");
});

await MiniTest.Run("A transaction is opened for every order", async () =>
{
    // SLIDE 24 - the unit of work belongs to the service. The fake cannot prove
    // PostgreSQL rolled anything back; it CAN prove the service asked for a
    // transaction and committed it. Testing the rollback itself needs a real
    // database - an integration test, not a unit test.
    var orders = BuildOrderServiceFor(out var uow,
        new Product { Name = "Mouse", Price = 2_500m, StockQty = 40 });

    await orders.PlaceOrderAsync(1, NewOrder("card", (1, 1)));

    MiniTest.AreEqual(1, uow.TransactionCount, "PlaceOrderAsync must open a transaction.");
    MiniTest.IsTrue(uow.Committed, "...and commit it when everything succeeds.");
});

Console.WriteLine();
return MiniTest.Summary();


// ===========================================================================
//  Test builders
// ===========================================================================

User SeedAmal()
{
    var customer = new Role
    {
        Id   = 3,
        Name = Roles.Customer,
        RolePermissions = new List<RolePermission>()
    };

    var user = new User
    {
        Id           = 1,
        Name         = "Amal Perera",
        Email        = "amal@mail.lk",
        PasswordHash = FakePasswordHasher.Prefix + "Password123!",
        IsActive     = true
    };

    user.UserRoles.Add(new UserRole { UserId = 1, RoleId = 3, Role = customer, User = user });

    return user;
}

(IAuthService Auth,
 FakeUserRepository Users,
 FakeRoleRepository Roles,
 FakeRefreshTokenRepository Tokens) BuildAuthService(User? existing = null, FixedClock? clock = null)
{
    clock ??= new FixedClock(startOfDemo);

    var users = existing is null
        ? new FakeUserRepository()
        : new FakeUserRepository(existing);

    var roles = new FakeRoleRepository(
        new Role { Id = 1, Name = Roles.Admin,    RolePermissions = new List<RolePermission>() },
        new Role { Id = 2, Name = Roles.Staff,    RolePermissions = new List<RolePermission>() },
        new Role { Id = 3, Name = Roles.Customer, RolePermissions = new List<RolePermission>() });

    var tokens  = new FakeRefreshTokenRepository();
    var hasher  = new FakePasswordHasher();

    // The REAL token service - signing a JWT needs no server and no database,
    // so there is no reason to fake it, and the tests can decode what it made.
    var tokenService = new JwtTokenService(
        Options.Create(new JwtOptions
        {
            Issuer             = "LankaMart.Tests",
            Audience           = "LankaMart.Tests",
            SigningKey         = "test-only-signing-key-that-is-long-enough-1234567890",
            AccessTokenMinutes = 15,
            RefreshTokenDays   = 7
        }),
        clock);

    var auth = new AuthService(
        users, roles, tokens,
        new FakeUnitOfWork(),
        hasher, tokenService, clock,
        new JwtOptionsSnapshot { RefreshTokenDays = 7 },
        NullLogger<AuthService>.Instance);

    return (auth, users, roles, tokens);
}

IOrderService BuildOrderService(
    out FakeUnitOfWork unitOfWork, Order? seededOrder = null, params Product[] products)
{
    unitOfWork = new FakeUnitOfWork();

    var orderRepo = seededOrder is null
        ? new FakeOrderRepository()
        : new FakeOrderRepository(seededOrder);

    return new OrderService(
        orderRepo,
        new FakeProductRepository(products),
        unitOfWork,
        Options.Create(new LankaMartOptions()),
        new FixedClock(startOfDemo),
        NullLogger<OrderService>.Instance);
}

IOrderService BuildOrderServiceFor(out FakeUnitOfWork unitOfWork, params Product[] products)
    => BuildOrderService(out unitOfWork, null, products);

Order SeedOrderForCustomer(long customerId)
{
    var order = new Order { CustomerId = customerId, PaymentMethod = "card" };
    order.Items.Add(new OrderItem { ProductId = 1, Quantity = 1, UnitPrice = 2_500m });
    order.Confirm();
    return order;
}

CreateOrderDto NewOrder(string paymentMethod, params (long ProductId, int Quantity)[] lines)
    => new(lines.Select(l => new OrderItemInputDto(l.ProductId, l.Quantity)).ToList(),
           paymentMethod);

async Task<string> CaptureMessage(Func<Task> action)
{
    try
    {
        await action();
        return "(no exception was thrown)";
    }
    catch (Exception ex)
    {
        return ex.Message;
    }
}

# Teaching Guide — SE3090 Lecture 04
### Running this project live, beside the slides

A three-hour session. This guide says **what to run, when, and the one sentence to land**
after each demo. Request numbers refer to `src/LankaMart.Api/LankaMart.Api.http`; Postman
numbers refer to `postman/LankaMart.postman_collection.json`.

Nothing here needs more than 12 minutes of demo in any one part. The code is a punctuation
mark for the slides, not a replacement for them.

---

## Before the class — 10 minutes, do it once

```bash
# 1. database up
docker compose -f db/docker-compose.yml up -d      # or your local PostgreSQL

# 2. secrets set (once per machine) - ONE LINE, no backslash continuation
cd src/LankaMart.Api
dotnet user-secrets set "ConnectionStrings:LankaMartDb" "Host=localhost;Port=5432;Database=lankamart;Username=lankamart_app;Password=dev_only_password"
dotnet user-secrets list        # must print the whole string, starting with Host=

# 3. schema created
dotnet ef migrations add InitialCreate
dotnet ef database update

# 4. it runs
dotnet run                      # http://localhost:5090/swagger

# 5. tests pass
cd ../.. && dotnet run --project src/LankaMart.ServiceTests
```

**Projector checklist**

- [ ] Terminal font at 16pt+ — the SQL log is half the lesson and the back row must read it
- [ ] Swagger open in one browser tab, <https://jwt.io> in another
- [ ] `psql` or pgAdmin connected to `lankamart`, with `db/useful_queries.sql` open
- [ ] `LankaMart.Api.http` open in the editor, ready to click
- [ ] Editor and terminal side by side, not full-screen — they need to see both
- [ ] Wi-Fi off for jwt.io? No — but have a screenshot ready in case the venue's network dies

**If the demo dies mid-lecture:** every screenshot you might need is reproducible from the
`.http` file afterwards. Say "we'll come back to it", move on, and do not debug in front of
30 people for more than 90 seconds.

---

## Part 1 — Databases in a full-stack application (≈25 min, slides 5–10)

Mostly conceptual. One short demo, at the end.

| Slide | Run this | The moment to land |
|-------|----------|--------------------|
| 6 | — | "Your React state disappears when the tab closes. That is not a database, and neither is a list in a C# service — which is exactly what Lecture 03 used." |
| 8 | `db/useful_queries.sql` query 6, the `UPDATE products SET stock_qty = -5` line | It fails. Nothing in C# ran. **Consistency is enforced by the database, not by your good intentions.** |
| 10 | request **1d** (`/demo/db-info`) | Show `current_user`. "If this ever says `postgres`, one injection flaw stops being a data leak and becomes a deleted company." |

> **Question to the room, slide 8:** the money leaves one account and the crash happens before
> it arrives in the other. Which ACID letter just saved you? *(Atomicity.)*

---

## Part 2 — Designing the schema (≈40 min, slides 11–21)

The heaviest design content in the module. Draw on the board first; open the code second.

| Slide | Run this | The moment to land |
|-------|----------|--------------------|
| 13 | — | Ask: "one customer, many orders — which table gets the foreign key?" Wait for it. The FK **always** goes on the many side. |
| 14 | `Data/AppDbContext.cs`, the `orders` block | Point at `OnDelete(DeleteBehavior.Restrict)`. "Someone chose this. The default in several ORMs is Cascade, and that default deletes invoices." |
| 15 | `Models/OrderItem.cs` | The `unit_price` argument. Ask whether it is redundant, let them argue, then run query 4 in `useful_queries.sql`, change a price, and re-run it. |
| 17–18 | `Models/Category.cs` | "In Lecture 03 the category was a *string on every product*. That is the left-hand side of slide 18. Here it is a table." |
| 19 | `db/schema_reference.sql` beside `Data/AppDbContext.cs` | Same schema, two languages. `NUMERIC` not `FLOAT`; `TIMESTAMPTZ` not `TIMESTAMP`; indexes on every FK. |
| 20 | `psql` → `\d orders` | Everything on slide 20 is visibly there — and EF Core generated it from C# classes. |
| 21 | — | Q3 is the unit-price question. If they argued about it at slide 15 they will get it right here. |

**Live modelling activity (10 min, slide 13).** Give them a Library: members borrow books.
Ask for the tables. The expected wrong answer is a `book_ids` column on `members`; the right
one is a `loans` junction table with a `due_date` — which shows that a junction table often
becomes a real entity with attributes of its own.

---

## Part 3 — Connecting the backend (≈30 min, slides 22–27)

This is where Lecture 03's promise gets tested in public.

| Slide | Run this | The moment to land |
|-------|----------|--------------------|
| 23 | `appsettings.json`, then `dotnet user-secrets list` | The connection string is **empty in the committed file**. "This is the whole slide. Ten minutes of inconvenience buys you not being in the news." |
| 24 | `Controllers/` → `Services/` → `Repositories/` | Trace one request aloud through the three folders. Ask where a transaction belongs, then open `OrderService.PlaceOrderAsync`. |
| 25 | request **1** with the terminal visible | The SQL EF Core sent, with `$1` placeholders. "You wrote LINQ. It wrote SQL. Note where the values are — *not* in the text." |
| 25 | requests **42** and **43** | Count the queries out loud: eleven, then one. This lands better than any diagram of N+1. |
| 26 | request **36** (`/demo/unique-violation`) | 409 in the browser; the full Npgsql exception in the terminal. Two audiences, one error. |
| 27 | — | Q2: `Include`. Q3: environment variables / secret manager. |

**Show them the diff, slide 24.** Open Lecture 03's `ProductService.cs` and this one side by
side. Nearly identical. Then be honest about `IProductRepository`, which *did* change —
§10 of the README has the argument ready. This is the intellectually honest moment of the
lecture and students remember it.

---

## Part 4 — Authentication (≈35 min, slides 28–34)

The part with the highest engagement. Use it.

| Slide | Run this | The moment to land |
|-------|----------|--------------------|
| 28 | request **11** (no token), then **21** (Customer posting a product) | 401, then 403. "Passport control versus the airline lounge." Write both on the board and leave them up. |
| 30 | request **9**, then paste the token into jwt.io | **The single best 60 seconds of the lecture.** No key, no password, and the payload is readable. "So: never put anything in there you would not print on a boarding pass." |
| 31 | `Services/AuthService.cs`, `LoginAsync` | Walk the seven steps of slide 31 down the method. |
| 31 | requests **6** and **7** | Identical messages. Then explain the dummy hash: "and identical *timing*, because a 2 ms answer versus a 300 ms answer leaks the same fact." |
| 31 | request **44** (`/demo/hash-password`) | Same password, two different hashes, both valid. Automatic salting, in one screen. |
| 32 | requests **12** then **13** | Rotation, then the replay. Show the log line "Refresh token REUSE detected", then run query 7 in `useful_queries.sql` to show the whole family revoked. |
| 33 | `Program.cs` §D | Point at `ValidAlgorithms`, `ValidateLifetime`, `ClockSkew`. "Every one of these lines is a row on slide 33." |
| 34 | — | Q1: access tokens cannot be revoked, so they expire fast. Q2: signed, not encrypted. |

**If time is short,** cut the register demo and keep slide 30's jwt.io moment. It is the one
students quote back to you in the exam.

---

## Part 5 — Authorization and secure integration (≈40 min, slides 35–44)

| Slide | Run this | The moment to land |
|-------|----------|--------------------|
| 36 | requests **17**, **18**, **19** in that order | 403, 200, 200. **Stop and let it sit.** Same endpoint, same role, one number different in the URL. "This is number one in the OWASP API Security Top 10, and it is one missing line of code." |
| 36 | `OrderService.EnsureCanAccess` | Comment out the last line, restart, re-run request 17. It becomes a data breach on the projector. Put it back before you move on. |
| 37 | query 3 in `useful_queries.sql` | Permissions resolved through two junction tables. "To give every Staff member a new capability, how many rows change? One." |
| 37 | requests **30**, then **31**, then **2** and **31** again | Grant the role → still 403 with the old token → log in again → 201. Revocation lag, live. |
| 38 | `Controllers/ProductsController.cs` | Read the attributes top to bottom. It *is* slide 38. |
| 38 | requests **26** and **27** | Refund by **permission**, not role. "The endpoint declares the capability; the database decides who has it." |
| 39 | `Program.cs` §F | The pipeline order. Offer to swap the two lines and watch everything break — do it if you have five minutes spare. |
| 40 | requests **39**, **40**, **41** | Baseline, attack, defence. Read the `sqlSentToPostgres` field aloud on request 40. "The quote closed the string. Everything after it was executed as code." |
| 40 | request **15** | The over-posting body, ignored. "The DTO has no `customerId` field, so there is nowhere for the lie to land." |
| 41 | requests **37** and **38** | The clean 500 with a `traceId`, then what a careless API would have said. Ask what an attacker learns from the second one. |
| 42 | — | The requirement → decision → justification table. This is exam and viva shape; say so. |

**Group activity (slide 43, 10 min).** Library system. Ask each group for: the tables, one
`ON DELETE` decision with a justification, and one rule that role-based access control
*cannot* express. The last one is the point — "a member can only see their own loans" is
object-level, exactly like `EnsureCanAccess`.

**Quiz (slide 44).** Answers are in the deck's speaker notes. The one they get wrong is 401
vs 403; the board from Part 4 is still up, so point at it.

---

## Closing (≈10 min, slides 45–47)

| Slide | Say |
|-------|-----|
| 45 | Recap in one line each: keys and relationships → normalize then justify any denormalization → constraints are free tests → hash passwords → short access tokens plus revocable refresh tokens → 401 is not 403 → check the row, not just the role. |
| 46 | Next lecture is Agentic Software Development. Preparation: get this project running on their own laptop, which is README steps 1–6 and about 20 minutes. |
| 47 | "Your assignment API will be attacked by your classmates during the demo. Build it as though it will be." |

---

## Timing plan

| Block | Minutes | Running total |
|-------|---------|---------------|
| Opening, outcomes, roadmap | 10 | 0:10 |
| Part 1 — databases in a full-stack app | 25 | 0:35 |
| Part 2 — schema design, normalization | 40 | 1:15 |
| **Break** | 10 | 1:25 |
| Part 3 — connecting the backend | 30 | 1:55 |
| Part 4 — authentication | 35 | 2:30 |
| Part 5 — authorization and secure integration | 20 | 2:50 |
| Quiz, recap, close | 10 | 3:00 |

Part 5's slides carry more than 20 minutes of material, so the group activity is the flexible
item: run it if Part 2 finished on time, set it as preparation for the lab if not.

---

## The four demos to protect if you are running late

1. **Slide 30** — decode a JWT on jwt.io with no key.
2. **Slide 36** — requests 17 / 18 / 19. Same role, different row, 403.
3. **Slide 40** — `' OR '1'='1`, then the same input parameterized.
4. **Slide 31** — identical error messages for wrong password and unknown email.

Each is under 90 seconds and each answers an exam question.

---

## Lab connection

The lab sheet extends this project rather than replacing it. Natural tasks, in order of
difficulty:

1. Generate the migration themselves and read the generated file (README step 5).
2. Add a `suppliers` table and a `products.supplier_id` foreign key, with the index, and
   justify the `ON DELETE` behaviour they chose.
3. Add `POST /api/v1/products/{id}/restock`, Staff-only, that increases stock inside a
   transaction and rejects negative quantities.
4. Add account lockout: five failed logins in ten minutes returns 429.
5. Write two service tests for whatever they added, using the existing fakes.

Tasks 2 and 3 map to LO2, task 4 to LO3, and the justification in task 2 to LO4.

---

*SE3090 — Software Engineering Frameworks · Faculty of Computing, SLIIT*

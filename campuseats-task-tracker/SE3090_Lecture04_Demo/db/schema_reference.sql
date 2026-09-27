-- ============================================================================
--  SE3090 Lecture 04 - REFERENCE SCHEMA
--
--  READ THIS FIRST: you do NOT need to run this file. EF Core creates the real
--  schema from AppDbContext.cs when you run:
--
--      dotnet ef migrations add InitialCreate
--      dotnet ef database update
--
--  This file exists so you can put the two side by side. On the left, the DDL
--  from slide 20, written by hand. On the right, Data/AppDbContext.cs, written
--  in C#. They describe the SAME database. Being able to move between the two
--  is most of what "backend developer" means in a job description.
--
--  (If you ever DO run this by hand, EF Core will then think the database is
--   empty of migrations and try to create everything again. Pick one route.)
-- ============================================================================


-- ---------------------------------------------------------------------------
--  IDENTITY AND ACCESS                                        (SLIDE 20, 37)
-- ---------------------------------------------------------------------------

CREATE TABLE users (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name          TEXT        NOT NULL,
    email         TEXT        NOT NULL,
    password_hash TEXT        NOT NULL,          -- bcrypt output, never a password
    is_active     BOOLEAN     NOT NULL DEFAULT TRUE,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

--  UNIQUE on the natural key. This also creates the index that makes the login
--  lookup (WHERE email = $1) fast - one constraint, two jobs.
CREATE UNIQUE INDEX ux_users_email ON users (email);


CREATE TABLE roles (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name        TEXT NOT NULL,                   -- Admin / Staff / Customer
    description TEXT
);
CREATE UNIQUE INDEX ux_roles_name ON roles (name);


CREATE TABLE permissions (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code        TEXT NOT NULL,                   -- e.g. 'orders.refund'
    description TEXT
);
CREATE UNIQUE INDEX ux_permissions_code ON permissions (code);


--  SLIDE 37 - users <-> roles is MANY-TO-MANY, so it needs a junction table.
--  The COMPOSITE primary key also prevents granting the same role twice.
CREATE TABLE user_roles (
    user_id     BIGINT      NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    role_id     BIGINT      NOT NULL REFERENCES roles (id) ON DELETE RESTRICT,
    assigned_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, role_id)
);

--  The composite PK already indexes (user_id, role_id) left to right, so
--  lookups BY USER are covered and lookups BY ROLE are not. Hence this index.
CREATE INDEX idx_user_roles_role ON user_roles (role_id);


CREATE TABLE role_permissions (
    role_id       BIGINT NOT NULL REFERENCES roles (id)       ON DELETE CASCADE,
    permission_id BIGINT NOT NULL REFERENCES permissions (id) ON DELETE CASCADE,
    PRIMARY KEY (role_id, permission_id)
);
CREATE INDEX idx_role_permissions_permission ON role_permissions (permission_id);


-- ---------------------------------------------------------------------------
--  CATALOGUE                                                  (SLIDE 18, 19)
-- ---------------------------------------------------------------------------

--  In Lecture 03 the category was a STRING on every product - the flat
--  spreadsheet from slide 18. Here the fact lives once and products point at it.
CREATE TABLE categories (
    id   BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name TEXT NOT NULL
);
CREATE UNIQUE INDEX ux_categories_name ON categories (name);


CREATE TABLE products (
    id                 BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name               TEXT           NOT NULL,

    --  SLIDE 19 - NUMERIC for money. NEVER FLOAT or DOUBLE: binary floating
    --  point cannot represent 0.10 exactly, and the rounding error compounds
    --  across an invoice run until an auditor finds it.
    price              NUMERIC(10, 2) NOT NULL,

    stock_qty          INTEGER        NOT NULL DEFAULT 0,

    --  NOT NULL on a mandatory foreign key. Forgetting this is a classic:
    --  the relationship silently becomes optional and orphan rows appear.
    category_id        BIGINT         NOT NULL REFERENCES categories (id) ON DELETE RESTRICT,

    supplier_cost_note TEXT           NOT NULL DEFAULT '',
    created_at         TIMESTAMPTZ    NOT NULL DEFAULT now(),

    --  SLIDE 19 - "constraints = free correctness". These run on every INSERT
    --  and UPDATE, forever, including the ones made by next year's intern.
    CONSTRAINT ck_products_price_non_negative CHECK (price >= 0),
    CONSTRAINT ck_products_stock_non_negative CHECK (stock_qty >= 0)
);

CREATE UNIQUE INDEX ux_products_name     ON products (name);

--  SLIDE 19 - PostgreSQL indexes PRIMARY KEYS automatically and FOREIGN KEYS
--  NOT AT ALL. Every "products in this category" query needs this.
CREATE INDEX        idx_products_category ON products (category_id);


-- ---------------------------------------------------------------------------
--  ORDERS                                                     (SLIDE 14, 15)
-- ---------------------------------------------------------------------------

CREATE TABLE orders (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    --  ONE customer has MANY orders, so the foreign key lives on the many side.
    --  ON DELETE RESTRICT: deleting a customer who has orders must FAIL. Order
    --  history is a financial record; CASCADE here would erase it silently.
    customer_id    BIGINT         NOT NULL REFERENCES users (id) ON DELETE RESTRICT,

    status         TEXT           NOT NULL DEFAULT 'PENDING',
    payment_method TEXT           NOT NULL,
    delivery_fee   NUMERIC(10, 2) NOT NULL DEFAULT 0,
    surcharge      NUMERIC(10, 2) NOT NULL DEFAULT 0,
    created_at     TIMESTAMPTZ    NOT NULL DEFAULT now(),

    CONSTRAINT ck_orders_status CHECK (
        status IN ('PENDING', 'CONFIRMED', 'DELIVERED', 'CANCELLED', 'REFUNDED')),
    CONSTRAINT ck_orders_fees_non_negative CHECK (delivery_fee >= 0 AND surcharge >= 0)
);

--  The index behind "my orders". Without it, that page is a sequential scan of
--  the whole orders table - fine with 20 rows, fatal with 2 million.
CREATE INDEX idx_orders_customer ON orders (customer_id);

--  NOTE what is NOT here: a total_amount column. The total is derived from the
--  order lines, and storing a derived value means keeping it in sync forever.
--  Denormalize later, if a measurement says you must (slide 17).


--  SLIDE 15 - orders <-> products is MANY-TO-MANY, resolved here.
CREATE TABLE order_items (
    order_id   BIGINT         NOT NULL REFERENCES orders (id)   ON DELETE CASCADE,
    product_id BIGINT         NOT NULL REFERENCES products (id) ON DELETE RESTRICT,
    quantity   INTEGER        NOT NULL,

    --  NOT redundant. products.price is "the price today"; this is "the price
    --  when this was sold". Without it, tomorrow's price change silently
    --  rewrites every invoice ever issued.
    unit_price NUMERIC(10, 2) NOT NULL,

    PRIMARY KEY (order_id, product_id),
    CONSTRAINT ck_order_items_quantity_positive CHECK (quantity > 0)
);

--  CASCADE above is correct HERE and nowhere else in this schema: an order line
--  has no meaning without its order. Compare with orders -> users.
CREATE INDEX idx_order_items_product ON order_items (product_id);


-- ---------------------------------------------------------------------------
--  REFRESH TOKENS                                                 (SLIDE 32)
-- ---------------------------------------------------------------------------

CREATE TABLE refresh_tokens (
    id                     BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id                BIGINT      NOT NULL REFERENCES users (id) ON DELETE CASCADE,

    --  SHA-256 of the token, hex encoded. The token itself is shown to the
    --  client once and never stored - same principle as password_hash.
    token_hash             TEXT        NOT NULL,

    --  Every token descended from one login shares this id, so a suspected
    --  theft can revoke the entire chain in a single UPDATE.
    family_id              UUID        NOT NULL,

    created_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at             TIMESTAMPTZ NOT NULL,
    revoked_at             TIMESTAMPTZ,
    replaced_by_token_hash TEXT,
    revoked_reason         TEXT
);

--  UNIQUE: a collision here would be an authentication bypass.
CREATE UNIQUE INDEX ux_refresh_tokens_token_hash ON refresh_tokens (token_hash);
CREATE INDEX        idx_refresh_tokens_family    ON refresh_tokens (family_id);

-- ============================================================================
--  Queries for the lecture demo. Run them in psql or pgAdmin while the API is
--  running, so the class sees the SAME data from two directions.
--
--      psql -h localhost -U lankamart_app -d lankamart
--
--  (In pgAdmin, open the Query Tool on the lankamart database.)
-- ============================================================================


-- ---------------------------------------------------------------------------
--  1. What did EF Core actually build?                            (SLIDE 25)
-- ---------------------------------------------------------------------------
-- In psql:
--     \dt              list tables
--     \d users         describe the users table, with indexes and constraints
--     \d+ order_items  the same, with more detail

SELECT table_name
FROM information_schema.tables
WHERE table_schema = 'public'
ORDER BY table_name;


-- ---------------------------------------------------------------------------
--  2. THE SLIDE 20 MOMENT: passwords are not in this database.
--     Project this. Nobody in the room can read anybody's password, including
--     you, including the database administrator.
-- ---------------------------------------------------------------------------
SELECT id, name, email, left(password_hash, 32) || '...' AS stored_value
FROM users
ORDER BY id;

-- Same password, different rows, different hashes - that is the automatic salt.
-- (True for accounts created through /auth/register; the four seeded demo
--  accounts share one hash because the seeder hashes once for speed.)


-- ---------------------------------------------------------------------------
--  3. RBAC, as data.                                              (SLIDE 37)
--     Who can do what, resolved through TWO junction tables.
-- ---------------------------------------------------------------------------
SELECT u.email,
       r.name AS role,
       string_agg(p.code, ', ' ORDER BY p.code) AS permissions
FROM users u
JOIN user_roles       ur ON ur.user_id       = u.id
JOIN roles            r  ON r.id             = ur.role_id
LEFT JOIN role_permissions rp ON rp.role_id  = r.id
LEFT JOIN permissions p  ON p.id             = rp.permission_id
GROUP BY u.email, r.name
ORDER BY u.email;

-- Ask the class: to give every Staff member a new capability, how many rows
-- change?  ONE, in role_permissions. Not one per employee.


-- ---------------------------------------------------------------------------
--  4. The M:N join that produces an invoice.                      (SLIDE 15)
-- ---------------------------------------------------------------------------
SELECT o.id                              AS order_id,
       u.name                            AS customer,
       p.name                            AS product,
       oi.quantity,
       oi.unit_price                     AS price_when_sold,
       p.price                           AS price_today,
       oi.quantity * oi.unit_price       AS line_total
FROM orders o
JOIN users       u  ON u.id  = o.customer_id
JOIN order_items oi ON oi.order_id = o.id
JOIN products    p  ON p.id  = oi.product_id
ORDER BY o.id, p.name;

--  Now change a product price through the API and re-run this. price_today
--  moves; price_when_sold does not. That column is the whole reason the
--  snapshot is not redundant.
-- UPDATE products SET price = 3900 WHERE name = 'Wireless Mouse';


-- ---------------------------------------------------------------------------
--  5. Does the index on the foreign key matter?                   (SLIDE 20)
--     Run this BEFORE and AFTER dropping the index and compare the plans.
-- ---------------------------------------------------------------------------
EXPLAIN ANALYZE
SELECT * FROM orders WHERE customer_id = 3;

--  With ten rows PostgreSQL will choose a sequential scan anyway - it is
--  cheaper. To make the difference visible, generate some volume first:
--
--      INSERT INTO orders (customer_id, status, payment_method, delivery_fee, surcharge)
--      SELECT 3, 'CONFIRMED', 'card', 350, 0 FROM generate_series(1, 200000);
--
--      EXPLAIN ANALYZE SELECT * FROM orders WHERE customer_id = 3;   -- Index Scan
--      DROP INDEX idx_orders_customer;
--      EXPLAIN ANALYZE SELECT * FROM orders WHERE customer_id = 3;   -- Seq Scan
--      CREATE INDEX idx_orders_customer ON orders (customer_id);
--
--  Compare the "Execution Time" lines. That is what an index is for, and why
--  PostgreSQL leaving foreign keys unindexed is worth remembering.


-- ---------------------------------------------------------------------------
--  6. Constraints defending the database from the application.    (SLIDE 19)
--     Every statement below SHOULD fail. Run them one at a time.
-- ---------------------------------------------------------------------------

-- 23514 check_violation - negative stock
-- UPDATE products SET stock_qty = -5 WHERE id = 1;

-- 23505 unique_violation - duplicate email
-- INSERT INTO users (name, email, password_hash) VALUES ('Copy', 'amal@mail.lk', 'x');

-- 23503 foreign_key_violation - a product that does not exist
-- INSERT INTO order_items (order_id, product_id, quantity, unit_price)
-- VALUES (1, 999999, 1, 100);

-- 23503 again - deleting a product that appears on an order (ON DELETE RESTRICT)
-- DELETE FROM products WHERE id = 1;

--  Each error code maps to an HTTP status in Middleware/GlobalExceptionHandler.cs.
--  Trigger them through the API too and compare what the client is told.


-- ---------------------------------------------------------------------------
--  7. The refresh-token family, after the theft demo.             (SLIDE 32)
--     Run request #12 then #13 in LankaMart.Api.http, then run this.
-- ---------------------------------------------------------------------------
SELECT id,
       user_id,
       left(token_hash, 16) || '...' AS token_hash,
       family_id,
       created_at,
       revoked_at,
       revoked_reason
FROM refresh_tokens
ORDER BY id;

--  Expect: one 'rotated', then everything in the family revoked with
--  'reuse-detected'. The raw tokens appear nowhere.


-- ---------------------------------------------------------------------------
--  8. Least privilege - which role is the API connected as?       (SLIDE 23)
-- ---------------------------------------------------------------------------
SELECT current_user, current_database(), version();

--  What CAN the application user do? If the answer includes DROP, the
--  connection string is over-privileged.
SELECT grantee, table_name, string_agg(privilege_type, ', ') AS privileges
FROM information_schema.role_table_grants
WHERE grantee = 'lankamart_app'
GROUP BY grantee, table_name
ORDER BY table_name;

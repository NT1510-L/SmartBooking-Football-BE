-- Clean, realistic demo data for local development and first Render deployment.
-- Password for all Manager accounts: AuditFootball2026!
-- Password for all User accounts: AuditFootball2026!
SET NAMES utf8mb4;
USE booking_db;

INSERT INTO users (email, passwordHash, fullName, role, isActive) VALUES
('manager.thuduc@smartbooking.demo', 'AQAAAAIAAYagAAAAEJpJ4B2z0hiqjjRcjXym6E6QiKKe+Oy2efE3DVATqMUCKmIf+RIeX7yoEDyzv9dPpA==', 'Lê Hoàng Nam', 'Manager', TRUE),
('manager.tanbinh@smartbooking.demo', 'AQAAAAIAAYagAAAAEJpJ4B2z0hiqjjRcjXym6E6QiKKe+Oy2efE3DVATqMUCKmIf+RIeX7yoEDyzv9dPpA==', 'Trần Bảo Ngọc', 'Manager', TRUE),
('manager.quan7@smartbooking.demo', 'AQAAAAIAAYagAAAAEJpJ4B2z0hiqjjRcjXym6E6QiKKe+Oy2efE3DVATqMUCKmIf+RIeX7yoEDyzv9dPpA==', 'Phạm Quốc Huy', 'Manager', TRUE),
('minh.anh@smartbooking.demo', 'AQAAAAIAAYagAAAAEDZATPB4mA5rRyQc5zjRyye778+xfXaWwsWvbUcKm64HUtLFTy0vUAIbvuIP76/FBg==', 'Nguyễn Minh Anh', 'User', TRUE),
('thao.nguyen@smartbooking.demo', 'AQAAAAIAAYagAAAAEDZATPB4mA5rRyQc5zjRyye778+xfXaWwsWvbUcKm64HUtLFTy0vUAIbvuIP76/FBg==', 'Nguyễn Phương Thảo', 'User', TRUE),
('hoang.long@smartbooking.demo', 'AQAAAAIAAYagAAAAEDZATPB4mA5rRyQc5zjRyye778+xfXaWwsWvbUcKm64HUtLFTy0vUAIbvuIP76/FBg==', 'Võ Hoàng Long', 'User', TRUE);

INSERT INTO branches (name, address, area, openingHour, closingHour, isActive) VALUES
('Green Field Thủ Đức', '18 Đường số 9, P. Hiệp Bình Chánh, TP. Thủ Đức', 'Thủ Đức', 6, 23, TRUE),
('Cộng Hòa Football Tân Bình', '120 Cộng Hòa, P. 4, Q. Tân Bình', 'Tân Bình', 6, 23, TRUE),
('Riverside Football Quận 7', '45 Nguyễn Hữu Thọ, P. Tân Hưng, Q. 7', 'Quận 7', 6, 23, TRUE);

SET @thuDucId = (SELECT id FROM branches WHERE name = 'Green Field Thủ Đức');
SET @tanBinhId = (SELECT id FROM branches WHERE name = 'Cộng Hòa Football Tân Bình');
SET @quan7Id = (SELECT id FROM branches WHERE name = 'Riverside Football Quận 7');
SET @managerThuDuc = (SELECT id FROM users WHERE email = 'manager.thuduc@smartbooking.demo');
SET @managerTanBinh = (SELECT id FROM users WHERE email = 'manager.tanbinh@smartbooking.demo');
SET @managerQuan7 = (SELECT id FROM users WHERE email = 'manager.quan7@smartbooking.demo');

INSERT INTO branchManagers (managerId, branchId) VALUES
(@managerThuDuc, @thuDucId), (@managerTanBinh, @tanBinhId), (@managerQuan7, @quan7Id);

INSERT INTO pitches (branchId, pitchNumber, name, isActive) VALUES
(@thuDucId, 1, 'Sân 5 người A', TRUE), (@thuDucId, 2, 'Sân 5 người B', TRUE),
(@thuDucId, 3, 'Sân 5 người C', TRUE), (@thuDucId, 4, 'Sân 7 người', TRUE),
(@tanBinhId, 1, 'Sân 5 người A', TRUE), (@tanBinhId, 2, 'Sân 5 người B', TRUE),
(@tanBinhId, 3, 'Sân 7 người A', TRUE), (@tanBinhId, 4, 'Sân 7 người B', TRUE),
(@quan7Id, 1, 'Sân 5 người 1', TRUE), (@quan7Id, 2, 'Sân 5 người 2', TRUE),
(@quan7Id, 3, 'Sân 7 người', TRUE), (@quan7Id, 4, 'Sân 11 người', TRUE);

USE pricing_db;
INSERT INTO pricingPolicies (branchId, effectiveFrom, regularPriceVnd, peakPriceVnd, peakStartHour, peakEndHour) VALUES
(@thuDucId, '2026-01-01', 220000, 300000, 18, 22),
(@tanBinhId, '2026-01-01', 250000, 340000, 18, 22),
(@quan7Id, '2026-01-01', 280000, 380000, 18, 22);

-- Database Update Script for Guidance Counselor Support
-- Run this script to add GuidanceCounselor support to your database

-- 1. Update the UserType enum in the user table
ALTER TABLE user 
MODIFY COLUMN UserType ENUM('Admin', 'Teacher', 'Student', 'GuidanceCounselor') NOT NULL;

-- 2. Verify the update
SELECT COLUMN_TYPE 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'user' 
AND COLUMN_NAME = 'UserType';

-- 3. Optional: Check if there are any existing users that might be affected
SELECT UserType, COUNT(*) as Count 
FROM user 
GROUP BY UserType;

-- 4. Test the enum by trying to insert a test record (optional - remove after testing)
-- INSERT INTO user (UserId, Username, Email, Password, UserType, IsActive, CreatedAt, UpdatedAt)
-- VALUES ('test-guidance-id', 'test_guidance', 'test@example.com', 'password123', 'GuidanceCounselor', true, NOW(), NOW());

-- 5. Clean up test record (uncomment if you ran the test insert)
-- DELETE FROM user WHERE UserId = 'test-guidance-id';

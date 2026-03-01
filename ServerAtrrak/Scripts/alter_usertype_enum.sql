-- Update user.UserType ENUM:
--   Teacher -> SubjectTeacher  (picks class offerings, takes subject attendance)
--   GuidanceCounselor stays   (counseling / student monitoring)
--   Advisor added             (creates section classes: subject + schedule; links students)
--
-- IMPORTANT: Run this script BEFORE restarting the server after code update.
-- Run after backup.

-- Step 1: Expand the ENUM safely (keep old Teacher, add SubjectTeacher and Advisor)
ALTER TABLE user
    MODIFY COLUMN UserType ENUM('Admin', 'Teacher', 'SubjectTeacher', 'Student', 'GuidanceCounselor', 'Advisor') NOT NULL;

-- Step 2: Migrate existing Teacher rows -> SubjectTeacher
UPDATE user SET UserType = 'SubjectTeacher' WHERE UserType = 'Teacher';

-- Step 3: Remove old Teacher value from ENUM (all rows already migrated in Step 2)
ALTER TABLE user
    MODIFY COLUMN UserType ENUM('Admin', 'SubjectTeacher', 'Student', 'GuidanceCounselor', 'Advisor') NOT NULL;

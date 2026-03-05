-- Database Migration: Rename Advisor to Adviser
-- Spelling Correction Refactor

-- 1. Update UserType ENUM in 'user' table
-- First expand to include Adviser, then migrate, then shrink.
ALTER TABLE user MODIFY COLUMN UserType ENUM('Admin', 'SubjectTeacher', 'Student', 'GuidanceCounselor', 'Advisor', 'Adviser', 'Head') NOT NULL;
UPDATE user SET UserType = 'Adviser' WHERE UserType = 'Advisor';
ALTER TABLE user MODIFY COLUMN UserType ENUM('Admin', 'SubjectTeacher', 'Student', 'GuidanceCounselor', 'Adviser', 'Head') NOT NULL;

-- 2. Rename AdvisorId to AdviserId in 'student' table
ALTER TABLE student CHANGE COLUMN AdvisorId AdviserId VARCHAR(255);

-- 3. Rename AdvisorId to AdviserId in 'class_offering' table
ALTER TABLE class_offering CHANGE COLUMN AdvisorId AdviserId VARCHAR(255) NOT NULL;

-- Note: Ensure permissions are correct and backup data before running.

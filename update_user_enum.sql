-- Update the UserType enum in the user table to include GuidanceCounselor
-- This script will modify the existing enum to include the new user type

-- First, let's check the current table structure
-- DESCRIBE user;

-- Update the UserType column to include GuidanceCounselor in the enum
ALTER TABLE user 
MODIFY COLUMN UserType ENUM('Admin', 'Teacher', 'Student', 'GuidanceCounselor') NOT NULL;

-- Verify the change
-- DESCRIBE user;

-- Guidance Counselor Account Creation Script (Final Version)
-- Optimized for existing school: KORONADAL NATIONAL COMPREHENSIVE HIGH SCHOOL

-- 1. Create the teacher record linked to the existing SchoolId
-- This avoids the foreign key constraint error.
INSERT IGNORE INTO teacher (TeacherId, FullName, Email, SchoolId, Gradelvl, Section, Strand)
VALUES (
    'teacher-guidance-001', 
    'MARIA SANTOS', -- Sample Name
    'guidance.maria@koronadal.edu.ph', 
    '1d543c73-eebc-4c2c-99ff-4beb2dbfc12f', -- Eto na po yung sa screenshot niyo
    0, 
    'Guidance', 
    NULL
);

-- 2. Create the user record linked to the teacher
INSERT IGNORE INTO user (UserId, Username, Email, Password, UserType, IsActive, IsApproved, CreatedAt, UpdatedAt, TeacherId)
VALUES (
    UUID(), 
    'guidance_account', -- Username for login
    'guidance.maria@koronadal.edu.ph', 
    'admin123', -- Password for login
    'GuidanceCounselor', 
    1, 
    1, 
    NOW(), 
    NOW(), 
    'teacher-guidance-001'
);

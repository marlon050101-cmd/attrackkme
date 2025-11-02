-- Check the actual schema of the school table
DESCRIBE school;

-- Check if there are any schools
SELECT 'All Schools:' as Status;
SELECT * FROM school LIMIT 5;

-- Check if there are any teachers
SELECT 'All Teachers:' as Status;
SELECT * FROM teacher LIMIT 5;

-- Check if there are any users with GuidanceCounselor type
SELECT 'Guidance Counselors:' as Status;
SELECT * FROM user WHERE UserType = 'GuidanceCounselor' LIMIT 5;

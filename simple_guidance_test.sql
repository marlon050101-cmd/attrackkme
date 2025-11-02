-- Simple test to check guidance counselor data
-- Check if the user exists
SELECT 'User Check:' as Status;
SELECT UserId, Username, UserType, TeacherId, IsActive 
FROM user 
WHERE UserId = '76f9b696-9149-4387-8b1d-06615c6fc777';

-- Check if the teacher record exists
SELECT 'Teacher Check:' as Status;
SELECT t.TeacherId, t.FullName, t.SchoolId
FROM teacher t
WHERE t.TeacherId = (
    SELECT TeacherId 
    FROM user 
    WHERE UserId = '76f9b696-9149-4387-8b1d-06615c6fc777'
);

-- Check if the school exists
SELECT 'School Check:' as Status;
SELECT s.SchoolId, s.SchoolName
FROM school s
WHERE s.SchoolId = (
    SELECT t.SchoolId
    FROM teacher t
    WHERE t.TeacherId = (
        SELECT TeacherId 
        FROM user 
        WHERE UserId = '76f9b696-9149-4387-8b1d-06615c6fc777'
    )
);

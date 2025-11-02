-- Test teacher data retrieval
SELECT 'Teacher Data Test:' as Status;

-- Check if teacher exists with the specific ID
SELECT u.UserId, u.Username, u.Email, u.UserType, u.TeacherId, t.FullName, t.Gradelvl, t.Section, t.SchoolId
FROM user u
LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777';

-- Check students in the same school and grade
SELECT 'Students in School and Grade:' as Status;
SELECT StudentId, FullName, GradeLevel, Section, SchoolId
FROM student 
WHERE SchoolId = (
    SELECT t.SchoolId 
    FROM user u
    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
    WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777'
)
AND GradeLevel = (
    SELECT t.Gradelvl 
    FROM user u
    LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
    WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777'
)
AND IsActive = 1;

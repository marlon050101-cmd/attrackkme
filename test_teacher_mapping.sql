-- Test teacher ID mapping
SELECT 'Teacher ID Mapping Test:' as Status;

-- Check if user exists and get TeacherId
SELECT u.UserId, u.Username, u.TeacherId, t.TeacherId as ActualTeacherId, t.FullName, t.SchoolId, t.Gradelvl, t.Section
FROM user u
LEFT JOIN teacher t ON u.TeacherId = t.TeacherId
WHERE u.UserId = '76f9b696-9149-4387-8b1d-06615c6fc777';

-- Check if the actual TeacherId exists in teacher table
SELECT 'Checking actual TeacherId in teacher table:' as Status;
SELECT TeacherId, FullName, SchoolId, Gradelvl, Section
FROM teacher 
WHERE TeacherId = '7c3f5cfc-8ad7-4e65-b058-896090328d63';

-- Check students in the same school and grade
SELECT 'Students in same school and grade:' as Status;
SELECT StudentId, FullName, GradeLevel, Section, SchoolId
FROM student 
WHERE SchoolId = (
    SELECT t.SchoolId 
    FROM teacher t
    WHERE t.TeacherId = '7c3f5cfc-8ad7-4e65-b058-896090328d63'
)
AND GradeLevel = (
    SELECT t.Gradelvl 
    FROM teacher t
    WHERE t.TeacherId = '7c3f5cfc-8ad7-4e65-b058-896090328d63'
)
AND IsActive = 1;
